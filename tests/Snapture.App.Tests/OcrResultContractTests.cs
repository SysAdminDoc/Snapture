using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class OcrResultContractTests
{
    [TestMethod]
    public void WordResultPreservesConfidenceAndQuadrilateralGeometry()
    {
        var polygon = new[]
        {
            new SKPoint(10, 12), new SKPoint(82, 12),
            new SKPoint(78, 38), new SKPoint(8, 38)
        };
        var word = new OcrWordResult("secret", new SKRect(8, 12, 82, 38), 0.87f, polygon);
        var result = new OcrRecognitionResult(
            "secret",
            new[] { new OcrLineResult("secret", new[] { word }, word.BoundingBox) },
            OcrEngineKind.WindowsAiTextRecognizer);

        Assert.AreEqual(OcrEngineKind.WindowsAiTextRecognizer, result.Engine);
        Assert.AreEqual(0.87f, result.Lines.Single().Words.Single().Confidence, 0.001f);
        Assert.HasCount(4, result.Lines.Single().Words.Single().Polygon);
        Assert.AreEqual(82, result.Lines.Single().Words.Single().Polygon[1].X);
        Assert.AreEqual(38, result.Lines.Single().Words.Single().BoundingBox.Bottom);
    }

    [TestMethod]
    public void LegacyEngineHasTheSameNormalizedShapeContract()
    {
        var word = new OcrWordResult(
            "fallback",
            new SKRect(4, 5, 60, 24),
            1f,
            new[]
            {
                new SKPoint(4, 5), new SKPoint(60, 5),
                new SKPoint(60, 24), new SKPoint(4, 24)
            });
        var result = new OcrRecognitionResult(
            "fallback",
            new[] { new OcrLineResult("fallback", new[] { word }, word.BoundingBox) },
            OcrEngineKind.WindowsMediaOcr);

        Assert.AreEqual(OcrEngineKind.WindowsMediaOcr, result.Engine);
        Assert.AreEqual("fallback", result.Lines.Single().Text);
        Assert.AreEqual(1f, result.Lines.Single().Words.Single().Confidence, 0.001f);
        Assert.AreEqual(60, result.Lines.Single().BoundingBox.Right);
    }

    [TestMethod]
    public void RapidEngineUsesTheSharedNormalizedContract()
    {
        var word = new OcrWordResult(
            "rapid",
            new SKRect(8, 10, 72, 34),
            0.94f,
            new[]
            {
                new SKPoint(8, 10), new SKPoint(72, 10),
                new SKPoint(72, 34), new SKPoint(8, 34)
            });
        var result = new OcrRecognitionResult(
            "rapid",
            new[] { new OcrLineResult("rapid", new[] { word }, word.BoundingBox) },
            OcrEngineKind.RapidOcr);

        Assert.AreEqual(OcrEngineKind.RapidOcr, result.Engine);
        Assert.AreEqual(0.94f, result.Lines.Single().Words.Single().Confidence, 0.001f);
        Assert.HasCount(4, result.Lines.Single().Words.Single().Polygon);
    }

    [TestMethod]
    public void OneOcrPlainTextIsNormalizedWithoutInventingGeometry()
    {
        var result = OcrService.NormalizeOneOcrText("\uFEFF first line\r\nsecond line\r\n");

        Assert.IsNotNull(result);
        Assert.AreEqual(OcrEngineKind.OneOcr, result.Engine);
        Assert.AreEqual($"first line{Environment.NewLine}second line", result.Text);
        Assert.HasCount(2, result.Lines);
        Assert.IsEmpty(result.Lines[0].Words);
        Assert.IsTrue(result.Lines[0].BoundingBox.IsEmpty);
    }

    [TestMethod]
    public void OneOcrEmptyOutputReturnsNoResult()
    {
        Assert.IsNull(OcrService.NormalizeOneOcrText("\r\n \t"));
    }
}
