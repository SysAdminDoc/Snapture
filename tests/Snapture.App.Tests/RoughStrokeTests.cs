using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class RoughStrokeTests
{
    [TestMethod]
    public void ZeroSloppinessKeepsTheOriginalPolyline()
    {
        var points = new[] { new SKPoint(0, 0), new SKPoint(40, 20), new SKPoint(80, 0) };

        var generated = RoughStroke.GeneratePolylinePoints(points, 0, 3, 17);

        CollectionAssert.AreEqual(points, generated.ToArray());
    }

    [TestMethod]
    public void SloppyPolylineIsDeterministicAndWobblesInteriorPoints()
    {
        var points = new[] { new SKPoint(0, 0), new SKPoint(120, 0) };

        var first = RoughStroke.GeneratePolylinePoints(points, 0.75f, 3, 17);
        var second = RoughStroke.GeneratePolylinePoints(points, 0.75f, 3, 17);

        CollectionAssert.AreEqual(first.ToArray(), second.ToArray());
        Assert.AreEqual(points[0], first[0]);
        Assert.AreEqual(points[1], first[^1]);
        Assert.IsTrue(first.Skip(1).Take(first.Count - 2).Any(point => Math.Abs(point.Y) > 0.01f));
    }

    [TestMethod]
    public void ShapeClonePreservesSloppiness()
    {
        var original = new RectangleShape { Width = 100, Height = 50, Sloppiness = 0.6f };

        var clone = original.Clone();

        Assert.AreEqual(original.Sloppiness, clone.Sloppiness, 0.0001f);
    }
}
