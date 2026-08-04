using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Editor;

/// <summary>Projects positioned OCR lines into editable image-space text annotations.</summary>
public static class OcrOverlayBuilder
{
    public static IReadOnlyList<TextShape> CreateShapes(
        OcrRecognitionResult result,
        SKBitmap background)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(background);

        var shapes = new List<TextShape>();
        foreach (var line in result.Lines)
        {
            var text = line.Text.Trim();
            var bounds = ClampBounds(line.BoundingBox, background.Width, background.Height);
            if (text.Length == 0 || bounds.Width <= 0 || bounds.Height <= 0) continue;

            var fontSize = Math.Clamp(bounds.Height * 0.82f, 10f, 96f);
            var widthLimitedSize = bounds.Width / Math.Max(1f, text.Length * 0.62f);
            fontSize = Math.Clamp(Math.Min(fontSize, widthLimitedSize), 8f, 96f);
            shapes.Add(new TextShape
            {
                X = bounds.Left,
                Y = bounds.Top,
                Text = text,
                FontSize = fontSize,
                StrokeColorArgb = ChooseReadableColor(background, bounds),
                DropShadow = true,
                Orientation = TextOrientation.Horizontal
            });
        }
        return shapes;
    }

    private static SKRect ClampBounds(SKRect bounds, int width, int height)
    {
        var left = Math.Clamp(bounds.Left, 0, width);
        var top = Math.Clamp(bounds.Top, 0, height);
        var right = Math.Clamp(bounds.Right, left, width);
        var bottom = Math.Clamp(bounds.Bottom, top, height);
        return new SKRect(left, top, right, bottom);
    }

    private static uint ChooseReadableColor(SKBitmap background, SKRect bounds)
    {
        var left = Math.Clamp((int)bounds.Left, 0, background.Width - 1);
        var right = Math.Clamp((int)MathF.Max(bounds.Left, bounds.Right - 1), 0, background.Width - 1);
        var top = Math.Clamp((int)bounds.Top, 0, background.Height - 1);
        var bottom = Math.Clamp((int)MathF.Max(bounds.Top, bounds.Bottom - 1), 0, background.Height - 1);
        var points = new[]
        {
            background.GetPixel(left, top),
            background.GetPixel(right, top),
            background.GetPixel(left, bottom),
            background.GetPixel(right, bottom),
            background.GetPixel((left + right) / 2, (top + bottom) / 2)
        };

        var luminance = points.Average(pixel =>
            (0.2126 * pixel.Red) + (0.7152 * pixel.Green) + (0.0722 * pixel.Blue));
        return luminance >= 145 ? 0xFF11111B : 0xFFFFFFFF;
    }
}
