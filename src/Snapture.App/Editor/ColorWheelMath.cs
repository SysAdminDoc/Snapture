namespace Snapture.App.Editor;

/// <summary>HSV helpers shared by the editor's radial colour picker and its tests.</summary>
internal static class ColorWheelMath
{
    public static bool TryFromPoint(double x, double y, double radius, byte alpha, out uint argb)
    {
        if (radius <= 0)
        {
            argb = 0;
            return false;
        }

        double distance = Math.Sqrt((x * x) + (y * y));
        if (distance > radius)
        {
            argb = 0;
            return false;
        }

        double hue = (Math.Atan2(y, x) * 180.0 / Math.PI + 360.0) % 360.0;
        double saturation = Math.Clamp(distance / radius, 0.0, 1.0);
        argb = FromHsv(hue, saturation, 1.0, alpha);
        return true;
    }

    public static uint FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        hue = ((hue % 360.0) + 360.0) % 360.0;
        saturation = Math.Clamp(saturation, 0.0, 1.0);
        value = Math.Clamp(value, 0.0, 1.0);

        double chroma = value * saturation;
        double sector = hue / 60.0;
        double x = chroma * (1.0 - Math.Abs((sector % 2.0) - 1.0));
        double m = value - chroma;

        double r, g, b;
        if (sector < 1) (r, g, b) = (chroma, x, 0);
        else if (sector < 2) (r, g, b) = (x, chroma, 0);
        else if (sector < 3) (r, g, b) = (0, chroma, x);
        else if (sector < 4) (r, g, b) = (0, x, chroma);
        else if (sector < 5) (r, g, b) = (x, 0, chroma);
        else (r, g, b) = (chroma, 0, x);

        return ((uint)alpha << 24)
             | ((uint)Math.Round((r + m) * 255.0) << 16)
             | ((uint)Math.Round((g + m) * 255.0) << 8)
             | (uint)Math.Round((b + m) * 255.0);
    }

    public static (double Hue, double Saturation, double Value, byte Alpha) ToHsv(uint argb)
    {
        double r = ((argb >> 16) & 0xFF) / 255.0;
        double g = ((argb >> 8) & 0xFF) / 255.0;
        double b = (argb & 0xFF) / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double hue = delta == 0
            ? 0
            : max == r
                ? 60.0 * (((g - b) / delta) % 6.0)
                : max == g
                    ? 60.0 * (((b - r) / delta) + 2.0)
                    : 60.0 * (((r - g) / delta) + 4.0);
        if (hue < 0) hue += 360.0;

        return (hue, max == 0 ? 0 : delta / max, max, (byte)(argb >> 24));
    }
}
