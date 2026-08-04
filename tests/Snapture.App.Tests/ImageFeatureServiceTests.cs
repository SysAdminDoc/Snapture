using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ImageFeatureServiceTests
{
    [TestMethod]
    public void ComputeFindsDominantColorAndStablePerceptualHash()
    {
        var root = CreateRoot();
        try
        {
            var path = CreateImage(root, "red.png", static (canvas, _) => canvas.Clear(new SKColor(240, 24, 32)));

            var features = ImageFeatureService.Compute(path);

            Assert.IsNotNull(features);
            Assert.IsLessThanOrEqualTo(12, ImageFeatureService.ColorDistance(features.DominantColorHex, "#F01820"));
            Assert.AreEqual(16, features.PerceptualHash.Length);
            Assert.IsTrue(features.PerceptualHash.All(Uri.IsHexDigit));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void NearDuplicateHashesStayWithinThreshold()
    {
        var root = CreateRoot();
        try
        {
            var first = CreateImage(root, "first.png", static (canvas, _) => canvas.Clear(new SKColor(32, 80, 160)));
            var second = CreateImage(root, "second.png", static (canvas, _) => canvas.Clear(new SKColor(38, 86, 166)));
            var firstFeatures = ImageFeatureService.Compute(first);
            var secondFeatures = ImageFeatureService.Compute(second);

            Assert.IsNotNull(firstFeatures);
            Assert.IsNotNull(secondFeatures);
            Assert.IsTrue(ImageFeatureService.IsNearDuplicate(
                firstFeatures.PerceptualHash,
                secondFeatures.PerceptualHash));
            Assert.AreEqual(1, ImageFeatureService.HammingDistance("0000000000000000", "0000000000000001"));
            Assert.AreEqual(64, ImageFeatureService.HammingDistance("0000000000000000", "FFFFFFFFFFFFFFFF"));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [TestMethod]
    public void ColorParsingNormalizesAndRejectsInvalidValues()
    {
        Assert.IsTrue(ImageFeatureService.TryParseHex(" #cba6f7 ", out var color));
        Assert.AreEqual("#CBA6F7", ImageFeatureService.ToHex(color));
        Assert.AreEqual(0, ImageFeatureService.ColorDistance("#123456", "#123456"));
        Assert.IsFalse(ImageFeatureService.TryParseHex("purple", out _));
        Assert.AreEqual(int.MaxValue, ImageFeatureService.ColorDistance("#123456", "purple"));
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-ImageFeatureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateImage(string root, string name, Action<SKCanvas, SKBitmap> draw)
    {
        var path = Path.Combine(root, name);
        using var bitmap = new SKBitmap(96, 64);
        using (var canvas = new SKCanvas(bitmap))
            draw(canvas, bitmap);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
