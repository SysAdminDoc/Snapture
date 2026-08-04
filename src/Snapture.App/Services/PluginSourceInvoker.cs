using Snapture.Plugin;

namespace Snapture.App.Services;

/// <summary>Safe invocation boundary for optional plugin capture sources.</summary>
public static class PluginSourceInvoker
{
    public static async Task<PluginCaptureResponse?> InvokeAsync(
        ICaptureSource source,
        IPluginHost host,
        PluginCaptureResponseMode responseMode = PluginCaptureResponseMode.MetadataOnly,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(host);
        var capture = await source.CaptureAsync(host, ct).ConfigureAwait(false);
        return capture is null ? null : PluginCaptureResponse.FromCapture(capture, responseMode);
    }
}
