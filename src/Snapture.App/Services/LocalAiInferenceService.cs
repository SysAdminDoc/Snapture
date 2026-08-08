using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace Snapture.App.Services;

/// <summary>
/// Sends one flattened PNG to a selected local OpenAI-compatible runtime.
/// The endpoint is validated as loopback before any request is created.
/// </summary>
public sealed class LocalAiInferenceService
{
    public const string DefaultPrompt =
        "Describe this capture concisely. Mention the important visible text, controls, and visual context.";

    private const int MaximumErrorDetailCharacters = 512;
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
        ArgumentNullException.ThrowIfNull(choice);

        var provider = choice.Provider;
        if (provider.OpenAiBaseUri is not { } baseUri ||
            !LocalAiProviderService.IsLoopbackHttpUri(baseUri))
        {
            throw CreateException(
                LocalAiInferenceErrorKind.InvalidEndpoint,
                "Local AI requests must target a loopback endpoint.",
                choice);
        }

        if (!provider.IsAvailable)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.ProviderUnavailable,
                $"{provider.DisplayName} is not available.",
                choice);
        }

        if (!provider.Capabilities.SupportsVision)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.VisionUnsupported,
                $"{provider.DisplayName} does not advertise image input support.",
                choice);
        }

        if (!choice.Model.IsVisionCapable)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.VisionUnsupported,
                $"{choice.Reference} does not advertise vision support.",
                choice);
        }

        var limits = provider.Capabilities.Limits;
        ValidatePng(pngBytes, limits, choice);
        string effectivePrompt = string.IsNullOrWhiteSpace(prompt) ? DefaultPrompt : prompt.Trim();
        if (effectivePrompt.Length > limits.MaxPromptCharacters)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.RequestTooLarge,
                $"The local AI instruction exceeds the {limits.MaxPromptCharacters:N0}-character limit.",
                choice);
        }

        EnsureRequestBudget(pngBytes.Length, effectivePrompt, limits, choice);
        var payload = BuildRequestPayload(choice.Model.Id, pngBytes.Span, effectivePrompt);
        if (payload.Length > limits.MaxRequestBytes)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.RequestTooLarge,
                $"The local AI request exceeded the {limits.MaxRequestBytes:N0}-byte limit.",
                choice);
        }

        var requestUri = new Uri(baseUri, "chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new ByteArrayContent(payload)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        using var timeout = new CancellationTokenSource(limits.Timeout);
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                requestCancellation.Token).ConfigureAwait(false);
            string responseBody;
            try
            {
                responseBody = await LocalAiResponseReader.ReadBoundedStringAsync(
                    response.Content,
                    limits.MaxResponseBytes,
                    requestCancellation.Token).ConfigureAwait(false);
            }
            catch (LocalAiResponseTooLargeException ex)
            {
                throw CreateException(
                    LocalAiInferenceErrorKind.ResponseTooLarge,
                    ex.Message,
                    choice,
                    innerException: ex);
            }

            if (!response.IsSuccessStatusCode)
                throw CreateProviderException(choice, response.StatusCode, responseBody);

            return ParseResponseText(responseBody);
        }
        catch (LocalAiInferenceException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.Cancelled,
                "The local AI request was canceled.",
                choice,
                innerException: ex);
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.Timeout,
                $"The local AI request exceeded its {limits.Timeout.TotalSeconds:N0}-second timeout.",
                choice,
                innerException: ex);
        }
        catch (OperationCanceledException ex)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.Timeout,
                "The local AI request timed out.",
                choice,
                innerException: ex);
        }
        catch (HttpRequestException ex)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.Transport,
                "Local AI HTTP request failed: " + RedactAndLimit(ex.Message),
                choice,
                innerException: ex);
        }
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
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new LocalAiInferenceException(
                LocalAiInferenceErrorKind.InvalidResponse,
                "The local model returned invalid JSON.",
                innerException: ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (TryGetPropertyIgnoreCase(root, "error", out var error))
            {
                var message = GetStringProperty(error, "message");
                throw new LocalAiInferenceException(
                    LocalAiInferenceErrorKind.ProviderError,
                    RedactAndLimit(message ?? "The local model returned an error."),
                    providerErrorCode: GetStringProperty(error, "code"),
                    providerErrorType: GetStringProperty(error, "type"));
            }

            if (!TryGetPropertyIgnoreCase(root, "choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                throw new LocalAiInferenceException(
                    LocalAiInferenceErrorKind.InvalidResponse,
                    "The local model returned no response choices.");
            }

            var choice = choices[0];
            if (!TryGetPropertyIgnoreCase(choice, "message", out var messageObject) ||
                !TryGetPropertyIgnoreCase(messageObject, "content", out var content))
            {
                throw new LocalAiInferenceException(
                    LocalAiInferenceErrorKind.InvalidResponse,
                    "The local model returned no message content.");
            }

            var text = ExtractContentText(content);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new LocalAiInferenceException(
                    LocalAiInferenceErrorKind.InvalidResponse,
                    "The local model returned empty message content.");
            }

            return text.Trim();
        }
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

    private static void ValidatePng(
        ReadOnlyMemory<byte> pngBytes,
        LocalAiProviderLimits limits,
        LocalAiModelChoice choice)
    {
        if (pngBytes.IsEmpty)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.InvalidImage,
                "The flattened capture is empty.",
                choice);
        }

        if (pngBytes.Length > limits.MaxImageBytes)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.ImageTooLarge,
                $"The flattened PNG exceeds the {limits.MaxImageBytes / (1024 * 1024):N0}-MB image limit.",
                choice);
        }

        try
        {
            using var stream = new MemoryStream(pngBytes.ToArray(), writable: false);
            using var codec = SKCodec.Create(stream, out var codecResult);
            if (codec is null || codecResult != SKCodecResult.Success || codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                throw CreateException(
                    LocalAiInferenceErrorKind.InvalidImage,
                    "The local AI input is not a complete, decodable PNG.",
                    choice);
            }

            int width = codec.Info.Width;
            int height = codec.Info.Height;
            if (width <= 0 || height <= 0 ||
                width > limits.MaxImageWidth || height > limits.MaxImageHeight ||
                (long)width * height > limits.MaxImagePixels)
            {
                throw CreateException(
                    LocalAiInferenceErrorKind.ImageDimensionsExceeded,
                    $"The flattened PNG dimensions exceed the {limits.MaxImageWidth:N0}×{limits.MaxImageHeight:N0} / " +
                    $"{limits.MaxImagePixels:N0}-pixel limit.",
                    choice);
            }
        }
        catch (LocalAiInferenceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.InvalidImage,
                "The local AI input could not be decoded as a PNG.",
                choice,
                innerException: ex);
        }
    }

    private static void EnsureRequestBudget(
        int imageBytes,
        string prompt,
        LocalAiProviderLimits limits,
        LocalAiModelChoice choice)
    {
        long encodedImageBytes = checked(((long)imageBytes + 2) / 3 * 4);
        long estimatedRequestBytes = encodedImageBytes + Encoding.UTF8.GetByteCount(prompt) + 4 * 1024;
        if (estimatedRequestBytes > limits.MaxRequestBytes)
        {
            throw CreateException(
                LocalAiInferenceErrorKind.RequestTooLarge,
                $"The encoded local AI request exceeds the {limits.MaxRequestBytes:N0}-byte limit.",
                choice);
        }
    }

    private static LocalAiInferenceException CreateProviderException(
        LocalAiModelChoice choice,
        HttpStatusCode statusCode,
        string responseBody)
    {
        var message = ParseErrorMessage(responseBody);
        var safeMessage = RedactAndLimit(
            string.IsNullOrWhiteSpace(message)
                ? $"{choice.Reference} returned HTTP {(int)statusCode}."
                : message);
        var kind = ClassifyProviderError(statusCode, message);
        var details = TryParseErrorDetails(responseBody);
        return CreateException(
            kind,
            $"{choice.Reference}: {safeMessage}",
            choice,
            statusCode,
            details.Code,
            details.Type);
    }

    private static LocalAiInferenceErrorKind ClassifyProviderError(
        HttpStatusCode statusCode,
        string? message)
    {
        if (statusCode == HttpStatusCode.NotFound ||
            message?.Contains("model", StringComparison.OrdinalIgnoreCase) == true &&
            (message.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            return LocalAiInferenceErrorKind.ModelUnavailable;
        }

        if (message?.Contains("vision", StringComparison.OrdinalIgnoreCase) == true ||
            message?.Contains("image", StringComparison.OrdinalIgnoreCase) == true)
        {
            return LocalAiInferenceErrorKind.VisionUnsupported;
        }

        return LocalAiInferenceErrorKind.ProviderError;
    }

    private static (string? Code, string? Type) TryParseErrorDetails(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!TryGetPropertyIgnoreCase(document.RootElement, "error", out var error))
                return (null, null);
            return (GetStringProperty(error, "code"), GetStringProperty(error, "type"));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static LocalAiInferenceException CreateException(
        LocalAiInferenceErrorKind kind,
        string message,
        LocalAiModelChoice choice,
        HttpStatusCode? statusCode = null,
        string? providerErrorCode = null,
        string? providerErrorType = null,
        Exception? innerException = null) =>
        new(
            kind,
            message,
            statusCode,
            choice.Provider.Key,
            choice.Model.Id,
            choice.Model.ModelIdentity,
            providerErrorCode,
            providerErrorType,
            innerException);

    private static string RedactAndLimit(string message)
    {
        var redacted = OutboundDataFlowAudit.RedactSensitive(message);
        return redacted.Length <= MaximumErrorDetailCharacters
            ? redacted
            : redacted[..MaximumErrorDetailCharacters] + "…";
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
