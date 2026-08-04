using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginSourceInvokerTests
{
    [TestMethod]
    public async Task SourceInvocationReturnsMetadataByDefaultAndPixelsOnlyWhenRequested()
    {
        var source = new TestSource();
        var host = new TestHost();

        var metadata = await PluginSourceInvoker.InvokeAsync(source, host);
        Assert.IsNotNull(metadata);
        Assert.IsFalse(metadata!.IncludesPixels);
        Assert.AreEqual(2, metadata.Metadata.Width);

        var full = await PluginSourceInvoker.InvokeAsync(
            source,
            host,
            PluginCaptureResponseMode.IncludePixels);
        Assert.IsNotNull(full);
        Assert.IsTrue(full!.IncludesPixels);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, full.PixelsBgra!);
    }

    private sealed class TestSource : ICaptureSource
    {
        public string Id => "test-source";
        public string DisplayName => "Test source";
        public string Description => "Synthetic source";

        public Task<PluginCapture?> CaptureAsync(IPluginHost host, CancellationToken ct = default) =>
            Task.FromResult<PluginCapture?>(new PluginCapture(
                new byte[] { 1, 2, 3, 4 },
                2,
                1,
                8,
                "TestSource",
                DateTime.UtcNow,
                null));
    }

    private sealed class TestHost : IPluginHost
    {
        public string ScratchDirectory => Path.GetTempPath();
        public void ShowToast(string title, string message) { }
        public void Log(string message) { }
    }
}
