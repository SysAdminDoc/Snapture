namespace Snapture.App.Tests;

using Snapture.App.Services;

[TestClass]
public sealed class WatchFolderServiceTests
{
    [TestMethod]
    public async Task ImportsOnlySupportedFilesAfterTheyBecomeStable()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.WatchFolder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new WatchFolderService(path =>
        {
            received.TrySetResult(path);
            return Task.CompletedTask;
        });

        try
        {
            watcher.Start(root);
            File.WriteAllText(Path.Combine(root, "ignore.txt"), "not an image");
            string image = Path.Combine(root, "capture.png");
            File.WriteAllBytes(image, new byte[] { 1, 2, 3 });

            Task completed = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            Assert.AreSame(received.Task, completed);
            Assert.AreEqual(Path.GetFullPath(image), await received.Task);
            Assert.IsTrue(watcher.IsRunning);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void StartRejectsMissingFolderAndDisposeStopsWatcher()
    {
        using var watcher = new WatchFolderService(_ => Task.CompletedTask);
        Assert.Throws<DirectoryNotFoundException>(() => watcher.Start(
            Path.Combine(Path.GetTempPath(), "Snapture.WatchFolder", Guid.NewGuid().ToString("N"))));
        Assert.IsFalse(watcher.IsRunning);
    }
}
