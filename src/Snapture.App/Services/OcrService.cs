using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using SkiaSharp;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapture.App.Services;

public enum OcrEngineKind
{
    WindowsAiTextRecognizer,
    WindowsMediaOcr
}

/// <summary>A normalized OCR word with rectangular and polygonal geometry.</summary>
public sealed record OcrWordResult(
    string Text,
    SKRect BoundingBox,
    float Confidence,
    IReadOnlyList<SKPoint> Polygon);

/// <summary>A normalized OCR line and its word-level geometry.</summary>
public sealed record OcrLineResult(
    string Text,
    IReadOnlyList<OcrWordResult> Words,
    SKRect BoundingBox);

/// <summary>OCR output shared by the Windows AI and legacy Windows OCR engines.</summary>
public sealed record OcrRecognitionResult(
    string Text,
    IReadOnlyList<OcrLineResult> Lines,
    OcrEngineKind Engine);

/// <summary>
/// Uses the Windows AI Foundry TextRecognizer when its model/runtime is ready, then falls back
/// to Windows.Media.Ocr. Both paths return the same word confidence and geometry contract.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class OcrService
{
    /// <summary>OCR a WPF <see cref="BitmapSource"/> with the preferred local engine.</summary>
    public static async Task<OcrRecognitionResult?> RecognizeAsync(BitmapSource source, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var softwareBitmap = await ConvertToSoftwareBitmapAsync(source);
        try
        {
            return await RecognizeSoftwareBitmapAsync(softwareBitmap, languageTag);
        }
        finally
        {
            softwareBitmap.Dispose();
        }
    }

    /// <summary>OCR an Skia bitmap without forcing callers to create a WPF image source.</summary>
    public static async Task<OcrRecognitionResult?> RecognizeAsync(SKBitmap source, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);

        var softwareBitmap = await DecodeSoftwareBitmapAsync(stream.ToArray());
        try
        {
            return await RecognizeSoftwareBitmapAsync(softwareBitmap, languageTag);
        }
        finally
        {
            softwareBitmap.Dispose();
        }
    }

    /// <summary>List languages installed on this system for the legacy fallback engine.</summary>
    public static IReadOnlyList<string> AvailableLanguages()
    {
        try
        {
            return OcrEngine.AvailableRecognizerLanguages
                .Select(l => l.LanguageTag)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Open Windows Settings on the language-pack install page.</summary>
    public static void OpenLanguageInstallSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:regionlanguage-adddisplaylanguage")
            { UseShellExecute = true });
        }
        catch { /* swallow */ }
    }

    private static async Task<OcrRecognitionResult?> RecognizeSoftwareBitmapAsync(
        SoftwareBitmap bitmap,
        string? languageTag)
    {
        var aiResult = await TryRecognizeWithWindowsAiAsync(bitmap);
        if (aiResult is not null) return aiResult;

        var legacyEngine = ResolveLegacyEngine(languageTag);
        if (legacyEngine is null) return null;
        var result = await legacyEngine.RecognizeAsync(bitmap);
        return NormalizeLegacyResult(result);
    }

    private static async Task<OcrRecognitionResult?> TryRecognizeWithWindowsAiAsync(SoftwareBitmap bitmap)
    {
        // TextRecognizer is a Windows 11 24H2+ AI surface. The catch-all is intentional: an
        // older OS, missing NPU model, absent Windows App SDK runtime, or failed model readiness
        // must transparently use the already-supported Windows.Media.Ocr path.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)) return null;

        try
        {
            if (TextRecognizer.GetReadyState() == AIFeatureReadyState.NotReady)
            {
                var ready = await TextRecognizer.EnsureReadyAsync();
                if (ready.Status != AIFeatureReadyResultState.Success) return null;
            }

            using var recognizer = await TextRecognizer.CreateAsync();
            using var imageBuffer = ImageBuffer.CreateForSoftwareBitmap(bitmap);
            var result = recognizer.RecognizeTextFromImage(imageBuffer);
            return NormalizeWindowsAiResult(result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows AI TextRecognizer unavailable; using legacy OCR: {ex.Message}");
            return null;
        }
    }

    private static OcrRecognitionResult NormalizeWindowsAiResult(RecognizedText result)
    {
        var lines = result.Lines
            .Select(line =>
            {
                var words = line.Words
                    .Select(word => NormalizeWindowsAiWord(word))
                    .ToList();
                return new OcrLineResult(line.Text, words, ToRect(line.BoundingBox));
            })
            .ToList();
        return new OcrRecognitionResult(string.Join(Environment.NewLine, lines.Select(line => line.Text)), lines, OcrEngineKind.WindowsAiTextRecognizer);
    }

    private static OcrWordResult NormalizeWindowsAiWord(RecognizedWord word)
    {
        var polygon = ToPolygon(word.BoundingBox);
        return new OcrWordResult(
            word.Text,
            Bounds(polygon),
            Math.Clamp(word.MatchConfidence, 0, 1),
            polygon);
    }

    private static OcrRecognitionResult NormalizeLegacyResult(Windows.Media.Ocr.OcrResult result)
    {
        var lines = result.Lines
            .Select(line =>
            {
                var words = line.Words
                    .Select(word =>
                    {
                        var rect = new SKRect(
                            (float)word.BoundingRect.Left,
                            (float)word.BoundingRect.Top,
                            (float)word.BoundingRect.Right,
                            (float)word.BoundingRect.Bottom);
                        return new OcrWordResult(word.Text, rect, 1f, RectanglePolygon(rect));
                    })
                    .ToList();
                return new OcrLineResult(line.Text, words, Bounds(words.SelectMany(word => word.Polygon).ToList()));
            })
            .ToList();
        return new OcrRecognitionResult(result.Text, lines, OcrEngineKind.WindowsMediaOcr);
    }

    private static OcrEngine? ResolveLegacyEngine(string? languageTag)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(languageTag) && OcrEngine.IsLanguageSupported(new Language(languageTag)))
                return OcrEngine.TryCreateFromLanguage(new Language(languageTag));
            return OcrEngine.TryCreateFromUserProfileLanguages();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<SoftwareBitmap> ConvertToSoftwareBitmapAsync(BitmapSource source)
    {
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        encoder.Save(stream);
        return await DecodeSoftwareBitmapAsync(stream.ToArray());
    }

    private static async Task<SoftwareBitmap> DecodeSoftwareBitmapAsync(byte[] encodedImage)
    {
        using var randomAccessStream = new InMemoryRandomAccessStream();
        using (var dataWriter = new DataWriter(randomAccessStream))
        {
            dataWriter.WriteBytes(encodedImage);
            await dataWriter.StoreAsync();
            await dataWriter.FlushAsync();
            dataWriter.DetachStream();
        }
        randomAccessStream.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(randomAccessStream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }

    private static IReadOnlyList<SKPoint> ToPolygon(RecognizedTextBoundingBox box) =>
        new[]
        {
            ToSkPoint(box.TopLeft), ToSkPoint(box.TopRight),
            ToSkPoint(box.BottomRight), ToSkPoint(box.BottomLeft)
        };

    private static SKPoint ToSkPoint(Point point) => new((float)point.X, (float)point.Y);

    private static IReadOnlyList<SKPoint> RectanglePolygon(SKRect rect) =>
        new[]
        {
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Top),
            new SKPoint(rect.Right, rect.Bottom), new SKPoint(rect.Left, rect.Bottom)
        };

    private static SKRect ToRect(RecognizedTextBoundingBox box) => Bounds(ToPolygon(box));

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
