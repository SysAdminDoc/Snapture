using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CodeAwareCaptureServiceTests
{
    [TestMethod]
    public void DetectsCodeFromSignalsAndMonospaceWordGeometry()
    {
        var lines = new[]
        {
            new OcrLineResult(
                "public static void Main() {",
                new[]
                {
                    Word("public", 0, 0, 48), Word("static", 56, 0, 48), Word("void", 112, 0, 32), Word("Main", 152, 0, 40)
                },
                new SKRect(0, 0, 220, 24)),
            new OcrLineResult(
                "    return value;",
                new[] { Word("return", 32, 28, 48), Word("value", 88, 28, 40) },
                new SKRect(0, 28, 180, 52)),
            new OcrLineResult(
                "}",
                new[] { Word("}", 0, 56, 8) },
                new SKRect(0, 56, 8, 80))
        };
        var result = new OcrRecognitionResult(string.Join('\n', lines.Select(line => line.Text)), lines, OcrEngineKind.RapidOcr);

        var analysis = CodeAwareCaptureService.Analyze(result);

        Assert.IsTrue(analysis.IsLikelyCode);
        Assert.IsTrue(analysis.MonospaceLike);
        Assert.AreEqual(3, analysis.LineCount);
        Assert.AreEqual(3, analysis.CodeLineCount);
        Assert.IsGreaterThanOrEqualTo(45, analysis.Score);
    }

    [TestMethod]
    public void RendersSyntaxHighlightedCodeCardWithBoundedLines()
    {
        using var card = CodeAwareCaptureService.RenderCodeCard(new[] { "const value = 42;", "// local only" });

        Assert.IsGreaterThan(360, card.Width);
        Assert.IsGreaterThan(100, card.Height);
        Assert.AreEqual(System.Drawing.Imaging.PixelFormat.Format32bppArgb, card.PixelFormat);
    }

    private static OcrWordResult Word(string text, float x, float y, float width)
        => new(text, new SKRect(x, y, x + width, y + 20), 0.99f, Array.Empty<SKPoint>());
}
