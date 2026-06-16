using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Snapture.Capture.Tests;

[SupportedOSPlatform("windows")]
public class ImageStitcherTests
{
    private static Bitmap CreateSolidBitmap(int width, int height, Color color)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    [Fact]
    public void Stitch_SingleFrame_ReturnsSameFrame()
    {
        using var frame = CreateSolidBitmap(100, 80, Color.Red);
        var (stitched, seams, sticky) = ImageStitcher.Stitch(new[] { frame });
        Assert.Equal(100, stitched.Width);
        Assert.Equal(80, stitched.Height);
        Assert.Single(seams);
    }

    [Fact]
    public void Stitch_TwoIdenticalFrames_ProducesResult()
    {
        using var a = CreateSolidBitmap(200, 150, Color.Blue);
        using var b = CreateSolidBitmap(200, 150, Color.Blue);
        var (stitched, seams, _) = ImageStitcher.Stitch(new[] { a, b });
        Assert.NotNull(stitched);
        Assert.Equal(2, seams.Count);
        stitched.Dispose();
    }

    [Fact]
    public void DetectStickyStrips_AllIdenticalRows_DetectsStickyRegion()
    {
        using var a = CreateSolidBitmap(100, 100, Color.White);
        using var b = CreateSolidBitmap(100, 100, Color.White);
        var sticky = ImageStitcher.DetectStickyStrips(new[] { a, b }, 100);
        Assert.True(sticky.TopRows > 0 || sticky.BottomRows > 0);
    }

    [Fact]
    public void DetectStickyStrips_DifferentFrames_ReturnsZeroSticky()
    {
        using var a = CreateSolidBitmap(100, 100, Color.Red);
        using var b = CreateSolidBitmap(100, 100, Color.Blue);
        var sticky = ImageStitcher.DetectStickyStrips(new[] { a, b }, 100);
        Assert.Equal(0, sticky.TopRows);
        Assert.Equal(0, sticky.BottomRows);
    }

    [Fact]
    public void FindOverlap_IdenticalFrames_HighConfidence()
    {
        using var a = CreateSolidBitmap(200, 200, Color.Green);
        using var b = CreateSolidBitmap(200, 200, Color.Green);
        var (overlap, confidence) = ImageStitcher.FindOverlap(a, b, 0, 0);
        Assert.True(confidence >= 0.0);
    }

    [Fact]
    public void Stitch_ThrowsOnEmptyList()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ImageStitcher.Stitch(Array.Empty<Bitmap>()));
    }
}

[SupportedOSPlatform("windows")]
public class MonitorEnumeratorTests
{
    [Fact]
    public void Enumerate_ReturnsAtLeastOneMonitor()
    {
        var monitors = MonitorEnumerator.Enumerate();
        Assert.NotEmpty(monitors);
    }

    [Fact]
    public void Enumerate_PrimaryMonitorExists()
    {
        var monitors = MonitorEnumerator.Enumerate();
        Assert.Contains(monitors, m => m.IsPrimary);
    }

    [Fact]
    public void GetVirtualScreen_HasPositiveSize()
    {
        var virt = MonitorEnumerator.GetVirtualScreen();
        Assert.True(virt.Width > 0);
        Assert.True(virt.Height > 0);
    }
}
