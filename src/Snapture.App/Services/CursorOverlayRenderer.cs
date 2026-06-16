using System.Drawing;

namespace Snapture.App.Services;

internal static class CursorOverlayRenderer
{
    public const int ClickAnimationMilliseconds = 450;

    private static readonly BgraColor CursorColor = new(36, 230, 255);
    private static readonly BgraColor LeftClickColor = new(68, 214, 255);
    private static readonly BgraColor RightClickColor = new(255, 142, 214);
    private static readonly BgraColor MiddleClickColor = new(255, 255, 255);

    public static void RenderBgra(Span<byte> bgra, int width, int height, int stride, RecordingPointerFrame frame)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < stride * height)
            return;

        foreach (var click in frame.Clicks)
            DrawClick(bgra, width, height, stride, click);

        if (frame.CursorPosition is { } cursor)
        {
            DrawRing(bgra, width, height, stride, cursor, radius: 18.0, thickness: 3.0, CursorColor, alpha: 0.76);
            DrawRing(bgra, width, height, stride, cursor, radius: 7.0, thickness: 1.5, CursorColor, alpha: 0.42);
        }
    }

    private static void DrawClick(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        RecordingPointerEffect click)
    {
        double progress = Math.Clamp(click.AgeMilliseconds / ClickAnimationMilliseconds, 0.0, 1.0);
        double radius = 10.0 + (42.0 * progress);
        double thickness = Math.Max(2.0, 5.0 - (2.5 * progress));
        double alpha = 0.88 * Math.Pow(1.0 - progress, 1.15);
        var color = click.Button switch
        {
            RecordingPointerButton.Right => RightClickColor,
            RecordingPointerButton.Middle => MiddleClickColor,
            _ => LeftClickColor
        };

        DrawRing(bgra, width, height, stride, click.Position, radius, thickness, color, alpha);

        if (progress < 0.35)
        {
            double fillRadius = 8.0 + (10.0 * progress);
            double fillAlpha = 0.18 * (1.0 - progress / 0.35);
            DrawDisk(bgra, width, height, stride, click.Position, fillRadius, color, fillAlpha);
        }
    }

    private static void DrawRing(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        Point center,
        double radius,
        double thickness,
        BgraColor color,
        double alpha)
    {
        double reach = radius + thickness + 1.0;
        int minX = Math.Max(0, center.X - (int)Math.Ceiling(reach));
        int maxX = Math.Min(width - 1, center.X + (int)Math.Ceiling(reach));
        int minY = Math.Max(0, center.Y - (int)Math.Ceiling(reach));
        int maxY = Math.Min(height - 1, center.Y + (int)Math.Ceiling(reach));
        double halfThickness = Math.Max(0.5, thickness / 2.0);

        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - center.Y;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - center.X;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double edgeDistance = Math.Abs(distance - radius);
                if (edgeDistance > halfThickness + 1.0)
                    continue;

                double coverage = edgeDistance <= halfThickness
                    ? 1.0
                    : 1.0 - (edgeDistance - halfThickness);
                BlendPixel(bgra, (y * stride) + (x * 4), color, alpha * coverage);
            }
        }
    }

    private static void DrawDisk(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        Point center,
        double radius,
        BgraColor color,
        double alpha)
    {
        int minX = Math.Max(0, center.X - (int)Math.Ceiling(radius));
        int maxX = Math.Min(width - 1, center.X + (int)Math.Ceiling(radius));
        int minY = Math.Max(0, center.Y - (int)Math.Ceiling(radius));
        int maxY = Math.Min(height - 1, center.Y + (int)Math.Ceiling(radius));
        double radiusSquared = radius * radius;

        for (int y = minY; y <= maxY; y++)
        {
            int dy = y - center.Y;
            for (int x = minX; x <= maxX; x++)
            {
                int dx = x - center.X;
                double distanceSquared = dx * dx + dy * dy;
                if (distanceSquared > radiusSquared)
                    continue;

                double distance = Math.Sqrt(distanceSquared);
                double coverage = distance <= radius - 1.0 ? 1.0 : radius - distance;
                BlendPixel(bgra, (y * stride) + (x * 4), color, alpha * Math.Clamp(coverage, 0.0, 1.0));
            }
        }
    }

    private static void BlendPixel(Span<byte> bgra, int offset, BgraColor color, double alpha)
    {
        if (alpha <= 0.0)
            return;

        alpha = Math.Clamp(alpha, 0.0, 1.0);
        bgra[offset] = BlendChannel(bgra[offset], color.B, alpha);
        bgra[offset + 1] = BlendChannel(bgra[offset + 1], color.G, alpha);
        bgra[offset + 2] = BlendChannel(bgra[offset + 2], color.R, alpha);
        bgra[offset + 3] = 255;
    }

    private static byte BlendChannel(byte destination, byte source, double alpha)
        => (byte)Math.Clamp(Math.Round((destination * (1.0 - alpha)) + (source * alpha)), 0.0, 255.0);

    private readonly record struct BgraColor(byte B, byte G, byte R);
}
