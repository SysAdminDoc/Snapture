using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Snapture.App.Services;

public enum LocalAiProviderKind
{
    FoundryLocal,
    Ollama,
    LmStudio
}

public sealed record LocalAiModel(string Id, string? DisplayName = null)
{
    public string Label => string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName;

    /// <summary>
    /// A provider-advertised value takes precedence. When a provider omits capabilities,
    /// the conservative name heuristic only admits well-known vision model families.
    /// </summary>
    public bool? SupportsVision { get; init; }

    public string? Identity { get; init; }

    public string ModelIdentity => string.IsNullOrWhiteSpace(Identity) ? Id : Identity;

    public bool IsVisionCapable => SupportsVision ?? LocalAiModelCapabilityDetector.IsLikelyVisionModel(Id);
}

public sealed record LocalAiProviderInfo(
    LocalAiProviderKind Kind,
    string Key,
    string DisplayName,
    Uri? OpenAiBaseUri,
    bool IsAvailable,
    IReadOnlyList<LocalAiModel> Models,
    string Status)
{
    public LocalAiProviderCapabilities Capabilities { get; init; } = LocalAiProviderCapabilities.For(Kind);
}

public sealed record LocalAiModelChoice(LocalAiProviderInfo Provider, LocalAiModel Model)
{
    public string Reference => LocalAiProviderService.FormatModelReference(Provider, Model);

    public string DisplayLabel => string.Equals(Model.Label, Model.Id, StringComparison.Ordinal)
        ? Reference
        : $"{Reference} — {Model.Label}";
}

/// <summary>
/// Discovers opt-in local model runtimes without accepting arbitrary endpoints.
/// Every HTTP request is to a loopback address and cloud providers are deliberately
/// not represented by this service.
/// </summary>
public static class LocalAiProviderService
{
    public const string FoundryKey = "foundry";
    public const string OllamaKey = "ollama";
    public const string LmStudioKey = "lmstudio";

    private const int FoundryCommandTimeoutMs = 1200;
    private const int MaxDiscoveryResponseBytes = 256 * 1024;

