using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CaptureHistoryVerificationTests
{
    [TestMethod]
    public void VerifiedRedactedStateIsPersistedAndCanBeCleared()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-CaptureHistoryVerificationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = CreateImage(root);
            using var history = new CaptureHistoryService(Path.Combine(root, "index.db"));
            history.Add(imagePath, "Region", "TestApp", "Raw", 64, 48, null);

            Assert.IsFalse(history.Recent(1).Single().VerifiedRedacted);
            Assert.AreEqual(1, history.SetVerifiedRedacted(imagePath, true));
            Assert.IsTrue(history.Recent(1).Single().VerifiedRedacted);
            Assert.AreEqual(1, history.SetVerifiedRedacted(imagePath, false));
            Assert.IsFalse(history.Recent(1).Single().VerifiedRedacted);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateImage(string root)
    {
        var path = Path.Combine(root, "capture.png");
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(new SKColor(32, 80, 160));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
