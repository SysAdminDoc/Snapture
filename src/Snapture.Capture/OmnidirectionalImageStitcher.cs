using System.Drawing;
using System.Drawing.Imaging;

namespace Snapture.Capture;

public sealed record OmnidirectionalScrollTile(Bitmap Bitmap, int OffsetX, int OffsetY);

/// <summary>Places horizontal, vertical, and diagonal scroll tiles on one bounded canvas.</summary>
public static class OmnidirectionalImageStitcher
{
    private const long MaxCanvasPixels = 100_000_000;

    public static Bitmap Stitch(IReadOnlyList<OmnidirectionalScrollTile> tiles)
    {
        ArgumentNullException.ThrowIfNull(tiles);
        if (tiles.Count == 0)
            throw new InvalidOperationException("No scroll tiles were captured.");

        int minX = tiles.Min(tile => tile.OffsetX);
        int minY = tiles.Min(tile => tile.OffsetY);
        int maxX = tiles.Max(tile => checked(tile.OffsetX + tile.Bitmap.Width));
        int maxY = tiles.Max(tile => checked(tile.OffsetY + tile.Bitmap.Height));
        int width = checked(maxX - minX);
        int height = checked(maxY - minY);
        if (width < 1 || height < 1 || (long)width * height > MaxCanvasPixels)
            throw new InvalidDataException("The omnidirectional stitched canvas exceeds the 100 million pixel safety limit.");

        var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.Transparent);
        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        foreach (var tile in tiles)
        {
            int x = checked(tile.OffsetX - minX);
            int y = checked(tile.OffsetY - minY);
            graphics.DrawImageUnscaled(tile.Bitmap, x, y);
        }
        return output;
    }
}
