using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
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

    [TestMethod]
    public void EncodeWritesAnimatedPngWithCentisecondTiming()
        => AssertAnimatedFormat(AnimatedImageFormat.Apng, ".apng");

    [TestMethod]
    public void EncodeWritesAnimatedAvifWithCentisecondTiming()
        => AssertAnimatedFormat(AnimatedImageFormat.Avif, ".avif");

    private static void AssertAnimatedFormat(AnimatedImageFormat format, string extension)
    {
        Assert.IsTrue(GifEncoder.IsFormatSupported(format));
        using var first = CreateFrame(Color.MidnightBlue, Color.CornflowerBlue);
        using var second = CreateFrame(Color.MidnightBlue, Color.Goldenrod);
        string outputPath = Path.Combine(Path.GetTempPath(), $"SnaptureMagick_{Guid.NewGuid():N}{extension}");

        try
        {
            GifEncoder.Encode(
                outputPath,
                new[]
                {
                    new GifFrameInput(first, 100),
                    new GifFrameInput(second, 240)
                },
                GifEncodingOptions.Default,
                format);

            Assert.IsTrue(File.Exists(outputPath));
            Assert.IsGreaterThan(0L, new FileInfo(outputPath).Length);
            if (format == AnimatedImageFormat.Apng)
            {
                var delays = ReadApngDelays(outputPath);
                Assert.HasCount(2, delays);
                Assert.AreEqual((ushort)10, delays[0].Numerator);
                Assert.AreEqual((ushort)100, delays[0].Denominator);
                Assert.AreEqual((ushort)24, delays[1].Numerator);
                Assert.AreEqual((ushort)100, delays[1].Denominator);
            }
            else
            {
                using var collection = new MagickImageCollection(outputPath);
                Assert.AreEqual(2, collection.Count, $"{format} decoded delays: {string.Join(",", collection.Select(image => image.AnimationDelay))}");
                Assert.AreEqual((uint)10, collection[0].AnimationDelay, $"{format} decoded delays: {string.Join(",", collection.Select(image => image.AnimationDelay))}");
                Assert.AreEqual((uint)24, collection[1].AnimationDelay, $"{format} decoded delays: {string.Join(",", collection.Select(image => image.AnimationDelay))}");
            }
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static List<(ushort Numerator, ushort Denominator)> ReadApngDelays(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Assert.IsTrue(bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        var delays = new List<(ushort Numerator, ushort Denominator)>();
        int offset = 8;
        while (offset + 12 <= bytes.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (type == "fcTL")
            {
                int dataOffset = offset + 8;
                delays.Add((
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(dataOffset + 20, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(dataOffset + 22, 2))));
            }

            offset += 12 + length;
            if (type == "IEND")
                break;
        }

        return delays;
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
