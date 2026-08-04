using SkiaSharp;
using ZXing;

namespace Snapture.App.Services;

/// <summary>Creates local QR images for URLs already produced by the LAN-share server.</summary>
public static class QrCodeService
{
    public static byte[] EncodePng(string payload, int pixels = 360)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("A QR payload is required.", nameof(payload));
        if (pixels is < 128 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(pixels), "QR size must be between 128 and 1024 pixels.");

        var matrix = new MultiFormatWriter().encode(
            payload,
            BarcodeFormat.QR_CODE,
            pixels,
            pixels);
        using var bitmap = new SKBitmap(
            matrix.Width,
            matrix.Height,
            SKColorType.Bgra8888,
            SKAlphaType.Opaque);
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
                bitmap.SetPixel(x, y, matrix[x, y] ? SKColors.Black : SKColors.White);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
