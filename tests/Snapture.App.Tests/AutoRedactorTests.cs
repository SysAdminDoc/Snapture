using SkiaSharp;
using Snapture.App.Editor;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class AutoRedactorTests
{
    [TestMethod]
    public void RedactionFindingsUseTheExistingOcrWordGeometry()
    {
        var secret = "ghp_" + new string('a', 36);
        var polygon = new[]
        {
            new SKPoint(10, 12), new SKPoint(82, 12),
            new SKPoint(82, 38), new SKPoint(10, 38)
        };
        var word = new OcrWordResult(secret, new SKRect(10, 12, 82, 38), 0.96f, polygon);
        var result = new OcrRecognitionResult(
            secret,
            new[] { new OcrLineResult(secret, new[] { word }, word.BoundingBox) },
            OcrEngineKind.RapidOcr);

        var findings = AutoRedactor.FindFindings(result);

        Assert.HasCount(1, findings);
        Assert.AreEqual("gh-pat", findings[0].RuleId);
        Assert.AreEqual(secret, findings[0].MatchedText);
        Assert.AreEqual(new SKRect(8, 10, 84, 40), findings[0].Box);
    }

    [TestMethod]
    public void DisabledRulesAreAppliedWithoutAnotherOcrPass()
    {
        var secret = "ghp_" + new string('b', 36);
        var word = new OcrWordResult(
            secret,
            new SKRect(4, 5, 60, 24),
            1f,
            new[]
            {
                new SKPoint(4, 5), new SKPoint(60, 5),
                new SKPoint(60, 24), new SKPoint(4, 24)
            });
        var result = new OcrRecognitionResult(
            secret,
            new[] { new OcrLineResult(secret, new[] { word }, word.BoundingBox) },
            OcrEngineKind.RapidOcr);

        var findings = AutoRedactor.FindFindings(
            result,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "gh-pat" });

        Assert.IsEmpty(findings);
    }
}
