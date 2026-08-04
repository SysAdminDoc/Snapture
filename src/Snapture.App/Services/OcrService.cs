using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Microsoft.Graphics.Imaging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using RapidOcrNet;
using SkiaSharp;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapture.App.Services;

using WindowsAiTextRecognizer = Microsoft.Windows.AI.Imaging.TextRecognizer;

public enum OcrEngineKind
{
    WindowsAiTextRecognizer,
    WindowsMediaOcr,
    RapidOcr
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
/// to Windows.Media.Ocr and RapidOCR. Every path returns the same word confidence and geometry contract.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class OcrService
{
    private static readonly SemaphoreSlim RapidGate = new(1, 1);
    private static RapidOcr? _rapidOcr;
    private static bool _rapidUseDirectMl;
    private static string _rapidProviderStatus = "CPU (default)";

    /// <summary>Whether the experimental DirectML provider is requested for RapidOCR.</summary>
    public static bool RapidOcrUseDirectMl => _rapidUseDirectMl;

    /// <summary>Current RapidOCR provider state for Settings and diagnostics.</summary>
    public static string RapidOcrProviderStatus => _rapidProviderStatus;

    /// <summary>
    /// Apply the RapidOCR provider preference and discard an already initialized model when it changes.
    /// DirectML is optional; model initialization falls back to CPU if the provider DLL is not installed
    /// or the selected device cannot load the models.
    /// </summary>
    public static void ConfigureRapidOcr(bool useDirectMl)
    {
        RapidGate.Wait();
        try
        {
            if (_rapidUseDirectMl != useDirectMl && _rapidOcr is not null)
            {
                _rapidOcr.Dispose();
                _rapidOcr = null;
            }

            _rapidUseDirectMl = useDirectMl;
            _rapidProviderStatus = useDirectMl
                ? "DirectML requested (CPU fallback if unavailable)"
                : "CPU (default)";
        }
        finally
        {
            RapidGate.Release();
        }
    }

    /// <summary>OCR a WPF <see cref="BitmapSource"/> with the preferred local engine.</summary>
    public static async Task<OcrRecognitionResult?> RecognizeAsync(BitmapSource source, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        encoder.Save(stream);
        return await RecognizeEncodedImageAsync(stream.ToArray(), languageTag);
    }

    /// <summary>OCR an Skia bitmap without forcing callers to create a WPF image source.</summary>
    public static async Task<OcrRecognitionResult?> RecognizeAsync(SKBitmap source, string? languageTag = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var image = SKImage.FromBitmap(source);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);

        return await RecognizeEncodedImageAsync(stream.ToArray(), languageTag);
    }

    /// <summary>
    /// Run the cross-platform RapidOCR path directly. This is used by diagnostics and tests to verify
    /// the fallback independently of whichever Windows OCR engine is installed on the host.
    /// </summary>
    internal static Task<OcrRecognitionResult?> RecognizeWithRapidOcrAsync(
        SKBitmap source,
        bool useDirectMl = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        ConfigureRapidOcr(useDirectMl);
        return TryRecognizeWithRapidOcrAsync(source);
    }

    private static async Task<OcrRecognitionResult?> RecognizeEncodedImageAsync(
        byte[] encodedImage,
        string? languageTag)
    {
        using var softwareBitmap = await DecodeSoftwareBitmapAsync(encodedImage);
        using var skBitmap = SKBitmap.Decode(encodedImage);
        return await RecognizeSoftwareBitmapAsync(softwareBitmap, languageTag, skBitmap);
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
        string? languageTag,
        SKBitmap? skBitmap)
    {
        var aiResult = await TryRecognizeWithWindowsAiAsync(bitmap);
        if (aiResult is not null) return aiResult;

        try
        {
            var legacyEngine = ResolveLegacyEngine(languageTag);
            if (legacyEngine is not null)
            {
                var result = await legacyEngine.RecognizeAsync(bitmap);
                if (!string.IsNullOrWhiteSpace(result.Text))
                    return NormalizeLegacyResult(result);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Windows.Media.Ocr unavailable; using RapidOCR: {ex.Message}");
        }

        return skBitmap is null ? null : await TryRecognizeWithRapidOcrAsync(skBitmap);
    }

    private static async Task<OcrRecognitionResult?> TryRecognizeWithRapidOcrAsync(SKBitmap bitmap)
    {
        await RapidGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                _rapidOcr ??= CreateRapidOcr();
                var result = _rapidOcr.Detect(bitmap, RapidOcrOptions.Default with
                {
                    ReturnWordBox = true
                });
                return NormalizeRapidResult(result);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _rapidProviderStatus = "Unavailable";
            Debug.WriteLine($"RapidOCR unavailable: {ex.Message}");
            return null;
        }
        finally
        {
            RapidGate.Release();
        }
    }

