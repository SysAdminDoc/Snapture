using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginCaptureResponseTests
{
    [TestMethod]
    public void MetadataOnlyResponseOmitsPixelPayloadAndIncludesIdentity()
    {
        var pixels = new byte[] { 0, 1, 2, 3, 4 };
        var capturedAt = new DateTime(2026, 8, 3, 12, 34, 56, DateTimeKind.Utc);
        var capture = new PluginCapture(pixels, 2, 1, 8, "Monitor 2", capturedAt, null);

        var response = PluginCaptureResponse.FromCapture(capture);

        Assert.IsFalse(response.IncludesPixels);
        Assert.IsNull(response.PixelsBgra);
        Assert.AreEqual(2, response.Metadata.Width);
        Assert.AreEqual(1, response.Metadata.Height);
        Assert.AreEqual(8, response.Metadata.Stride);
        Assert.AreEqual("08bb5e5d6eaac1049ede0893d30ed022b1a4d9b5b48db414871f51c9cb35283d", response.Metadata.Sha256);
        Assert.AreEqual("Monitor 2", response.Metadata.Source);
        Assert.AreEqual(capturedAt, response.Metadata.CapturedAtUtc);
    }

    [TestMethod]
    public void IncludePixelsRequiresAnExplicitResponseModeAndCopiesTheBuffer()
    {
        var pixels = new byte[] { 5, 6, 7, 8 };
        var capture = new PluginCapture(pixels, 1, 1, 4, "Region", DateTime.UtcNow, null);

        var response = PluginCaptureResponse.FromCapture(capture, PluginCaptureResponseMode.IncludePixels);

        Assert.IsTrue(response.IncludesPixels);
        Assert.IsNotNull(response.PixelsBgra);
        CollectionAssert.AreEqual(pixels, response.PixelsBgra!);
        Assert.AreNotSame(pixels, response.PixelsBgra);
    }

    [TestMethod]
    public async Task InvokerReturnsMetadataForProcessedOutputByDefault()
    {
        var processor = new TestProcessor();
        var capture = new PluginCapture(new byte[] { 1, 2, 3, 4 }, 1, 1, 4, "Window", DateTime.UtcNow, null);
        var host = new TestHost();

        var response = await PluginProcessorInvoker.InvokeAsync(processor, capture, host);

        Assert.AreEqual(1, processor.InvocationCount);
        Assert.AreEqual(2, response.Metadata.Width);
        Assert.AreEqual(1, response.Metadata.Height);
        Assert.AreEqual("Window (processed)", response.Metadata.Source);
        Assert.IsFalse(response.IncludesPixels);
        Assert.IsTrue(host.Logged);
    }

    private sealed class TestProcessor : ICaptureProcessor
    {
        public int InvocationCount { get; private set; }
        public string Id => "test-processor";
        public string DisplayName => "Test processor";
        public bool RunsByDefault => false;

        public Task<PluginCapture> ProcessAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default)
        {
            InvocationCount++;
            host.Log("processed");
            return Task.FromResult(new PluginCapture(
                new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 },
                2,
                1,
                8,
                capture.Source + " (processed)",
                capture.CapturedAtUtc,
                capture.FilePathOnDisk));
        }
    }

    private sealed class TestHost : IPluginHost
    {
        public bool Logged { get; private set; }
        public string ScratchDirectory => string.Empty;
        public void ShowToast(string title, string message) { }
        public void Log(string message) => Logged = true;
    }
}
