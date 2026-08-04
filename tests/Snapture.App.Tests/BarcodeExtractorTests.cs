using SkiaSharp;
using Snapture.App.Services;
using ZXing;
using ZXing.Common;

namespace Snapture.App.Tests;

[TestClass]
public sealed class BarcodeExtractorTests
{
    [TestMethod]
    public void ExtractDecodesGeneratedQrPayloadAndReturnsImageGeometry()
    {
        var matrix = new MultiFormatWriter().encode(
            "https://snapture.test/qr",
            BarcodeFormat.QR_CODE,
            240,
            240);
        using var bitmap = CreateBitmap(matrix);

        var detections = BarcodeExtractor.Extract(bitmap);

        var qr = detections.Single(detection => detection.Format == nameof(BarcodeFormat.QR_CODE));
        Assert.AreEqual("https://snapture.test/qr", qr.Text);
        Assert.IsFalse(qr.BoundingBox.IsEmpty);
        Assert.IsGreaterThan(0, qr.Polygon.Count);
    }

    [TestMethod]
    public void ExtractDecodesGeneratedCode128Payload()
    {
        var matrix = new MultiFormatWriter().encode(
            "SNAPTURE-123",
            BarcodeFormat.CODE_128,
            420,
            120);
        using var bitmap = CreateBitmap(matrix);

        var code = BarcodeExtractor.Extract(bitmap).Single(detection =>
            detection.Format == nameof(BarcodeFormat.CODE_128));

        Assert.AreEqual("SNAPTURE-123", code.Text);
        Assert.IsFalse(code.BoundingBox.IsEmpty);
    }

    [TestMethod]
    public void ExtractReturnsEmptyForPlainImage()
    {
        using var bitmap = new SKBitmap(120, 80);
        bitmap.Erase(SKColors.White);

        Assert.IsEmpty(BarcodeExtractor.Extract(bitmap));
    }

    private static SKBitmap CreateBitmap(BitMatrix matrix)
    {
        var bitmap = new SKBitmap(matrix.Width, matrix.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        }
        return bitmap;
    }
}
