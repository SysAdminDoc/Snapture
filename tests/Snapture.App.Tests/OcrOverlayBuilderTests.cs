using SkiaSharp;
using Snapture.App.Editor;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class OcrOverlayBuilderTests
{
    [TestMethod]
    public void PositionedLinesBecomeAnchoredEditableTextShapes()
    {
        using var background = new SKBitmap(160, 80);
        background.Erase(SKColors.White);
        var result = new OcrRecognitionResult(
            "Overlay",
            new[]
            {
                new OcrLineResult(
                    "Overlay",
                    Array.Empty<OcrWordResult>(),
                    new SKRect(20, 12, 100, 32))
            },
            OcrEngineKind.WindowsMediaOcr);

        var shapes = OcrOverlayBuilder.CreateShapes(result, background);

        Assert.HasCount(1, shapes);
        Assert.AreEqual(20, shapes[0].X, 0.001f);
        Assert.AreEqual(12, shapes[0].Y, 0.001f);
        Assert.AreEqual("Overlay", shapes[0].Text);
        Assert.AreEqual(0xFF11111Bu, shapes[0].StrokeColorArgb);
        Assert.IsTrue(shapes[0].DropShadow);
    }

    [TestMethod]
    public void TextOnlyLinesAndOutOfBoundsGeometryAreSkippedOrClamped()
    {
        using var background = new SKBitmap(100, 50);
        background.Erase(SKColors.Black);
        var result = new OcrRecognitionResult(
            "visible",
            new[]
            {
                new OcrLineResult("no box", Array.Empty<OcrWordResult>(), SKRect.Empty),
                new OcrLineResult("visible", Array.Empty<OcrWordResult>(), new SKRect(-20, -8, 120, 70))
            },
            OcrEngineKind.OneOcr);

        var shapes = OcrOverlayBuilder.CreateShapes(result, background);

        Assert.HasCount(1, shapes);
        Assert.AreEqual(0, shapes[0].X, 0.001f);
        Assert.AreEqual(0, shapes[0].Y, 0.001f);
        Assert.AreEqual(0xFFFFFFFFu, shapes[0].StrokeColorArgb);
    }
}
