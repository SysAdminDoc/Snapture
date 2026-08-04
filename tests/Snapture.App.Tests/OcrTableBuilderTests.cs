using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class OcrTableBuilderTests
{
    [TestMethod]
    public void AlignedWordBoxesBecomeRowsAndColumnsAndMergeCellWords()
    {
        var result = new OcrRecognitionResult(
            "Name Role Score\nAda Lovelace Engineer 98",
            new[]
            {
                new OcrLineResult(
                    "Name Role Score",
                    new[]
                    {
                        Word("Name", 10, 10, 48, 24),
                        Word("Role", 120, 10, 160, 24),
                        Word("Score", 240, 10, 286, 24)
                    },
                    new SKRect(10, 10, 286, 24)),
                new OcrLineResult(
                    "Ada Lovelace Engineer 98",
                    new[]
                    {
                        Word("Ada", 10, 40, 36, 54),
                        Word("Lovelace", 40, 40, 92, 54),
                        Word("Engineer", 120, 40, 184, 54),
                        Word("98", 240, 40, 258, 54)
                    },
                    new SKRect(10, 40, 258, 54))
            },
            OcrEngineKind.WindowsMediaOcr);

        var table = OcrTableBuilder.Build(result);

        Assert.AreEqual(3, table.ColumnCount);
        Assert.HasCount(2, table.Rows);
        Assert.AreEqual("Name", table.Rows[0].Cells[0].Text);
        Assert.AreEqual("Ada Lovelace", table.Rows[1].Cells[0].Text);
        Assert.AreEqual("Engineer", table.Rows[1].Cells[1].Text);
        Assert.AreEqual("Name\tRole\tScore\r\nAda Lovelace\tEngineer\t98", OcrTableBuilder.ToTsv(table));
    }

    [TestMethod]
    public void TextOnlyOcrDoesNotInventTableGeometry()
    {
        var result = new OcrRecognitionResult(
            "one\ntwo",
            new[]
            {
                new OcrLineResult("one", Array.Empty<OcrWordResult>(), SKRect.Empty),
                new OcrLineResult("two", Array.Empty<OcrWordResult>(), SKRect.Empty)
            },
            OcrEngineKind.OneOcr);

        var table = OcrTableBuilder.Build(result);

        Assert.IsTrue(table.IsEmpty);
        Assert.AreEqual(string.Empty, OcrTableBuilder.ToTsv(table));
    }

    private static OcrWordResult Word(string text, float left, float top, float right, float bottom) =>
        new(text, new SKRect(left, top, right, bottom), 1f, new[]
        {
            new SKPoint(left, top),
            new SKPoint(right, top),
            new SKPoint(right, bottom),
            new SKPoint(left, bottom)
        });
}

