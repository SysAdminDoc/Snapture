using System.Net.Http.Headers;
using System.Net.Http;
using System.Text.Json;

namespace Snapture.App.Services;

public sealed class LocalAiInferenceException : InvalidOperationException
{
    public LocalAiInferenceException(string message) : base(message) { }
}

/// <summary>
/// Sends one flattened PNG to a selected local OpenAI-compatible runtime.
/// The endpoint is validated as loopback before any request is created.
/// </summary>
public sealed class LocalAiInferenceService
{
    public const string DefaultPrompt =
        "Describe this capture concisely. Mention the important visible text, controls, and visual context.";

    private static readonly HttpClient DefaultHttp = CreateHttpClient();
    private readonly HttpClient _http;

    public LocalAiInferenceService(HttpClient? httpClient = null)
    {
        _http = httpClient ?? DefaultHttp;
    }

    public async Task<string> SendImageAsync(
        LocalAiModelChoice choice,
        ReadOnlyMemory<byte> pngBytes,
        string? prompt = null,
        CancellationToken cancellationToken = default)
    {
        if (choice.Provider.OpenAiBaseUri is not { } baseUri ||
            !LocalAiProviderService.IsLoopbackHttpUri(baseUri))
        {
            throw new LocalAiInferenceException("Local AI requests must target a loopback endpoint.");
        }

        if (pngBytes.IsEmpty)
            throw new LocalAiInferenceException("The flattened capture is empty.");

        var requestUri = new Uri(baseUri, "chat/completions");
        var payload = BuildRequestPayload(choice.Model.Id, pngBytes.Span, prompt);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var response = await _http.PostAsync(requestUri, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = ParseErrorMessage(responseBody);
            throw new LocalAiInferenceException(
                string.IsNullOrWhiteSpace(detail)
                    ? $"{choice.Reference} returned HTTP {(int)response.StatusCode}."
                    : $"{choice.Reference}: {detail}");
        }

        return ParseResponseText(responseBody);
    }

    internal static byte[] BuildRequestPayload(
        string modelId,
        ReadOnlySpan<byte> pngBytes,
        string? prompt)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("A model ID is required.", nameof(modelId));
        if (pngBytes.IsEmpty)
            throw new ArgumentException("PNG bytes are required.", nameof(pngBytes));

        var body = new
        {
            model = modelId,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt.Trim()
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = "data:image/png;base64," + Convert.ToBase64String(pngBytes)
                            }
                        }
                    }
                }
            },
            temperature = 0.2
        };

        return JsonSerializer.SerializeToUtf8Bytes(body);
    }

    internal static string ParseResponseText(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (TryGetPropertyIgnoreCase(root, "error", out var error))
        {
            var message = GetStringProperty(error, "message");
            throw new LocalAiInferenceException(message ?? "The local model returned an error.");
        }

        if (!TryGetPropertyIgnoreCase(root, "choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new LocalAiInferenceException("The local model returned no response choices.");
        }

        var choice = choices[0];
        if (!TryGetPropertyIgnoreCase(choice, "message", out var messageObject) ||
            !TryGetPropertyIgnoreCase(messageObject, "content", out var content))
        {
            throw new LocalAiInferenceException("The local model returned no message content.");
        }

        var text = ExtractContentText(content);
        if (string.IsNullOrWhiteSpace(text))
            throw new LocalAiInferenceException("The local model returned empty message content.");
        return text.Trim();
    }

    internal static string? ParseErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "error", out var error))
                return null;
            return GetStringProperty(error, "message");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ExtractContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in content.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                parts.Add(item.GetString() ?? string.Empty);
            }
            else if (item.ValueKind == JsonValueKind.Object &&
                     TryGetPropertyIgnoreCase(item, "text", out var text) &&
                     text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return TryGetPropertyIgnoreCase(element, propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
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

    private static HttpClient CreateHttpClient() => new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromMilliseconds(600),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2)
    })
    {
        Timeout = TimeSpan.FromMinutes(2)
    };
}
