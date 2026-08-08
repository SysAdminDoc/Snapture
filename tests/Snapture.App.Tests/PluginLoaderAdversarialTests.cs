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
            using var loader = new PluginLoader(new TestHost(), _ => false, root, _ => true);
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
            using var loader = new PluginLoader(new TestHost(), _ => true, root, _ => true);
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
            using var loader = new PluginLoader(new TestHost(), _ => true, root, _ => true);
            Assert.Throws<BadImageFormatException>(() => loader.LoadOne(path));
            Assert.IsEmpty(loader.All);
        }
        finally { DeleteRoot(root); }
    }

    [TestMethod]
    public void ArtifactTrustIsEnforcedBeforeActivation()
    {
        string root = CreateRoot();
        try
        {
            using var loader = new PluginLoader(new TestHost(), _ => true, root, _ => false);
            Assert.IsNull(loader.LoadOne(typeof(AdversarialProcessor).Assembly.Location));
            Assert.IsEmpty(loader.All);
        }
        finally { DeleteRoot(root); }
    }

    [TestMethod]
    public void FailedUpdateRestoresPreviousArtifactAndCleansStaging()
    {
        string root = CreateRoot();
        string sourceRoot = CreateRoot();
        string source = Path.Combine(sourceRoot, "candidate.dll");
        string replacement = Path.Combine(sourceRoot, "replacement.dll");
        try
        {
            string fixture = typeof(AdversarialProcessor).Assembly.Location;
            File.Copy(fixture, source);
            File.Copy(fixture, replacement);
            using var loader = new PluginLoader(new TestHost(), _ => true, root, _ => true);
            var installed = loader.InstallOrUpdate(source);
            byte[] previous = File.ReadAllBytes(installed.Info.AssemblyPath);

            Assert.ThrowsExactly<InvalidDataException>(() => loader.InstallOrUpdateWithLoader(replacement, _ => null));

            Assert.HasCount(1, loader.All);
            CollectionAssert.AreEqual(previous, File.ReadAllBytes(installed.Info.AssemblyPath));
            Assert.IsFalse(Directory.EnumerateFiles(root, "*.backup-*.tmp").Any());
            Assert.IsFalse(Directory.EnumerateFiles(root, "*.install-*.tmp").Any());
        }
        finally
        {
            DeleteRoot(root);
            DeleteRoot(sourceRoot);
        }
    }

    [TestMethod]
    public void UninstallRemovesTheArtifactAndUnloadEntry()
    {
        string root = CreateRoot();
        string sourceRoot = CreateRoot();
        string source = Path.Combine(sourceRoot, "candidate.dll");
        try
        {
            File.Copy(typeof(AdversarialProcessor).Assembly.Location, source);
            using var loader = new PluginLoader(new TestHost(), _ => true, root, _ => true);
            var installed = loader.InstallOrUpdate(source);
            string installedPath = installed.Info.AssemblyPath;

            Assert.IsTrue(loader.Uninstall(installed));
            Assert.IsFalse(File.Exists(installedPath));
            Assert.IsEmpty(loader.All);
            Assert.IsFalse(loader.Uninstall(installed));
        }
        finally
        {
            DeleteRoot(root);
            DeleteRoot(sourceRoot);
        }
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
