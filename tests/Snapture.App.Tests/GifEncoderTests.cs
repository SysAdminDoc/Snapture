using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using ImageMagick;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class GifEncoderTests
{
    [TestMethod]
    public void EncodeQuantizesFramesAndPreservesCentisecondTiming()
    {
        using var first = CreateFrame(Color.MidnightBlue, Color.CornflowerBlue);
        using var second = CreateFrame(Color.MidnightBlue, Color.Goldenrod);
        string outputPath = Path.Combine(Path.GetTempPath(), $"SnaptureMagick_{Guid.NewGuid():N}.gif");

        try
        {
            GifEncoder.Encode(
                outputPath,
                new[]
                {
                    new GifFrameInput(first, 100),
                    new GifFrameInput(second, 240)
                },
                new GifEncodingOptions(Colors: 64, FuzzPercent: 1.0));

            using var collection = new MagickImageCollection(outputPath);
            Assert.AreEqual(2, collection.Count);
            Assert.AreEqual((uint)10, collection[0].AnimationDelay);
            Assert.AreEqual((uint)24, collection[1].AnimationDelay);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    [TestMethod]
    public void EncodeRejectsInvalidLossySettingsBeforeReadingFrames()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GifEncoder.Encode(
                Path.Combine(Path.GetTempPath(), "unused.gif"),
                Array.Empty<GifFrameInput>(),
                new GifEncodingOptions(Colors: 1)));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GifEncoder.Encode(
                Path.Combine(Path.GetTempPath(), "unused.gif"),
                Array.Empty<GifFrameInput>(),
                new GifEncodingOptions(FuzzPercent: 101)));
    }

    private static Bitmap CreateFrame(Color background, Color accent)
    {
        var bitmap = new Bitmap(32, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.Clear(background);
        using var brush = new SolidBrush(accent);
        graphics.FillRectangle(brush, 8, 6, 16, 12);
        return bitmap;
    }
}
