using Snapture.AdversarialPlugin;
using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginLoaderAdversarialTests
{
    [TestMethod]
    public void UnapprovedCapabilitiesPreventActivation()
    {
        string root = CreateRoot();
        try
        {
            using var loader = new PluginLoader(new TestHost(), _ => false, root);
            Assert.IsNull(loader.LoadOne(typeof(AdversarialProcessor).Assembly.Location));
            Assert.IsEmpty(loader.All);
        }
        finally { DeleteRoot(root); }
    }

    [TestMethod]
    public async Task ConstructorFailureIsolatedAndProcessorHonorsCancellation()
    {
        string root = CreateRoot();
        try
        {
            using var loader = new PluginLoader(new TestHost(), _ => true, root);
            var loaded = loader.LoadOne(typeof(AdversarialProcessor).Assembly.Location);
            Assert.IsNotNull(loaded);
            Assert.HasCount(1, loaded.CaptureProcessors);
            Assert.IsEmpty(loaded.Destinations);
            Assert.IsTrue(loaded.Context.IsCollectible);

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var capture = new PluginCapture(new byte[4], 1, 1, 4, "test", DateTime.UtcNow, null);
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                PluginProcessorInvoker.InvokeAsync(
                    loaded.CaptureProcessors[0],
                    capture,
                    loaded.Host,
                    ct: cancellation.Token));
        }
        finally { DeleteRoot(root); }
    }

    [TestMethod]
    public void MalformedDllIsRejectedWithoutAddingAPlugin()
    {
        string root = CreateRoot();
        string path = Path.Combine(root, "malformed.dll");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0xFF });
            using var loader = new PluginLoader(new TestHost(), _ => true, root);
            Assert.Throws<BadImageFormatException>(() => loader.LoadOne(path));
            Assert.IsEmpty(loader.All);
        }
        finally { DeleteRoot(root); }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.PluginAdversarial", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        try { Directory.Delete(root, recursive: true); } catch { }
    }

    private sealed class TestHost : IPluginHost
    {
        public string ScratchDirectory { get; } = Path.Combine(Path.GetTempPath(), "Snapture.PluginScratch", Guid.NewGuid().ToString("N"));
        public void ShowToast(string title, string message) { }
        public void Log(string message) { }
    }
}
