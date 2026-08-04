using System.Drawing;
using Snapture.Capture;

namespace Snapture.Capture.Tests;

public sealed class OmnidirectionalImageStitcherTests
{
    [Fact]
    public void PlacesTilesAtHorizontalAndVerticalOffsets()
    {
        using var first = CreateTile(Color.Red, 10, 8);
        using var second = CreateTile(Color.Blue, 8, 6);
        using var stitched = OmnidirectionalImageStitcher.Stitch(new[]
        {
            new OmnidirectionalScrollTile(first, 0, 0),
            new OmnidirectionalScrollTile(second, 8, 6)
        });

        Assert.Equal(16, stitched.Width);
        Assert.Equal(12, stitched.Height);
        Assert.Equal(Color.Red.ToArgb(), stitched.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), stitched.GetPixel(12, 10).ToArgb());
    }

    [Fact]
    public void RejectsEmptyTileSets()
    {
        Assert.Throws<InvalidOperationException>(() => OmnidirectionalImageStitcher.Stitch(Array.Empty<OmnidirectionalScrollTile>()));
    }

    private static Bitmap CreateTile(Color color, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }
}
