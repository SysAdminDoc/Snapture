using System.Globalization;

namespace Snapture.App.Services;

internal static class KeystrokeOverlayRenderer
{
    public const int DisplayMilliseconds = 1_800;

    private const int GlyphWidth = 5;
    private const int GlyphHeight = 7;
    private const int GlyphGap = 1;

    private static readonly BgraColor BoxFill = new(28, 31, 38);
    private static readonly BgraColor BoxBorder = new(98, 105, 122);
    private static readonly BgraColor TextColor = new(242, 244, 248);

    public static void RenderBgra(Span<byte> bgra, int width, int height, int stride, RecordingKeystrokeFrame frame)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || bgra.Length < stride * height)
            return;
        if (frame.Keystrokes.Count == 0)
            return;

        int scale = width < 900 ? 2 : 3;
        int paddingX = scale * 4;
        int paddingY = scale * 3;
        int lineHeight = (GlyphHeight * scale) + (paddingY * 2);
        int gap = scale * 3;
        int x = Math.Max(scale * 8, 16);
        int visibleCount = Math.Min(frame.Keystrokes.Count, 5);
        int totalHeight = (visibleCount * lineHeight) + ((visibleCount - 1) * gap);
        int y = Math.Max(scale * 8, height - totalHeight - Math.Max(scale * 8, 16));

        int first = frame.Keystrokes.Count - visibleCount;
        for (int i = first; i < frame.Keystrokes.Count; i++)
        {
            var key = frame.Keystrokes[i];
            string text = key.RepeatCount > 1
                ? string.Create(CultureInfo.InvariantCulture, $"{key.Text}X{key.RepeatCount}")
                : key.Text;

            double fadeStart = DisplayMilliseconds * 0.72;
            double alpha = key.AgeMilliseconds <= fadeStart
                ? 0.86
                : 0.86 * (1.0 - Math.Clamp((key.AgeMilliseconds - fadeStart) / (DisplayMilliseconds - fadeStart), 0.0, 1.0));

            int textWidth = MeasureText(text, scale);
            int boxWidth = textWidth + (paddingX * 2);
            DrawBox(bgra, width, height, stride, x, y, boxWidth, lineHeight, alpha);
            DrawText(bgra, width, height, stride, text, x + paddingX, y + paddingY, scale, alpha);
            y += lineHeight + gap;
        }
    }

    internal static int MeasureText(string text, int scale)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return text.Length * GlyphWidth * scale
            + Math.Max(0, text.Length - 1) * GlyphGap * scale;
    }

    private static void DrawBox(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        int x,
        int y,
        int boxWidth,
        int boxHeight,
        double alpha)
    {
        int minX = Math.Clamp(x, 0, width);
        int maxX = Math.Clamp(x + boxWidth, 0, width);
        int minY = Math.Clamp(y, 0, height);
        int maxY = Math.Clamp(y + boxHeight, 0, height);

        for (int yy = minY; yy < maxY; yy++)
        {
            for (int xx = minX; xx < maxX; xx++)
            {
                bool border = yy == minY || yy == maxY - 1 || xx == minX || xx == maxX - 1;
                BlendPixel(bgra, (yy * stride) + (xx * 4), border ? BoxBorder : BoxFill, border ? alpha : alpha * 0.72);
            }
        }
    }

    private static void DrawText(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        string text,
        int x,
        int y,
        int scale,
        double alpha)
    {
        int cursorX = x;
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            if (TryGetGlyph(c, out var rows))
                DrawGlyph(bgra, width, height, stride, rows, cursorX, y, scale, alpha);
            cursorX += (GlyphWidth + GlyphGap) * scale;
        }
    }

    private static void DrawGlyph(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        byte[] rows,
        int x,
        int y,
        int scale,
        double alpha)
    {
        for (int row = 0; row < GlyphHeight; row++)
        {
            for (int col = 0; col < GlyphWidth; col++)
            {
                if ((rows[row] & (1 << (GlyphWidth - col - 1))) == 0)
                    continue;

                DrawScaledPixel(bgra, width, height, stride, x + (col * scale), y + (row * scale), scale, alpha);
            }
        }
    }

    private static void DrawScaledPixel(
        Span<byte> bgra,
        int width,
        int height,
        int stride,
        int x,
        int y,
        int scale,
        double alpha)
    {
        for (int yy = 0; yy < scale; yy++)
        {
            int py = y + yy;
            if ((uint)py >= (uint)height)
                continue;

            for (int xx = 0; xx < scale; xx++)
            {
                int px = x + xx;
                if ((uint)px >= (uint)width)
                    continue;

                BlendPixel(bgra, (py * stride) + (px * 4), TextColor, alpha);
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

    private static bool TryGetGlyph(char c, out byte[] rows)
    {
        rows = c switch
        {
            'A' => new byte[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
            'B' => new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110 },
            'C' => new byte[] { 0b01111, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b01111 },
            'D' => new byte[] { 0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110 },
            'E' => new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111 },
            'F' => new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000 },
            'G' => new byte[] { 0b01111, 0b10000, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111 },
            'H' => new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
            'I' => new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b11111 },
            'J' => new byte[] { 0b00111, 0b00010, 0b00010, 0b00010, 0b10010, 0b10010, 0b01100 },
            'K' => new byte[] { 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001 },
            'L' => new byte[] { 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111 },
            'M' => new byte[] { 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001 },
            'N' => new byte[] { 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001, 0b10001 },
            'O' => new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
            'P' => new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000 },
            'Q' => new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101 },
            'R' => new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001 },
            'S' => new byte[] { 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110 },
            'T' => new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 },
            'U' => new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
            'V' => new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 },
            'W' => new byte[] { 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010 },
            'X' => new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001 },
            'Y' => new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100 },
            'Z' => new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111 },
            '0' => new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 },
            '1' => new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
            '2' => new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
            '3' => new byte[] { 0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110 },
            '4' => new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
            '5' => new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b00001, 0b00001, 0b11110 },
            '6' => new byte[] { 0b00111, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
            '7' => new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 },
            '8' => new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
            '9' => new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b11100 },
            '+' => new byte[] { 0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000 },
            '-' => new byte[] { 0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000 },
            _ => Array.Empty<byte>()
        };

        return rows.Length > 0;
    }

    private readonly record struct BgraColor(byte B, byte G, byte R);
}
