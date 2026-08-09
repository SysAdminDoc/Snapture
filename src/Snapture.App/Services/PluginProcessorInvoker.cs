using Snapture.Plugin;

namespace Snapture.App.Services;

/// <summary>
/// Shared processor invocation boundary for the normal capture flow and external callers such as
/// the CLI, URL handler, or localhost MCP server.
/// </summary>
public static class PluginProcessorInvoker
{
    /// <summary>Run a processor and retain the full capture for in-process host work.</summary>
    public static Task<PluginCapture> ProcessAsync(
        ICaptureProcessor processor,
        PluginCapture capture,
        IPluginHost host,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(host);
        return processor.ProcessAsync(capture, host, ct);
    }

    /// <summary>
    /// Run a processor and return metadata by default. External adapters should keep the
    /// default response mode unless a caller explicitly asks for pixel bytes.
    /// </summary>
    public static async Task<PluginCaptureResponse> InvokeAsync(
        ICaptureProcessor processor,
        PluginCapture capture,
        IPluginHost host,
        PluginCaptureResponseMode responseMode = PluginCaptureResponseMode.MetadataOnly,
        CancellationToken ct = default)
    {
        var processed = await ProcessAsync(processor, capture, host, ct).ConfigureAwait(false);
        return PluginCaptureResponse.FromCapture(processed, responseMode);
    }
}
