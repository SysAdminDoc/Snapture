using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Snapture.App.Services;

public enum LocalAiInferenceErrorKind
{
    InvalidEndpoint,
    ProviderUnavailable,
    ModelUnavailable,
    VisionUnsupported,
    InvalidImage,
    ImageTooLarge,
    ImageDimensionsExceeded,
    RequestTooLarge,
    ResponseTooLarge,
    InvalidResponse,
    ProviderError,
    Timeout,
    Cancelled,
    Transport
}

public sealed class LocalAiInferenceException : InvalidOperationException
{
    public LocalAiInferenceException(string message)
        : this(LocalAiInferenceErrorKind.InvalidResponse, message)
    {
    }

    public LocalAiInferenceException(
        LocalAiInferenceErrorKind kind,
        string message,
        HttpStatusCode? statusCode = null,
        string? providerKey = null,
        string? modelId = null,
        string? modelIdentity = null,
        string? providerErrorCode = null,
        string? providerErrorType = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ProviderKey = providerKey;
        ModelId = modelId;
        ModelIdentity = modelIdentity ?? modelId;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorType = providerErrorType;
    }

    public LocalAiInferenceErrorKind Kind { get; }

    public HttpStatusCode? StatusCode { get; }

    public string? ProviderKey { get; }

    public string? ModelId { get; }

    public string? ModelIdentity { get; }

    public string? ProviderErrorCode { get; }

    public string? ProviderErrorType { get; }

    public bool IsTransient => Kind is LocalAiInferenceErrorKind.Timeout or LocalAiInferenceErrorKind.Transport ||
        (Kind == LocalAiInferenceErrorKind.ProviderError &&
         StatusCode is >= HttpStatusCode.InternalServerError);
}

public sealed record LocalAiProviderLimits(
    int MaxImageBytes,
    int MaxImageWidth,
    int MaxImageHeight,
    long MaxImagePixels,
    int MaxRequestBytes,
    int MaxResponseBytes,
    int MaxPromptCharacters,
    TimeSpan Timeout)
{
    public static LocalAiProviderLimits Default { get; } = new(
        MaxImageBytes: 8 * 1024 * 1024,
        MaxImageWidth: 8_192,
        MaxImageHeight: 8_192,
        MaxImagePixels: 50_000_000,
        MaxRequestBytes: 16 * 1024 * 1024,
        MaxResponseBytes: 1 * 1024 * 1024,
        MaxPromptCharacters: 8_192,
        Timeout: TimeSpan.FromMinutes(2));
}

public sealed record LocalAiProviderCapabilities(
    bool SupportsVision,
    string Protocol,
    string ModelIdentitySource,
    LocalAiProviderLimits Limits)
{
    public static LocalAiProviderCapabilities For(LocalAiProviderKind kind) => new(
        SupportsVision: true,
        Protocol: "OpenAI-compatible chat completions",
        ModelIdentitySource: kind switch
        {
            LocalAiProviderKind.Ollama => "Ollama model id or digest",
            LocalAiProviderKind.LmStudio => "OpenAI model id",
            LocalAiProviderKind.FoundryLocal => "Foundry model id or version",
            _ => "discovered model id"
        },
        Limits: LocalAiProviderLimits.Default);
}

public sealed class LocalAiResponseTooLargeException : Exception
{
    public LocalAiResponseTooLargeException(int maximumBytes)
        : base($"The local AI response exceeded the {maximumBytes:N0}-byte limit.")
    {
        MaximumBytes = maximumBytes;
    }

    public int MaximumBytes { get; }
}

internal static class LocalAiModelCapabilityDetector
{
    public static bool IsLikelyVisionModel(string modelId)
    {
        var normalized = modelId.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("llava", StringComparison.Ordinal) ||
            normalized.Contains("vision", StringComparison.Ordinal) ||
            normalized.Contains("qwen2vl", StringComparison.Ordinal) ||
            normalized.Contains("qwen25vl", StringComparison.Ordinal) ||
            normalized.Contains("minicpmv", StringComparison.Ordinal) ||
            normalized.Contains("pixtral", StringComparison.Ordinal) ||
            normalized.Contains("moondream", StringComparison.Ordinal) ||
            normalized.Contains("internvl", StringComparison.Ordinal) ||
            normalized.Contains("idefics", StringComparison.Ordinal) ||
            normalized.Contains("gemma3", StringComparison.Ordinal);
    }
}

internal static class LocalAiResponseReader
{
    public static async Task<string> ReadBoundedStringAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
            throw new LocalAiResponseTooLargeException(maximumBytes);

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(Math.Min(maximumBytes, 64 * 1024));
        var chunk = new byte[16 * 1024];
        while (true)
        {
            int read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (buffer.Length + read > maximumBytes)
                throw new LocalAiResponseTooLargeException(maximumBytes);
            buffer.Write(chunk, 0, read);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static async Task<string> ReadBoundedTextAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var result = new StringBuilder(Math.Min(maximumCharacters, 8 * 1024));
        var chunk = new char[8 * 1024];
        while (true)
        {
            int read = await reader.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            if (result.Length + read > maximumCharacters)
                throw new LocalAiResponseTooLargeException(maximumCharacters);
            result.Append(chunk, 0, read);
        }

        return result.ToString();
    }
}
