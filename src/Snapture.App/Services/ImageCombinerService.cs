using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;

namespace Snapture.App.Services;

public enum ImageCombineLayout
{
    Vertical,
    Horizontal,
    Grid
}

public sealed record ImageCombinerOptions(
    ImageCombineLayout Layout = ImageCombineLayout.Vertical,
    int Gap = 16,
    uint BackgroundColor = 0xFF1E1E2E,
    int GridColumns = 2,
    string OutputFormat = "png")
{
    public void Validate()
    {
        if (Gap is < 0 or > 500)
            throw new ArgumentOutOfRangeException(nameof(Gap), "The gap must be between 0 and 500 pixels.");
        if (GridColumns is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(GridColumns), "Grid columns must be between 1 and 16.");
        if (!Enum.IsDefined(Layout))
            throw new ArgumentOutOfRangeException(nameof(Layout));
        if (!ImageConversionService.TryNormalizeFormat(OutputFormat, out _))
            throw new ArgumentException("Output format must be png, jpg, bmp, or webp.", nameof(OutputFormat));
    }
}

public sealed record ImageCombinerResult(
    string OutputPath,
    ImageCombineLayout Layout,
    int ImageCount,
    int Width,
    int Height);

/// <summary>Combines a bounded set of local still images into one raster.</summary>
public static class ImageCombinerService
{
    private const int MaxInputFiles = 100;
    private const int MaxDimension = 32_768;
    private const long MaxCanvasPixels = 100_000_000;

    public static ImageCombinerResult Combine(
        IReadOnlyList<string> inputPaths,
        string outputPath,
        ImageCombinerOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputPaths);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        options.Validate();
        if (inputPaths.Count is < 2 or > MaxInputFiles)
            throw new ArgumentOutOfRangeException(nameof(inputPaths), $"Choose between 2 and {MaxInputFiles} images.");