    private static readonly Uri OllamaRoot = new("http://127.0.0.1:11434/");
    private static readonly Uri LmStudioRoot = new("http://127.0.0.1:1234/");

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(450),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = TimeSpan.FromMilliseconds(900)
    };

    private static readonly Regex EndpointRegex = new(
        @"https?://[^\s""'<>]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public static async Task<IReadOnlyList<LocalAiProviderInfo>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var providers = await Task.WhenAll(
            DiscoverFoundryLocalAsync(cancellationToken),
            DiscoverOllamaAsync(cancellationToken),
            DiscoverLmStudioAsync(cancellationToken));
        return providers;
    }

    public static string FormatModelReference(LocalAiProviderInfo provider, LocalAiModel model) =>
        $"{provider.Key}/{model.Id}";

    public static IReadOnlyList<LocalAiModelChoice> GetModelChoices(
        IEnumerable<LocalAiProviderInfo> providers) =>
        providers
            .Where(provider => provider.IsAvailable &&
                provider.Capabilities.SupportsVision &&
                provider.OpenAiBaseUri is not null)
            .SelectMany(provider => provider.Models
                .Where(model => model.IsVisionCapable)
                .Select(model => new LocalAiModelChoice(provider, model)))
            .ToArray();

    public static LocalAiModel? FindPreferredModel(LocalAiProviderInfo provider)
    {
        var preferred = provider.Kind switch
        {
            LocalAiProviderKind.Ollama => provider.Models.FirstOrDefault(model =>
                model.IsVisionCapable && IsLlavaModel(model)),
            LocalAiProviderKind.FoundryLocal => provider.Models.FirstOrDefault(model =>
                model.IsVisionCapable && IsPhiVisionModel(model)),
            _ => null
        };
        return preferred ?? provider.Models.FirstOrDefault(model => model.IsVisionCapable);
    }

    internal static IReadOnlyList<LocalAiModel> ParseOllamaModels(string json) =>
        ParseModels(json, "models", "name", "model");

    internal static IReadOnlyList<LocalAiModel> ParseOpenAiModels(string json) =>
        ParseModels(json, "data", "id", "name", "model");

    internal static IReadOnlyList<LocalAiModel> ParseFoundryModels(string json) =>
        ParseModels(json, "models", "data", "name", "id", "model", "alias");

    internal static IReadOnlyList<Uri> ParseLocalEndpoints(string text)
    {
        var endpoints = new List<Uri>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in EndpointRegex.Matches(text ?? string.Empty))
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}');
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
                !IsLoopbackHttpUri(uri))
            {
                continue;
            }

            var root = GetServiceRoot(uri);
            if (seen.Add(root.AbsoluteUri))
                endpoints.Add(root);
        }

        return endpoints;
    }

    internal static IReadOnlyList<Uri> ParseFoundryStatusEndpoints(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "endpoints", out var endpoints) ||
                endpoints.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<Uri>();
            }

            var result = new List<Uri>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in endpoints.EnumerateArray())
            {
                if (endpoint.ValueKind != JsonValueKind.String ||
                    !Uri.TryCreate(endpoint.GetString(), UriKind.Absolute, out var uri) ||
                    !IsLoopbackHttpUri(uri))
                {
                    continue;
                }

                var root = GetServiceRoot(uri);
                if (seen.Add(root.AbsoluteUri))
                    result.Add(root);
            }

            return result;
        }
        catch (JsonException)
        {
            return Array.Empty<Uri>();
        }
    }

    internal static bool IsLoopbackHttpUri(Uri? uri) =>
        uri is { IsAbsoluteUri: true, IsLoopback: true } &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static bool IsLlavaModel(LocalAiModel model) =>
        model.Id.Contains("llava", StringComparison.OrdinalIgnoreCase);

    private static bool IsPhiVisionModel(LocalAiModel model) =>
        model.Id.Contains("phi-3.5-vision", StringComparison.OrdinalIgnoreCase) ||
        model.Id.Contains("phi3.5-vision", StringComparison.OrdinalIgnoreCase) ||
        model.Id.Contains("phi_3.5_vision", StringComparison.OrdinalIgnoreCase);

    private static async Task<LocalAiProviderInfo> DiscoverOllamaAsync(CancellationToken cancellationToken)
    {
        var openAiBase = new Uri(OllamaRoot, "v1/");
        try
        {
            var json = await GetStringAsync(new Uri(OllamaRoot, "api/tags"), cancellationToken);
            return Available(
                LocalAiProviderKind.Ollama,
                OllamaKey,
                "Ollama",
                openAiBase,
                ParseOllamaModels(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable(LocalAiProviderKind.Ollama, OllamaKey, "Ollama", openAiBase);
        }
    }

    private static async Task<LocalAiProviderInfo> DiscoverLmStudioAsync(CancellationToken cancellationToken)
    {
        var openAiBase = new Uri(LmStudioRoot, "v1/");
        try
        {
            var json = await GetStringAsync(new Uri(LmStudioRoot, "v1/models"), cancellationToken);
            return Available(
                LocalAiProviderKind.LmStudio,
                LmStudioKey,
                "LM Studio",
                openAiBase,
                ParseOpenAiModels(json));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable(LocalAiProviderKind.LmStudio, LmStudioKey, "LM Studio", openAiBase);
        }
    }

    private static async Task<LocalAiProviderInfo> DiscoverFoundryLocalAsync(CancellationToken cancellationToken)
    {
        var output = await RunFoundryServiceStatusAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(output))
            return Unavailable(LocalAiProviderKind.FoundryLocal, FoundryKey, "Foundry Local", null);

        var candidates = ParseLocalEndpoints(output).ToList();
        foreach (var candidate in candidates.ToArray())
        {
            var statusJson = await TryGetStringAsync(new Uri(candidate, "openai/status"), cancellationToken);
            if (statusJson is null)
                continue;

            // Prefer the service's own endpoint list over CLI text when available.
            var statusEndpoints = ParseFoundryStatusEndpoints(statusJson);
            foreach (var statusEndpoint in statusEndpoints)
            {
                if (!candidates.Any(existing => string.Equals(
                        existing.AbsoluteUri,
                        statusEndpoint.AbsoluteUri,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add(statusEndpoint);
                }
            }

            var serviceRoot = statusEndpoints.FirstOrDefault() ?? candidate;
            var models = await ReadFoundryModelsAsync(serviceRoot, cancellationToken);
            return Available(
                LocalAiProviderKind.FoundryLocal,
                FoundryKey,
                "Foundry Local",
                new Uri(serviceRoot, "v1/"),
                models);
        }

        return Unavailable(LocalAiProviderKind.FoundryLocal, FoundryKey, "Foundry Local", null);
    }

    private static async Task<IReadOnlyList<LocalAiModel>> ReadFoundryModelsAsync(
        Uri serviceRoot,
        CancellationToken cancellationToken)
    {
        var json = await TryGetStringAsync(new Uri(serviceRoot, "openai/models"), cancellationToken);
        var models = json is null ? Array.Empty<LocalAiModel>() : ParseFoundryModels(json);
        if (models.Count > 0)
            return models;

        // Newer Foundry Local builds expose the standard OpenAI route as well.
        json = await TryGetStringAsync(new Uri(serviceRoot, "v1/models"), cancellationToken);
        return json is null ? Array.Empty<LocalAiModel>() : ParseOpenAiModels(json);
    }

    private static async Task<string?> RunFoundryServiceStatusAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "foundry",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        process.StartInfo.ArgumentList.Add("service");
        process.StartInfo.ArgumentList.Add("status");

        try
        {
            if (!process.Start())
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(FoundryCommandTimeoutMs);
            var stdoutTask = LocalAiResponseReader.ReadBoundedTextAsync(
                process.StandardOutput,
                MaxDiscoveryResponseBytes,
                timeout.Token);
            var stderrTask = LocalAiResponseReader.ReadBoundedTextAsync(
                process.StandardError,
                MaxDiscoveryResponseBytes,
                timeout.Token);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw;
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                return null;
            }

            var output = await Task.WhenAll(stdoutTask, stderrTask);
            return output[0] + Environment.NewLine + output[1];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            TryKill(process);
            return null;
        }
    }

    private static async Task<string> GetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await LocalAiResponseReader.ReadBoundedStringAsync(
            response.Content,
            MaxDiscoveryResponseBytes,
            cancellationToken);
    }

    private static async Task<string?> TryGetStringAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!IsLoopbackHttpUri(uri))
            return null;

        try
        {
            using var response = await Http.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            return await LocalAiResponseReader.ReadBoundedStringAsync(
                response.Content,
                MaxDiscoveryResponseBytes,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<LocalAiModel> ParseModels(
        string json,
        params string[] collectionPropertyNames)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            JsonElement collection;
            if (root.ValueKind == JsonValueKind.Array)
            {
                collection = root;
            }
            else
            {
                collection = default;
                foreach (var propertyName in collectionPropertyNames)
                {
                    if (TryGetPropertyIgnoreCase(root, propertyName, out var candidate) &&
                        candidate.ValueKind == JsonValueKind.Array)
                    {
                        collection = candidate;
                        break;
                    }
                }

                if (collection.ValueKind != JsonValueKind.Array)
                    return Array.Empty<LocalAiModel>();
            }

            var models = new List<LocalAiModel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in collection.EnumerateArray())
            {
                var model = ParseModel(item);
                if (model is not null && seen.Add(model.Id))
                    models.Add(model);
            }

            return models;
        }
        catch (JsonException)
        {
            return Array.Empty<LocalAiModel>();
        }
    }

    private static LocalAiModel? ParseModel(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
        {
            var textId = item.GetString()?.Trim();
            return string.IsNullOrWhiteSpace(textId) ? null : new LocalAiModel(textId);
        }

        if (item.ValueKind != JsonValueKind.Object)
            return null;

        string? id = null;
        foreach (var name in new[] { "id", "name", "model", "alias" })
        {
            if (TryGetPropertyIgnoreCase(item, name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                id = value.GetString()!.Trim();
                break;
            }
        }

        if (id is null)
            return null;

        string? displayName = null;
        foreach (var name in new[] { "displayName", "display_name", "label" })
        {
            if (TryGetPropertyIgnoreCase(item, name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                displayName = value.GetString()!.Trim();
                break;
            }
        }

        return new LocalAiModel(id, displayName)
        {
            SupportsVision = ReadVisionCapability(item),
            Identity = ReadModelIdentity(item)
        };
    }

    private static bool? ReadVisionCapability(JsonElement item)
    {
        foreach (var name in new[] { "vision", "supportsVision", "supports_vision", "vision_capable" })
        {
            if (!TryGetPropertyIgnoreCase(item, name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out bool parsed))
            {
                return parsed;
            }
        }

        foreach (var name in new[] { "modalities", "input_modalities", "capabilities" })
        {
            if (!TryGetPropertyIgnoreCase(item, name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Array)
            {
                bool sawValue = false;
                foreach (var modality in value.EnumerateArray())
                {
                    if (modality.ValueKind != JsonValueKind.String)
                        continue;
                    sawValue = true;
                    if (modality.GetString()?.Contains("vision", StringComparison.OrdinalIgnoreCase) == true ||
                        modality.GetString()?.Contains("image", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return true;
                    }
                }

                if (sawValue)
                    return false;
            }
            else if (value.ValueKind == JsonValueKind.Object)
            {
                var nested = ReadVisionCapability(value);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static string? ReadModelIdentity(JsonElement item)
    {
        foreach (var name in new[] { "digest", "sha256", "revision", "version" })
        {
            if (TryGetPropertyIgnoreCase(item, name, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString()!.Trim();
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static Uri GetServiceRoot(Uri uri) =>
        new UriBuilder(uri.Scheme, uri.Host, uri.Port).Uri;

    private static LocalAiProviderInfo Available(
        LocalAiProviderKind kind,
        string key,
        string displayName,
        Uri openAiBaseUri,
        IReadOnlyList<LocalAiModel> models) =>
        new(kind, key, displayName, openAiBaseUri, true, models,
            $"Detected · {models.Count} model{(models.Count == 1 ? string.Empty : "s")} · " +
            $"{models.Count(model => model.IsVisionCapable)} vision-capable");

    private static LocalAiProviderInfo Unavailable(
        LocalAiProviderKind kind,
        string key,
        string displayName,
        Uri? openAiBaseUri) =>
        new(kind, key, displayName, openAiBaseUri, false, Array.Empty<LocalAiModel>(), "Not detected");

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}
