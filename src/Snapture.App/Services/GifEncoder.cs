using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;

namespace Snapture.App.Services;

internal readonly record struct GifFrameInput(Bitmap Bitmap, int DelayMs);

internal sealed record GifEncodingOptions(int Colors = 256, double FuzzPercent = 1.0)
{
    public static GifEncodingOptions Default { get; } = new();

    public void Validate()
    {
        if (Colors is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(Colors), "GIF palettes must contain between 2 and 256 colors.");
        if (double.IsNaN(FuzzPercent) || double.IsInfinity(FuzzPercent) || FuzzPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(FuzzPercent), "GIF fuzz must be between 0 and 100 percent.");
    }
}

/// <summary>
/// Encodes the in-memory GIF frame model through ImageMagick's global palette and layer optimizer.
/// ColorFuzz is the managed equivalent of ImageMagick's -fuzz option and enables bounded lossy
/// optimization without changing the user's captured source bitmaps.
/// </summary>
internal static class GifEncoder
{
    private const int AnimationTicksPerSecond = 100;

    public static void Encode(
        string outputPath,
        IEnumerable<GifFrameInput> frames,
        GifEncodingOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(frames);
        options.Validate();

        var frameList = frames.ToList();
        if (frameList.Count == 0)
            throw new InvalidOperationException("No frames recorded.");

        var firstFrame = frameList[0].Bitmap;
        foreach (var frame in frameList)
        {
            if (frame.Bitmap.Width != firstFrame.Width || frame.Bitmap.Height != firstFrame.Height)
                throw new InvalidOperationException("All GIF frames must have the same dimensions.");
            if (frame.DelayMs < 20)
                throw new ArgumentOutOfRangeException(nameof(frames), "GIF frame delays must be at least 20 milliseconds.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var images = new MagickImageCollection();
        foreach (var frame in frameList)
        {
            using var png = new MemoryStream();
            frame.Bitmap.Save(png, ImageFormat.Png);
            png.Position = 0;

            var image = new MagickImage(png)
            {
                AnimationTicksPerSecond = AnimationTicksPerSecond,
                AnimationDelay = (uint)Math.Max(2, Math.Round(frame.DelayMs / 10.0)),
                GifDisposeMethod = GifDisposeMethod.Previous,
                ColorFuzz = new Percentage(options.FuzzPercent)
            };
            images.Add(image);
        }

        images.Quantize(new QuantizeSettings
        {
            Colors = (uint)options.Colors,
            DitherMethod = DitherMethod.FloydSteinberg
        });
        images.Optimize();
        images.Write(outputPath, MagickFormat.Gif);
    }
}
