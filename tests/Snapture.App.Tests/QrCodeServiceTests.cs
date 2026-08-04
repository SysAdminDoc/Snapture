using SkiaSharp;
using Snapture.App.Services;
using ZXing;

namespace Snapture.App.Tests;

[TestClass]
public sealed class QrCodeServiceTests
{
    [TestMethod]
    public void EncodePngProducesAReadableQrPayload()
    {
        const string url = "http://192.168.1.42:9087/s/test-token";

        var png = QrCodeService.EncodePng(url, 320);

        using var bitmap = SKBitmap.Decode(png);
        Assert.IsNotNull(bitmap);
        var detections = BarcodeExtractor.Extract(bitmap!);
        var qr = detections.Single(detection => detection.Format == nameof(BarcodeFormat.QR_CODE));
        Assert.AreEqual(url, qr.Text);
        Assert.AreEqual(320, bitmap!.Width);
        Assert.AreEqual(320, bitmap.Height);
    }

    [TestMethod]
    public void EncodePngRejectsMissingPayload()
    {
        Assert.Throws<ArgumentException>(() => QrCodeService.EncodePng(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => QrCodeService.EncodePng("https://example.test", 64));
    }
}
