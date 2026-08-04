using Snapture.Plugin;

namespace Snapture.AdversarialPlugin;

[SnapturePlugin(
    "Adversarial fixture",
    "Snapture tests",
    "1.0.0",
    "Exercises loader capability and invocation boundaries.",
    PluginCapability.Network)]
public sealed class AdversarialProcessor : ICaptureProcessor
{
    public string Id => "adversarial-hang";
    public string DisplayName => "Adversarial hanging processor";
    public bool RunsByDefault => false;

    public async Task<PluginCapture> ProcessAsync(
        PluginCapture capture,
        IPluginHost host,
        CancellationToken ct = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        return capture;
    }
}

public sealed class ThrowingDestination : IDestination
{
    public ThrowingDestination() => throw new InvalidOperationException("constructor fixture");

    public string Id => "adversarial-throw";
    public string DisplayName => "Adversarial throwing destination";

    public Task SendAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default) =>
        throw new InvalidOperationException("destination fixture");
}