    private static RapidOcr CreateRapidOcr()
    {
        if (_rapidUseDirectMl)
        {
            try
            {
                var directMl = CreateRapidOcr(useDirectMl: true);
                _rapidProviderStatus = "DirectML (device 0)";
                return directMl;
            }
            catch (Exception ex)
            {
                _rapidProviderStatus = "CPU fallback (DirectML unavailable)";
                Debug.WriteLine($"RapidOCR DirectML unavailable; using CPU: {ex.Message}");
            }
        }

        var cpu = CreateRapidOcr(useDirectMl: false);
        _rapidProviderStatus = "CPU";
        return cpu;
    }

    private static RapidOcr CreateRapidOcr(bool useDirectMl)
    {
        using var sessionOptions = RapidOcr.GetDefaultSessionOptions();
        if (useDirectMl)
            sessionOptions.AppendExecutionProvider_DML(0);

        var ocr = new RapidOcr();
        try
        {
            ocr.InitModels(sessionOptions);
            return ocr;
        }
        catch
        {
            ocr.Dispose();
            throw;
        }
    }

    private static OcrRecognitionResult NormalizeRapidResult(RapidOcrNet.OcrResult result)
    {
        var lines = (result.TextBlocks ?? Array.Empty<RapidOcrNet.TextBlock>())
            .Select(block =>
            {
                var linePolygon = ToPolygon(block.BoxPoints);
                var words = (block.WordResults ?? Array.Empty<RapidOcrNet.WordBox>())
                    .Select(word => NormalizeRapidWord(word))
                    .ToList();

                if (words.Count == 0 && !string.IsNullOrWhiteSpace(block.Text))
                {
                    var lineBounds = Bounds(linePolygon);
                    words.Add(new OcrWordResult(
                        block.Text,
                        lineBounds,
                        Math.Clamp(block.BoxScore, 0, 1),
                        linePolygon));
                }

                var bounds = words.Count > 0
                    ? Bounds(words.SelectMany(word => word.Polygon).ToList())
                    : Bounds(linePolygon);
                return new OcrLineResult(block.Text, words, bounds);
            })
            .ToList();

        var text = string.IsNullOrWhiteSpace(result.StrRes)
            ? string.Join(Environment.NewLine, lines.Select(line => line.Text))
            : result.StrRes;
        return new OcrRecognitionResult(text, lines, OcrEngineKind.RapidOcr);
    }

    private static OcrWordResult NormalizeRapidWord(RapidOcrNet.WordBox word)
    {
        var polygon = ToPolygon(word.BoxPoints);
        return new OcrWordResult(
            word.Text,
            Bounds(polygon),
            Math.Clamp(word.Score, 0, 1),
            polygon);
    }

    private static async Task<OcrRecognitionResult?> TryRecognizeWithWindowsAiAsync(SoftwareBitmap bitmap)
    {
        // TextRecognizer is a Windows 11 24H2+ AI surface. The catch-all is intentional: an
        // older OS, missing NPU model, absent Windows App SDK runtime, or failed model readiness
        // must transparently use the already-supported Windows.Media.Ocr path.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)) return null;

        try
        {
            if (WindowsAiTextRecognizer.GetReadyState() == AIFeatureReadyState.NotReady)
            {
                var ready = await WindowsAiTextRecognizer.EnsureReadyAsync();
                if (ready.Status != AIFeatureReadyResultState.Success) return null;
            }

            using var recognizer = await WindowsAiTextRecognizer.CreateAsync();
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

    private static IReadOnlyList<SKPoint> ToPolygon(SKPointI[]? points) =>
        points is null
            ? Array.Empty<SKPoint>()
            : points.Select(point => new SKPoint(point.X, point.Y)).ToArray();

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
