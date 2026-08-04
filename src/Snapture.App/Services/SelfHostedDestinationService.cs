using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Snapture.App.Services;

public enum SelfHostedDestinationKind
{
    Nextcloud,
    Immich
}

public sealed class NextcloudDestinationSettings
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string RemoteFolder { get; set; } = "Snapture";

    public NextcloudDestinationSettings Clone() => new()
    {
        Enabled = Enabled,
        ServerUrl = ServerUrl,
        Username = Username,
        RemoteFolder = RemoteFolder
    };
}

public sealed class ImmichDestinationSettings
{
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = string.Empty;
    public string AlbumId { get; set; } = string.Empty;

    public ImmichDestinationSettings Clone() => new()
    {
        Enabled = Enabled,
        ServerUrl = ServerUrl,
        AlbumId = AlbumId
    };
}

public sealed record SelfHostedUploadRequest(
    byte[] PngBytes,
    string FileName,
    string Source,
    int Width,
    int Height,
    DateTime CapturedAtUtc);

public sealed record SelfHostedUploadResult(
    bool Succeeded,
    SelfHostedDestinationKind Destination,
    int StatusCode,
    string ResponseBody,
    string? ResourceUrl,
    string? ErrorMessage);

/// <summary>
/// First-party, opt-in connectors for user-controlled Nextcloud and Immich instances. Credentials
/// are kept in the current-user DPAPI store and never serialized into settings.json.
/// </summary>
public static class SelfHostedDestinationService
{
    private const string NextcloudIdentity = "builtin:self-hosted:nextcloud";
    private const string ImmichIdentity = "builtin:self-hosted:immich";
    private const string SecretKey = "credential";
    private const int MaxInputBytes = 100 * 1024 * 1024;
    private const int MaxResponseCharacters = 512 * 1024;
    private const int TimeoutSeconds = 120;

    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    });

    public static IReadOnlyList<SelfHostedDestinationKind> EnabledDestinations(SnaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var enabled = new List<SelfHostedDestinationKind>();
        if (settings.Nextcloud.Enabled) enabled.Add(SelfHostedDestinationKind.Nextcloud);
        if (settings.Immich.Enabled) enabled.Add(SelfHostedDestinationKind.Immich);
        return enabled;
    }

    public static void ValidateNextcloud(NextcloudDestinationSettings settings, string credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
            throw new InvalidOperationException("Nextcloud is disabled. Enable it in Settings before uploading.");
        ValidateServerUrl(settings.ServerUrl, "Nextcloud");
        if (string.IsNullOrWhiteSpace(settings.Username) || settings.Username.Length > 256)
            throw new ArgumentException("Nextcloud requires a username of 1-256 characters.", nameof(settings));
        if (string.IsNullOrWhiteSpace(credential))
            throw new ArgumentException("Nextcloud requires an app password or password.", nameof(credential));
        _ = NormalizeFolder(settings.RemoteFolder);
    }

    public static void ValidateImmich(ImmichDestinationSettings settings, string credential)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.Enabled)
            throw new InvalidOperationException("Immich is disabled. Enable it in Settings before uploading.");
        ValidateServerUrl(settings.ServerUrl, "Immich");
        if (string.IsNullOrWhiteSpace(credential))
            throw new ArgumentException("Immich requires an API key.", nameof(credential));
        if (settings.AlbumId.Length > 128 || settings.AlbumId.Any(char.IsControl))
            throw new ArgumentException("Immich album ID is invalid.", nameof(settings));
    }

    public static string? GetCredential(SelfHostedDestinationKind destination)
    {
        string identity = destination == SelfHostedDestinationKind.Nextcloud ? NextcloudIdentity : ImmichIdentity;
        try
        {
            using var store = new PluginSecretStore(PortableMode.LocalDataDirectory, identity);
            return store.TryGetSecret(SecretKey, out var value) ? value : null;
        }
        catch { return null; }
    }

    public static void SetCredential(SelfHostedDestinationKind destination, string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(credential);
        string identity = destination == SelfHostedDestinationKind.Nextcloud ? NextcloudIdentity : ImmichIdentity;
        using var store = new PluginSecretStore(PortableMode.LocalDataDirectory, identity);
        store.SetSecret(SecretKey, credential);
    }

    public static void RemoveCredential(SelfHostedDestinationKind destination)
    {
        string identity = destination == SelfHostedDestinationKind.Nextcloud ? NextcloudIdentity : ImmichIdentity;
        using var store = new PluginSecretStore(PortableMode.LocalDataDirectory, identity);
        store.RemoveSecret(SecretKey);
    }

    public static async Task<SelfHostedUploadResult> UploadNextcloudAsync(
        NextcloudDestinationSettings settings,
        string credential,
        SelfHostedUploadRequest request,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ValidateNextcloud(settings, credential);
        ValidateRequest(request);
        var baseUri = new Uri(settings.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute);
        string folder = NormalizeFolder(settings.RemoteFolder);
        var pathSegments = new[] { "remote.php", "dav", "files", settings.Username }
            .Concat(folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            .Append(request.FileName);
        string relative = string.Join('/', pathSegments
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(Uri.EscapeDataString));
        var uri = new Uri(baseUri, relative);
        using var message = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = new ByteArrayContent(request.PngBytes)
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Username + ":" + credential)));
        return await SendAsync(SelfHostedDestinationKind.Nextcloud, message, uri, httpClient, ct).ConfigureAwait(false);
    }

    public static async Task<SelfHostedUploadResult> UploadImmichAsync(
        ImmichDestinationSettings settings,
        string credential,
        SelfHostedUploadRequest request,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ValidateImmich(settings, credential);
        ValidateRequest(request);
        var baseUri = new Uri(settings.ServerUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var uri = new Uri(baseUri, "api/assets");
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent(request.PngBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        multipart.Add(file, "assetData", request.FileName);
        multipart.Add(new StringContent("snapture"), "deviceId");
        multipart.Add(new StringContent("snapture-" + Guid.NewGuid().ToString("N")), "deviceAssetId");
        multipart.Add(new StringContent(request.CapturedAtUtc.ToUniversalTime().ToString("O")), "fileCreatedAt");
        multipart.Add(new StringContent("false"), "isFavorite");
        using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = multipart };
        message.Headers.TryAddWithoutValidation("x-api-key", credential);
        var result = await SendAsync(SelfHostedDestinationKind.Immich, message, uri, httpClient, ct).ConfigureAwait(false);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(settings.AlbumId))
            return result;

        string? assetId = TryGetString(result.ResponseBody, "id");
        if (string.IsNullOrWhiteSpace(assetId))
            return result with { Succeeded = false, ErrorMessage = "Immich did not return an asset ID for album assignment." };

        var albumUri = new Uri(baseUri, "api/albums/" + Uri.EscapeDataString(settings.AlbumId) + "/assets");
        using var album = new HttpRequestMessage(HttpMethod.Put, albumUri)
        {
            Content = JsonContent.Create(new { ids = new[] { assetId } })
        };
        album.Headers.TryAddWithoutValidation("x-api-key", credential);
        var albumResult = await SendAsync(SelfHostedDestinationKind.Immich, album, albumUri, httpClient, ct).ConfigureAwait(false);
        return albumResult.Succeeded
            ? result with { ResourceUrl = new Uri(baseUri, "api/assets/" + Uri.EscapeDataString(assetId) + "/original").ToString() }
            : albumResult with { ErrorMessage = "Immich uploaded the asset, but album assignment failed: " + albumResult.ErrorMessage };
    }

    private static async Task<SelfHostedUploadResult> SendAsync(
        SelfHostedDestinationKind destination,
        HttpRequestMessage message,
        Uri requestUri,
        HttpClient? httpClient,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(TimeoutSeconds));
        HttpResponseMessage response;
        try
        {
            response = await (httpClient ?? DefaultHttp).SendAsync(message, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{destination} upload exceeded the {TimeoutSeconds}-second timeout.");
        }
        using (response)
        {
            string body = Limit(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
            return new SelfHostedUploadResult(
                response.IsSuccessStatusCode,
                destination,
                (int)response.StatusCode,
                body,
                null,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");
        }
    }

    private static void ValidateRequest(SelfHostedUploadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PngBytes.Length == 0 || request.PngBytes.Length > MaxInputBytes)
            throw new ArgumentException("The capture is empty or exceeds the 100 MB upload limit.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("The capture file name is invalid.", nameof(request));
    }

    private static void ValidateServerUrl(string value, string destination)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException($"{destination} server URL must be an absolute HTTP or HTTPS URL.", nameof(value));
    }

    private static string NormalizeFolder(string? folder)
    {
        var segments = (folder ?? string.Empty)
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if (segment is "." or ".." || segment.Any(char.IsControl))
                throw new ArgumentException("Remote folder cannot contain traversal or control characters.", nameof(folder));
        }
        return string.Join('/', segments);
    }

    private static string? TryGetString(string json, string property)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var item in document.RootElement.EnumerateObject())
                if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase))
                    return item.Value.ValueKind == JsonValueKind.String ? item.Value.GetString() : item.Value.ToString();
        }
        catch (JsonException) { }
        return null;
    }

    private static string Limit(string value) => value.Length <= MaxResponseCharacters
        ? value
        : value[..MaxResponseCharacters] + Environment.NewLine + "[response truncated]";
}
