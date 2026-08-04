using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class SpeechBalloonTests
{
    [TestMethod]
    public void BoundsIncludeTheTailAndHitTestingCoversIt()
    {
        var balloon = new SpeechBalloonShape
        {
            X = 10,
            Y = 12,
            Width = 120,
            Height = 64,
            CornerRadius = 48,
            TailLength = 24
        };

        var bounds = balloon.GetBounds();

        Assert.AreEqual(100f, bounds.Bottom, 0.001f);
        Assert.IsTrue(balloon.HitTest(new SKPoint(52, 94)));
        Assert.IsFalse(balloon.HitTest(new SKPoint(150, 110)));
    }

    [TestMethod]
    public void ClonePreservesCornerRadiusAndTailLength()
    {
        var original = new SpeechBalloonShape { CornerRadius = 28, TailLength = 26 };

        var clone = (SpeechBalloonShape)original.Clone();

        Assert.AreEqual(original.CornerRadius, clone.CornerRadius, 0.001f);
        Assert.AreEqual(original.TailLength, clone.TailLength, 0.001f);
    }

    [TestMethod]
    public void ProjectSerializationRoundTripsSpeechBalloon()
    {
        using var background = new SKBitmap(24, 24);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(new SpeechBalloonShape { Width = 80, Height = 40, CornerRadius = 22 });

        using var restoredBackground = new SKBitmap(24, 24);
        var restored = new AnnotationDocument(restoredBackground);
        restored.DeserializeShapes(document.SerializeShapes());

        var balloon = (SpeechBalloonShape)restored.Shapes.Single();
        Assert.AreEqual(22f, balloon.CornerRadius, 0.001f);
        Assert.AreEqual(20f, balloon.TailLength, 0.001f);
    }

    [TestMethod]
    public void RenderSupportsFullyRoundedSmallBodies()
    {
        using var bitmap = new SKBitmap(160, 120);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var balloon = new SpeechBalloonShape
        {
            X = 12,
            Y = 12,
            Width = 36,
            Height = 24,
            CornerRadius = 100,
            StrokeColorArgb = 0xFFFF0000
        };

        balloon.Render(canvas, new AnnotationDocument(bitmap));

        Assert.AreNotEqual(SKColors.Transparent, bitmap.GetPixel(20, 20));
    }
}
