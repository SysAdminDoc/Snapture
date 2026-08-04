using System.Drawing;
using System.Drawing.Imaging;
using ImageMagick;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class BeforeAfterGifServiceTests
{
    [TestMethod]
    public void CreatesPingPongGifWithExpectedFramesAndCanvas()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.BeforeAfterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string before = CreateImage(root, "before.png", 20, 12, Color.MediumPurple);
            string after = CreateImage(root, "after.png", 14, 18, Color.CadetBlue);
            string output = Path.Combine(root, "comparison.gif");

            var result = BeforeAfterGifService.CreateGif(
                before,
                after,
                output,
                new BeforeAfterGifOptions(TransitionFrames: 4, DelayMs: 80));

            Assert.AreEqual(6, result.FrameCount);
            Assert.AreEqual(20, result.Width);
            Assert.AreEqual(18, result.Height);
            Assert.IsTrue(File.Exists(result.OutputPath));
            using var frames = new MagickImageCollection(result.OutputPath);
            Assert.AreEqual(result.FrameCount, frames.Count);
            Assert.AreEqual((uint)8, frames[0].AnimationDelay);
            Assert.AreEqual((uint)8, frames[^1].AnimationDelay);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RejectsInvalidOptionsAndNonGifOutput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeforeAfterGifOptions(TransitionFrames: 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BeforeAfterGifOptions(DelayMs: 10).Validate());
        Assert.Throws<ArgumentException>(() => BeforeAfterGifService.CreateGif(
            "before.png", "after.png", "comparison.png", new BeforeAfterGifOptions()));
    }

    private static string CreateImage(string root, string name, int width, int height, Color color)
    {
        string path = Path.Combine(root, name);
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
