using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ArrowGeometryTests
{
    [TestMethod]
    public void ControlPointBendsOnTheExpectedSideAndEndpointsRemainStable()
    {
        var start = new SKPoint(0, 0);
        var end = new SKPoint(100, 0);
        var control = ArrowGeometry.GetControlPoint(start, end, 1);

        Assert.AreEqual(new SKPoint(50, 45), control);
        Assert.AreEqual(start, ArrowGeometry.PointOnQuadratic(start, control, end, 0));
        Assert.AreEqual(end, ArrowGeometry.PointOnQuadratic(start, control, end, 1));
    }

    [TestMethod]
    public void NegativeCurveMirrorsTheControlPoint()
    {
        var control = ArrowGeometry.GetControlPoint(new SKPoint(10, 20), new SKPoint(110, 20), -0.5f);

        Assert.AreEqual(new SKPoint(60, -2.5f), control);
    }

    [TestMethod]
    public void ArrowClonePreservesStyleAndCurve()
    {
        var original = new ArrowShape
        {
            X1 = 10,
            Y1 = 20,
            X2 = 110,
            Y2 = 80,
            Style = ArrowStyle.Modern,
            Curve = -0.65f,
            Bidirectional = true
        };

        var clone = (ArrowShape)original.Clone();

        Assert.AreEqual(original.Style, clone.Style);
        Assert.AreEqual(original.Curve, clone.Curve, 0.0001f);
        Assert.AreEqual(original.Bidirectional, clone.Bidirectional);
    }

    [TestMethod]
    public void ProjectSerializationRoundTripsModernCurve()
    {
        using var background = new SKBitmap(24, 24);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(new ArrowShape
        {
            X1 = 2,
            Y1 = 4,
            X2 = 20,
            Y2 = 18,
            Style = ArrowStyle.Modern,
            Curve = 0.4f
        });

        using var restoredBackground = new SKBitmap(24, 24);
        var restored = new AnnotationDocument(restoredBackground);
        restored.DeserializeShapes(document.SerializeShapes());

        var arrow = (ArrowShape)restored.Shapes.Single();
        Assert.AreEqual(ArrowStyle.Modern, arrow.Style);
        Assert.AreEqual(0.4f, arrow.Curve, 0.0001f);
    }

    [TestMethod]
    public void ArrowBoundsIncludeTheQuadraticBend()
    {
        var arrow = new ArrowShape { X1 = 0, Y1 = 0, X2 = 100, Y2 = 0, Curve = 1 };

        var bounds = arrow.GetBounds();

        Assert.IsLessThan(0f, bounds.Top);
        Assert.IsGreaterThan(30f, bounds.Bottom);
    }
}
