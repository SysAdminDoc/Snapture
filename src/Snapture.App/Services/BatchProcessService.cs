using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ImageMagick;

namespace Snapture.App.Services;

public sealed record BatchProcessOptions(
    int ResizePercent = 100,
    int BorderWidth = 0,
    uint BorderColor = 0xFF888888,
    string WatermarkText = "",
    string OutputFormat = "png")
{
    public void Validate()
    {
        if (ResizePercent is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(ResizePercent), "Resize must be between 1 and 1000 percent.");
        if (BorderWidth is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(BorderWidth), "Border width must be between 0 and 100 pixels.");
        if (WatermarkText.Length > 200)
            throw new ArgumentException("Watermark text must be 200 characters or fewer.", nameof(WatermarkText));
        if (!ImageConversionService.TryNormalizeFormat(OutputFormat, out _))
            throw new ArgumentException("Output format must be png, jpg, bmp, or webp.", nameof(OutputFormat));
    }
}

public sealed record BatchProcessItemResult(
    string InputPath,
    string? OutputPath,
    string? Error)
{
    public bool Succeeded => OutputPath is not null && Error is null;
}

/// <summary>Applies a bounded local effect chain to a folder of still images.</summary>
public static class BatchProcessService
{
    private const int MaxFiles = 1_000;

    public static IReadOnlyList<BatchProcessItemResult> ProcessDirectory(
        string inputDirectory,
        string outputDirectory,
        BatchProcessOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        options.Validate();
        string input = Path.GetFullPath(inputDirectory);
        string output = Path.GetFullPath(outputDirectory);
        if (!Directory.Exists(input))
            throw new DirectoryNotFoundException($"The input folder does not exist: {input}");
        Directory.CreateDirectory(output);

        var paths = Directory.EnumerateFiles(input, "*.*", SearchOption.TopDirectoryOnly)
            .Where(path => SafeImageInput.IsSupportedExtension(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles + 1)
            .ToArray();
        if (paths.Length > MaxFiles)
            throw new InvalidOperationException($"A batch is limited to {MaxFiles} image files.");

        return paths.Select(path => ProcessOne(path, output, options)).ToArray();
    }

    public static BatchProcessItemResult ProcessOne(
        string inputPath,
        string outputDirectory,
        BatchProcessOptions options)
    {
        string fullInput = Path.GetFullPath(inputPath);
        string fullOutputDirectory = Path.GetFullPath(outputDirectory);
        try
        {
            options.Validate();
            using var sourceInput = SafeImageInput.Open(fullInput);

            Directory.CreateDirectory(fullOutputDirectory);
            string outputPath = UniqueOutputPath(fullInput, fullOutputDirectory, options.OutputFormat);
            using var sourceMagick = new MagickImage(sourceInput.Stream);
            sourceMagick.Format = MagickFormat.Png;
            using var sourcePng = new MemoryStream();
            sourceMagick.Write(sourcePng);
            sourcePng.Position = 0;
            using var source = new Bitmap(sourcePng);

            int width = Math.Max(1, (int)Math.Round(source.Width * options.ResizePercent / 100d));
            int height = Math.Max(1, (int)Math.Round(source.Height * options.ResizePercent / 100d));
            int canvasWidth = checked(width + options.BorderWidth * 2);
            int canvasHeight = checked(height + options.BorderWidth * 2);
            using var canvas = new Bitmap(canvasWidth, canvasHeight, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(canvas))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(ToColor(options.BorderColor));
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(options.BorderWidth, options.BorderWidth, width, height));

                if (!string.IsNullOrWhiteSpace(options.WatermarkText))
                    DrawWatermark(graphics, options.WatermarkText, canvasWidth, canvasHeight);
            }

            using var composedPng = new MemoryStream();
            canvas.Save(composedPng, ImageFormat.Png);
            composedPng.Position = 0;
            using var outputImage = new MagickImage(composedPng);
            outputImage.Quality = string.Equals(options.OutputFormat, "jpg", StringComparison.OrdinalIgnoreCase) ? 92u : 0u;
            outputImage.Write(outputPath, ToMagickFormat(options.OutputFormat));
            return new BatchProcessItemResult(fullInput, outputPath, null);
        }
        catch (Exception ex)
        {
            return new BatchProcessItemResult(fullInput, null, ex.Message);
        }
    }

    private static string UniqueOutputPath(string inputPath, string outputDirectory, string format)
    {
        string normalized = ImageConversionService.TryNormalizeFormat(format, out var value) ? value : format.TrimStart('.').ToLowerInvariant();
        string stem = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputPath) + "_snapture-batch");
        string candidate = stem + "." + normalized;
        for (int index = 1; File.Exists(candidate); index++)
            candidate = $"{stem}_{index}.{normalized}";
        return candidate;
    }

    private static void DrawWatermark(Graphics graphics, string text, int width, int height)
    {
        using var font = new Font("Segoe UI", Math.Max(10, Math.Min(width, height) / 24f), FontStyle.Bold, GraphicsUnit.Pixel);
        using var shadow = new SolidBrush(Color.FromArgb(180, 0, 0, 0));
        using var foreground = new SolidBrush(Color.FromArgb(235, 255, 255, 255));
        var bounds = new RectangleF(12, 12, Math.Max(1, width - 24), Math.Max(1, height - 24));
        var format = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
        graphics.DrawString(text, font, shadow, new RectangleF(bounds.X + 2, bounds.Y + 2, bounds.Width, bounds.Height), format);
        graphics.DrawString(text, font, foreground, bounds, format);
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (int)((argb >> 24) & 0xFF),
        (int)((argb >> 16) & 0xFF),
        (int)((argb >> 8) & 0xFF),
        (int)(argb & 0xFF));

    private static MagickFormat ToMagickFormat(string format) =>
        ImageConversionService.TryNormalizeFormat(format, out var normalized)
            ? normalized switch
            {
                "png" => MagickFormat.Png,
                "jpg" => MagickFormat.Jpeg,
                "bmp" => MagickFormat.Bmp,
                "webp" => MagickFormat.WebP,
                _ => throw new ArgumentException("Unsupported output format.", nameof(format))
            }
            : throw new ArgumentException("Unsupported output format.", nameof(format));
}
