namespace Snapture.App.Tests;

using SkiaSharp;
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
            File.WriteAllBytes(image, CreatePng());

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

    [TestMethod]
    public async Task DoesNotDeliverMalformedImageToTheImportCallback()
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
            File.WriteAllBytes(Path.Combine(root, "malformed.png"), new byte[] { 1, 2, 3, 4 });
            await Task.Delay(1_500);
            Assert.IsFalse(received.Task.IsCompleted);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(new SKColor(42, 48, 64));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
