using System.Drawing;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class EdgeDetectionRulerServiceTests
{
    [TestMethod]
    public void FindsNearestContrastingEdgesInEachDirection()
    {
        using var image = new Bitmap(100, 80);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.FromArgb(24, 24, 32));
            using var panel = new SolidBrush(Color.FromArgb(220, 220, 228));
            graphics.FillRectangle(panel, 50, 30, 30, 20);
        }

        var measurement = EdgeDetectionRulerService.FindNearest(
            image,
            new Point(60, 40),
            new EdgeRulerOptions(MaxDistance: 80, SampleSpan: 6, Threshold: 30));

        Assert.AreEqual(EdgeDirection.Left, measurement.Left!.Direction);
        Assert.AreEqual(10, measurement.Left.Distance);
        Assert.AreEqual(EdgeDirection.Right, measurement.Right!.Direction);
        Assert.AreEqual(20, measurement.Right.Distance);
        Assert.AreEqual(EdgeDirection.Top, measurement.Top!.Direction);
        Assert.AreEqual(10, measurement.Top.Distance);
        Assert.AreEqual(EdgeDirection.Bottom, measurement.Bottom!.Direction);
        Assert.AreEqual(10, measurement.Bottom.Distance);
        Assert.AreEqual(10, measurement.Nearest!.Distance);
    }

    [TestMethod]
    public void ReturnsNoHitForUniformPixelsAndValidatesOptions()
    {
        using var image = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(image))
            graphics.Clear(Color.Black);

        var measurement = EdgeDetectionRulerService.FindNearest(image, new Point(10, 10));
        Assert.IsEmpty(measurement.Hits);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeRulerOptions(MaxDistance: 0).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new EdgeRulerOptions(Threshold: 256).Validate());
    }
}
