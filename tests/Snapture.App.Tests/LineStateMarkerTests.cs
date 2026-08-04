using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class LineStateMarkerTests
{
    [TestMethod]
    public void MarkerRoundTripsStateAndGeometry()
    {
        using var background = new SKBitmap(180, 100);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(new LineStateMarkerShape
        {
            X = 20,
            Y = 24,
            Width = 120,
            Height = 22,
            State = LineState.Removed,
            Sloppiness = 0.4f
        });

        using var restoredBackground = new SKBitmap(180, 100);
        var restored = new AnnotationDocument(restoredBackground);
        restored.DeserializeShapes(document.SerializeShapes());
        var marker = (LineStateMarkerShape)restored.Shapes.Single();

        Assert.AreEqual(LineState.Removed, marker.State);
        Assert.AreEqual(new SKRect(20, 24, 140, 46), marker.GetBounds());
        Assert.AreEqual(new SKColor(243, 139, 168), LineStateMarkerShape.StateColor(LineState.Removed));
    }

    [TestMethod]
    public void MarkerClonePreservesStateAndCategory()
    {
        var original = new LineStateMarkerShape
        {
            State = LineState.Focus,
            Category = AnnotationCategory.Question,
            Sloppiness = 0.7f
        };

        var clone = (LineStateMarkerShape)original.Clone();

        Assert.AreEqual(LineState.Focus, clone.State);
        Assert.AreEqual(AnnotationCategory.Question, clone.Category);
        Assert.AreEqual(original.Sloppiness, clone.Sloppiness, 0.0001f);
    }

    [TestMethod]
    public void StatesRenderDifferentVisualSignals()
    {
        var colors = Enum.GetValues<LineState>()
            .Select(RenderStateSignature)
            .ToArray();

        Assert.AreEqual(colors.Length, colors.Distinct().Count());
    }

    private static SKColor RenderStateSignature(LineState state)
    {
        using var bitmap = new SKBitmap(180, 60);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(30, 30, 42));
        var marker = new LineStateMarkerShape { X = 20, Y = 12, Width = 120, Height = 30, State = state };
        marker.Render(canvas, new AnnotationDocument(bitmap));
        return bitmap.GetPixel(25, 30);
    }
}
