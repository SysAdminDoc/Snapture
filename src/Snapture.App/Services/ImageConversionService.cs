using ImageMagick;
using System.IO;

namespace Snapture.App.Services;

internal sealed record ImageConversionResult(
    string OutputPath,
    string Format,
    int Width,
    int Height);

/// <summary>Local image conversion and resize used by Explorer verbs and the CLI.</summary>
internal static class ImageConversionService
{
    private static readonly IReadOnlyDictionary<string, MagickFormat> Formats =
        new Dictionary<string, MagickFormat>(StringComparer.OrdinalIgnoreCase)
        {
            ["png"] = MagickFormat.Png,
            ["jpg"] = MagickFormat.Jpeg,
            ["jpeg"] = MagickFormat.Jpeg,
            ["bmp"] = MagickFormat.Bmp,
            ["webp"] = MagickFormat.WebP
        };

    public static ImageConversionResult Convert(
        string inputPath,
        string? requestedFormat = null,
        int resizePercent = 0,
        string? outputPath = null,
        ExportMetadataOptions? metadataOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        string input = Path.GetFullPath(inputPath);
        using var sourceInput = SafeImageInput.Open(input);

        if (resizePercent != 0 && (resizePercent < 1 || resizePercent > 1000))
            throw new ArgumentOutOfRangeException(nameof(resizePercent), "Resize must be between 1 and 1000 percent.");

        string format = ResolveFormat(requestedFormat, outputPath, input);
        string output = ResolveOutputPath(input, format, resizePercent, outputPath);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The output path must differ from the source image.");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var sourceMetadata = ExportMetadataService.TryReadSource(input);
        using var image = new MagickImage(sourceInput.Stream);
        if (resizePercent != 0 && resizePercent != 100)
        {
            uint width = Math.Max(1u, (uint)Math.Round(image.Width * resizePercent / 100d));
            uint height = Math.Max(1u, (uint)Math.Round(image.Height * resizePercent / 100d));
            image.Resize(width, height);
        }

        MagickFormat targetFormat = Formats[format];
        if (targetFormat == MagickFormat.Jpeg && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaOption.Remove);
        }
        image.Quality = targetFormat == MagickFormat.Jpeg ? 92u : image.Quality;
        using var encoded = new MemoryStream();
        image.Write(encoded, targetFormat);
        var policy = metadataOptions ?? ExportMetadataOptions.Default;
        var metadata = ExportMetadataService.Apply(
            encoded.ToArray(),
            targetFormat,
            policy,
            sourceMetadata);
        File.WriteAllBytes(output, metadata.Bytes);
        ExportMetadataService.WriteProvenanceSidecar(
            output,
            metadata.Bytes,
            targetFormat,
            policy,
            metadata,
            input,
            isComposite: false,
            isRedacted: false,
            checked((int)image.Width),
            checked((int)image.Height));
        return new ImageConversionResult(output, format, checked((int)image.Width), checked((int)image.Height));
    }

    internal static bool TryNormalizeFormat(string? value, out string format)
    {
        format = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        string key = value.Trim().TrimStart('.');
        if (!Formats.ContainsKey(key)) return false;
        format = key.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : key.ToLowerInvariant();
        return true;
    }

    private static string ResolveFormat(string? requestedFormat, string? outputPath, string inputPath)
    {
        if (TryNormalizeFormat(requestedFormat, out var requested))
            return requested;
        if (!string.IsNullOrWhiteSpace(requestedFormat))
            throw new ArgumentException("Supported formats are png, jpg, bmp, and webp.", nameof(requestedFormat));

        string extension = Path.GetExtension(outputPath ?? inputPath);
        if (!TryNormalizeFormat(extension, out var inferred))
            throw new ArgumentException("An output format is required for this image.", nameof(requestedFormat));
        return inferred;
    }

    private static string ResolveOutputPath(
        string inputPath,
        string format,
        int resizePercent,
        string? requestedOutputPath)
    {
        string extension = "." + format;
        if (!string.IsNullOrWhiteSpace(requestedOutputPath))
        {
            string output = Path.GetFullPath(requestedOutputPath);
            string existingExtension = Path.GetExtension(output);
            if (string.IsNullOrEmpty(existingExtension))
                output += extension;
            else if (!TryNormalizeFormat(existingExtension, out var existingFormat)
                || !existingFormat.Equals(format, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"The output extension must match the requested {format} format.",
                    nameof(requestedOutputPath));
            return output;
        }

        string suffix = resizePercent is > 0 and not 100
            ? $"_snapture_{resizePercent}pct"
            : "_snapture";
        string stem = Path.Combine(
            Path.GetDirectoryName(inputPath)!,
            Path.GetFileNameWithoutExtension(inputPath) + suffix + extension);
        if (!File.Exists(stem)) return stem;

        for (int index = 1; ; index++)
        {
            string candidate = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                $"{Path.GetFileNameWithoutExtension(inputPath)}{suffix}_{index}{extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
