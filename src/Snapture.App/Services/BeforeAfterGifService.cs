using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;

namespace Snapture.App.Services;

public sealed record BeforeAfterGifOptions(
    int TransitionFrames = 12,
    int DelayMs = 100,
    uint BackgroundColor = 0xFF1E1E2E)
{
    public void Validate()
    {
        if (TransitionFrames is < 2 or > 60)
            throw new ArgumentOutOfRangeException(nameof(TransitionFrames), "Transition frames must be between 2 and 60.");
        if (DelayMs is < 20 or > 2_000)
            throw new ArgumentOutOfRangeException(nameof(DelayMs), "Frame delay must be between 20 and 2000 milliseconds.");
    }
}

public sealed record BeforeAfterGifResult(
    string OutputPath,
    int FrameCount,
    int Width,
    int Height);

/// <summary>Creates a local ping-pong cross-fade GIF from two still images.</summary>
public static class BeforeAfterGifService
{
    private const int MaxDimension = 8_192;
    private const long MaxCanvasPixels = 40_000_000;

    public static BeforeAfterGifResult CreateGif(
        string beforePath,
        string afterPath,
        string outputPath,
        BeforeAfterGifOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(beforePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(afterPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        options.Validate();
        string before = Path.GetFullPath(beforePath);
        string after = Path.GetFullPath(afterPath);
        string output = ResolveOutputPath(outputPath);
        if (string.Equals(output, before, StringComparison.OrdinalIgnoreCase)
            || string.Equals(output, after, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The output file must differ from both source images.");

        using var first = LoadBitmap(before);
        using var second = LoadBitmap(after);
        int width = Math.Max(first.Width, second.Width);
        int height = Math.Max(first.Height, second.Height);
        if (width < 1 || height < 1 || (long)width * height > MaxCanvasPixels)
            throw new InvalidDataException("The comparison canvas exceeds the 40 million pixel safety limit.");

        var frames = new List<Bitmap>(options.TransitionFrames * 2 - 2);
        try
        {
            for (int index = 0; index < options.TransitionFrames; index++)
            {
                double alpha = index / (double)(options.TransitionFrames - 1);
                frames.Add(CreateBlendFrame(first, second, width, height, alpha, options.BackgroundColor));
            }
            for (int index = options.TransitionFrames - 2; index > 0; index--)
            {
                double alpha = index / (double)(options.TransitionFrames - 1);
                frames.Add(CreateBlendFrame(first, second, width, height, alpha, options.BackgroundColor));
            }

            GifEncoder.Encode(
                output,
                frames.Select(frame => new GifFrameInput(frame, options.DelayMs)),
                GifEncodingOptions.Default);
            return new BeforeAfterGifResult(output, frames.Count, width, height);
        }
        finally
        {
            foreach (var frame in frames)
                frame.Dispose();
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

    private static Bitmap CreateBlendFrame(
        Bitmap first,
        Bitmap second,
        int width,
        int height,
        double alpha,
        uint backgroundColor)
    {
        var frame = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(frame);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.Clear(ToColor(backgroundColor));
        graphics.CompositingMode = CompositingMode.SourceOver;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        DrawCentered(graphics, first, width, height);
        if (alpha > 0)
        {
            using var attributes = new ImageAttributes();
            var matrix = new ColorMatrix { Matrix33 = (float)Math.Clamp(alpha, 0, 1) };
            attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            var destination = new Rectangle(
                (width - second.Width) / 2,
                (height - second.Height) / 2,
                second.Width,
                second.Height);
            graphics.DrawImage(second, destination, 0, 0, second.Width, second.Height, GraphicsUnit.Pixel, attributes);
        }
        return frame;
    }

    private static void DrawCentered(Graphics graphics, Bitmap bitmap, int width, int height)
        => graphics.DrawImageUnscaled(bitmap, (width - bitmap.Width) / 2, (height - bitmap.Height) / 2);

    private static string ResolveOutputPath(string outputPath)
    {
        string output = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(output);
        if (string.IsNullOrEmpty(extension))
            return output + ".gif";
        if (!extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The before/after output must use the .gif extension.", nameof(outputPath));
        return output;
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (int)((argb >> 24) & 0xFF),
        (int)((argb >> 16) & 0xFF),
        (int)((argb >> 8) & 0xFF),
        (int)(argb & 0xFF));
}
