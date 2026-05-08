using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapture.App.Services;

/// <summary>
/// Wraps <see cref="OcrEngine"/>. Default path uses the system's installed-language pack and is
/// effectively zero-install on any modern Windows. RapidOCR ONNX (~50 MB) is the planned bundled
/// fallback for languages the user hasn't installed; that ships in v0.4 once the model download
/// flow exists.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class OcrService
{
    /// <summary>OCR a WPF <see cref="BitmapSource"/>. Returns the recognised text and word boxes.</summary>
    public static async Task<OcrResult?> RecognizeAsync(BitmapSource source, string? languageTag = null)
    {
        var ocr = ResolveEngine(languageTag);
        if (ocr is null) return null;

        // Convert WPF BitmapSource → SoftwareBitmap (BGRA8 premultiplied).
        var swBmp = await ConvertToSoftwareBitmapAsync(source);
        try
        {
            return await ocr.RecognizeAsync(swBmp);
        }
        finally
        {
            swBmp.Dispose();
        }
    }

    /// <summary>List languages installed on this system that <see cref="OcrEngine"/> can use.</summary>
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

    private static OcrEngine? ResolveEngine(string? languageTag)
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
        // Encode the BitmapSource to a PNG stream, then decode through Windows.Graphics.Imaging
        // so we can hand a SoftwareBitmap to the OCR engine.
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        encoder.Save(ms);
        ms.Position = 0;

        var ras = new InMemoryRandomAccessStream();
        using (var dataWriter = new DataWriter(ras))
        {
            dataWriter.WriteBytes(ms.ToArray());
            await dataWriter.StoreAsync();
            await dataWriter.FlushAsync();
            dataWriter.DetachStream();
        }
        ras.Seek(0);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ras);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
