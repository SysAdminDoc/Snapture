using System.Drawing;
using System.Drawing.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class LazyContentStabilityTests
{
    [TestMethod]
    public void IdenticalFramesAreStable()
    {
        using var first = CreateBitmap(Color.CornflowerBlue);
        using var second = CreateBitmap(Color.CornflowerBlue);

        Assert.AreEqual(0, LazyContentStability.MeanAbsoluteDifference(first, second));
        Assert.IsTrue(LazyContentStability.IsStable(first, second));
    }

    [TestMethod]
    public void ChangedFramesExceedStabilityThreshold()
    {
        using var first = CreateBitmap(Color.Black);
        using var second = CreateBitmap(Color.White);

        Assert.IsGreaterThan(180, LazyContentStability.MeanAbsoluteDifference(first, second));
        Assert.IsFalse(LazyContentStability.IsStable(first, second));
    }

    private static Bitmap CreateBitmap(Color color)
    {
        var bitmap = new Bitmap(64, 48, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        return bitmap;
    }
}
