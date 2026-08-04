using System.Diagnostics;
using SkiaSharp;
using ZXing;
using ZXing.Common;

namespace Snapture.App.Services;

/// <summary>A barcode or QR payload decoded from image-space geometry.</summary>
public sealed record BarcodeDetection(
    string Text,
    string Format,
    SKRect BoundingBox,
    IReadOnlyList<SKPoint> Polygon);

/// <summary>Runs a local ZXing.Net multi-format pass over a Skia bitmap.</summary>
public static class BarcodeExtractor
{
    private static readonly BarcodeFormat[] SupportedFormats =
    {
        BarcodeFormat.QR_CODE,
        BarcodeFormat.DATA_MATRIX,
        BarcodeFormat.AZTEC,
        BarcodeFormat.PDF_417,
        BarcodeFormat.CODE_128,
        BarcodeFormat.CODE_39,
        BarcodeFormat.CODE_93,
        BarcodeFormat.EAN_8,
        BarcodeFormat.EAN_13,
        BarcodeFormat.UPC_A,
        BarcodeFormat.UPC_E,
        BarcodeFormat.ITF,
        BarcodeFormat.CODABAR,
        BarcodeFormat.RSS_14,
        BarcodeFormat.RSS_EXPANDED
    };

    public static IReadOnlyList<BarcodeDetection> Extract(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        if (bitmap.Width <= 0 || bitmap.Height <= 0) return Array.Empty<BarcodeDetection>();

        try
        {
            var reader = CreateReader();
            var source = CreateSource(bitmap);
            var results = reader.DecodeMultiple(source);
            if (results is null || results.Length == 0)
            {
                var single = reader.Decode(CreateSource(bitmap));
                results = single is null ? null : new[] { single };
            }
            if (results is null || results.Length == 0) return Array.Empty<BarcodeDetection>();

            return results
                .Where(result => !string.IsNullOrWhiteSpace(result?.Text))
                .Select(Normalize)
                .GroupBy(result => $"{result.Format}\u001F{result.Text}\u001F{result.BoundingBox.Left:0.##},{result.BoundingBox.Top:0.##}")
                .Select(group => group.First())
                .OrderBy(result => result.BoundingBox.IsEmpty ? float.MaxValue : result.BoundingBox.Top)
                .ThenBy(result => result.BoundingBox.IsEmpty ? float.MaxValue : result.BoundingBox.Left)
                .ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Barcode extraction unavailable: {ex.Message}");
            return Array.Empty<BarcodeDetection>();
        }
    }

    private static BarcodeReaderGeneric CreateReader()
    {
        return new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PossibleFormats = SupportedFormats.ToList()
            }
        };
    }

    private static RGBLuminanceSource CreateSource(SKBitmap bitmap)
    {
        var raw = new byte[bitmap.Width * bitmap.Height * 3];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var offset = (y * bitmap.Width + x) * 3;
                raw[offset] = pixel.Red;
                raw[offset + 1] = pixel.Green;
                raw[offset + 2] = pixel.Blue;
            }
        }
        return new RGBLuminanceSource(raw, bitmap.Width, bitmap.Height);
    }

    private static BarcodeDetection Normalize(Result result)
    {
        var polygon = result.ResultPoints is { Length: > 0 }
            ? result.ResultPoints.Select(point => new SKPoint(point.X, point.Y)).ToArray()
            : Array.Empty<SKPoint>();
        return new BarcodeDetection(result.Text.Trim(), result.BarcodeFormat.ToString(), Bounds(polygon), polygon);
    }

    private static SKRect Bounds(IReadOnlyList<SKPoint> points)
    {
        if (points.Count == 0) return SKRect.Empty;
        return new SKRect(
            points.Min(point => point.X),
            points.Min(point => point.Y),
            points.Max(point => point.X),
            points.Max(point => point.Y));
    }
}
