using System.Drawing;
using System.IO;
using System.Windows.Media.Imaging;
using ImageMagick;
using Serilog;

namespace Snapture.App.Services;

internal sealed record HdrSaveResult(
    string PngPath,
    string? JxlPath,
    string? AvifPath,
    string? JxrPath)
{
    public int WrittenCount => 1
        + (JxlPath is null ? 0 : 1)
        + (AvifPath is null ? 0 : 1)
        + (JxrPath is null ? 0 : 1);
}

/// <summary>
/// Writes the fixed HDR delivery set. The capture/editor boundary is currently a
/// tone-mapped Bitmap so annotations and redactions are identical across variants;
/// PNG remains the history/editor primary while modern siblings serve archival and
/// sharing workflows. A display ICC profile is embedded in the PNG when WCS exposes
/// one for the single-monitor source.
/// </summary>
internal static class HdrSavePolicy
{
    private static readonly string[] ModernExtensions = { ".png", ".jxl", ".avif" };

    public static HdrSaveResult Save(
        string outputStem,
        Bitmap bitmap,
        bool writeJxr,
        byte[]? iccProfile = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputStem);
        ArgumentNullException.ThrowIfNull(bitmap);

        string stem = ResolveUniqueStem(outputStem, writeJxr);
        string pngPath = stem + ".png";
        byte[] pngBytes = PngIccProfileEmbedder.Encode(bitmap, iccProfile);
        File.WriteAllBytes(pngPath, pngBytes);

        string? jxlPath = TryWriteMagickVariant(pngBytes, stem + ".jxl", MagickFormat.Jxl, quality: 100);
        string? avifPath = TryWriteMagickVariant(pngBytes, stem + ".avif", MagickFormat.Avif, quality: 90);
        string? jxrPath = writeJxr ? TryWriteJxrVariant(pngBytes, stem + ".jxr") : null;

        return new HdrSaveResult(pngPath, jxlPath, avifPath, jxrPath);
    }

    private static string? TryWriteMagickVariant(
        byte[] pngBytes,
        string outputPath,
        MagickFormat format,
        int quality)
    {
        if (!MagickNET.SupportedFormats.Any(info => info.Format == format && info.SupportsWriting))
        {
            Log.Warning("HdrSave.{Format}Unavailable — installed ImageMagick delegate cannot write it", format);
            return null;
        }

        try
        {
            using var input = new MemoryStream(pngBytes, writable: false);
            using var image = new MagickImage(input);
            image.Quality = (uint)quality;
            image.Write(outputPath, format);
            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HdrSave.{Format}Failed", format);
            TryDelete(outputPath);
            return null;
        }
    }

    private static string? TryWriteJxrVariant(byte[] pngBytes, string outputPath)
    {
        try
        {
            using var input = new MemoryStream(pngBytes, writable: false);
            var source = new BitmapImage();
            source.BeginInit();
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.StreamSource = input;
            source.EndInit();
            source.Freeze();

            var encoder = new WmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = File.Create(outputPath);
            encoder.Save(output);
            return outputPath;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HdrSave.JxrFailed — WIC JXR is optional and SDR-clamped");
            TryDelete(outputPath);
            return null;
        }
    }

    private static string ResolveUniqueStem(string outputStem, bool writeJxr)
    {
        string directory = Path.GetDirectoryName(outputStem) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(outputStem);
        Directory.CreateDirectory(directory);

        string candidate = Path.Combine(directory, fileName);
        int suffix = 1;
        while (HasExistingVariant(candidate, writeJxr))
            candidate = Path.Combine(directory, $"{fileName}_{suffix++}");
        return candidate;
    }

    private static bool HasExistingVariant(string stem, bool writeJxr)
    {
        foreach (string extension in ModernExtensions)
        {
            if (File.Exists(stem + extension)) return true;
        }
        return writeJxr && File.Exists(stem + ".jxr");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }
}