        string output = ResolveOutputPath(outputPath, options.OutputFormat);
        var normalizedInputs = inputPaths
            .Select(path => Path.GetFullPath(path))
            .ToArray();
        if (normalizedInputs.Any(path => string.Equals(path, output, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The output file must differ from every source image.");

        var bitmaps = new List<Bitmap>(normalizedInputs.Length);
        try
        {
            foreach (string input in normalizedInputs)
                bitmaps.Add(LoadBitmap(input));

            var canvas = CalculateCanvas(bitmaps, options);
            using var composed = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(composed))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.Clear(ToColor(options.BackgroundColor));
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                foreach (var placement in canvas.Placements)
                    graphics.DrawImageUnscaled(placement.Bitmap, placement.X, placement.Y);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
            using var png = new MemoryStream();
            composed.Save(png, ImageFormat.Png);
            png.Position = 0;
            using var encoded = new MagickImage(png);
            if (string.Equals(options.OutputFormat, "jpg", StringComparison.OrdinalIgnoreCase))
            {
                encoded.BackgroundColor = MagickColors.White;
                encoded.Alpha(AlphaOption.Remove);
                encoded.Quality = 92;
            }
            encoded.Write(output, ToMagickFormat(options.OutputFormat));
            return new ImageCombinerResult(output, options.Layout, bitmaps.Count, canvas.Width, canvas.Height);
        }
        finally
        {
            foreach (var bitmap in bitmaps)
                bitmap.Dispose();
        }
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var input = SafeImageInput.Open(path);
        if (input.Info.Width is 0 or > MaxDimension || input.Info.Height is 0 or > MaxDimension)
            throw new InvalidDataException($"The source image dimensions exceed {MaxDimension} pixels.");

        using var source = new MagickImage(input.Stream);
        source.Format = MagickFormat.Png;
        using var png = new MemoryStream();
        source.Write(png);
        png.Position = 0;
        return new Bitmap(png);
    }

    private static CanvasPlan CalculateCanvas(IReadOnlyList<Bitmap> bitmaps, ImageCombinerOptions options)
    {
        return options.Layout switch
        {
            ImageCombineLayout.Vertical => PlanVertical(bitmaps, options.Gap),
            ImageCombineLayout.Horizontal => PlanHorizontal(bitmaps, options.Gap),
            ImageCombineLayout.Grid => PlanGrid(bitmaps, options.Gap, options.GridColumns),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Layout))
        };
    }

    private static CanvasPlan PlanVertical(IReadOnlyList<Bitmap> bitmaps, int gap)
    {
        int width = bitmaps.Max(bitmap => bitmap.Width);
        int height = CheckedDimension(bitmaps.Sum(bitmap => (long)bitmap.Height) + (long)gap * (bitmaps.Count - 1));
        EnsureCanvas(width, height);
        int y = 0;
        var placements = new List<Placement>(bitmaps.Count);
        foreach (var bitmap in bitmaps)
        {
            placements.Add(new Placement(bitmap, (width - bitmap.Width) / 2, y));
            y = checked(y + bitmap.Height + gap);
        }
        return new CanvasPlan(width, height, placements);
    }

    private static CanvasPlan PlanHorizontal(IReadOnlyList<Bitmap> bitmaps, int gap)
    {
        int width = CheckedDimension(bitmaps.Sum(bitmap => (long)bitmap.Width) + (long)gap * (bitmaps.Count - 1));
        int height = bitmaps.Max(bitmap => bitmap.Height);
        EnsureCanvas(width, height);
        int x = 0;
        var placements = new List<Placement>(bitmaps.Count);
        foreach (var bitmap in bitmaps)
        {
            placements.Add(new Placement(bitmap, x, (height - bitmap.Height) / 2));
            x = checked(x + bitmap.Width + gap);
        }
        return new CanvasPlan(width, height, placements);
    }

    private static CanvasPlan PlanGrid(IReadOnlyList<Bitmap> bitmaps, int gap, int requestedColumns)
    {
        int columns = Math.Min(requestedColumns, bitmaps.Count);
        int rows = (bitmaps.Count + columns - 1) / columns;
        var columnWidths = new int[columns];
        var rowHeights = new int[rows];
        for (int index = 0; index < bitmaps.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            columnWidths[column] = Math.Max(columnWidths[column], bitmaps[index].Width);
            rowHeights[row] = Math.Max(rowHeights[row], bitmaps[index].Height);
        }

        int width = CheckedDimension(columnWidths.Sum(value => (long)value) + (long)gap * (columns - 1));
        int height = CheckedDimension(rowHeights.Sum(value => (long)value) + (long)gap * (rows - 1));
        EnsureCanvas(width, height);
        var xOffsets = PrefixOffsets(columnWidths, gap);
        var yOffsets = PrefixOffsets(rowHeights, gap);
        var placements = new List<Placement>(bitmaps.Count);
        for (int index = 0; index < bitmaps.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            placements.Add(new Placement(
                bitmaps[index],
                xOffsets[column] + (columnWidths[column] - bitmaps[index].Width) / 2,
                yOffsets[row] + (rowHeights[row] - bitmaps[index].Height) / 2));
        }
        return new CanvasPlan(width, height, placements);
    }

    private static int[] PrefixOffsets(IReadOnlyList<int> sizes, int gap)
    {
        var offsets = new int[sizes.Count];
        for (int index = 1; index < sizes.Count; index++)
            offsets[index] = checked(offsets[index - 1] + sizes[index - 1] + gap);
        return offsets;
    }

    private static void EnsureCanvas(int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > MaxCanvasPixels)
            throw new InvalidDataException("The combined canvas exceeds the 100 million pixel safety limit.");
    }

    private static int CheckedDimension(long value) => value is < 1 or > int.MaxValue
        ? throw new InvalidDataException("The combined canvas dimensions are too large.")
        : (int)value;

    private static string ResolveOutputPath(string outputPath, string format)
    {
        string normalized = ImageConversionService.TryNormalizeFormat(format, out var value)
            ? value
            : throw new ArgumentException("Unsupported output format.", nameof(format));
        string output = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(output);
        if (string.IsNullOrEmpty(extension))
            return output + "." + normalized;
        if (!ImageConversionService.TryNormalizeFormat(extension, out var existing)
            || !string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The output extension must match the requested {normalized} format.", nameof(outputPath));
        return output;
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

    private sealed record Placement(Bitmap Bitmap, int X, int Y);

    private sealed record CanvasPlan(int Width, int Height, IReadOnlyList<Placement> Placements);
}
