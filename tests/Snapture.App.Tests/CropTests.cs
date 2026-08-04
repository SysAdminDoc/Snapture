using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CropTests
{
    [TestMethod]
    public void NormalizeAndSnapSnapsNearImageEdges()
    {
        var snapped = CropMath.NormalizeAndSnap(new SKRect(5.2f, 6.1f, 94.4f, 93.7f), 100, 100, true);
        var unsnapped = CropMath.NormalizeAndSnap(new SKRect(5.2f, 6.1f, 94.4f, 93.7f), 100, 100, false);

        Assert.AreEqual(new SKRectI(0, 0, 100, 100), snapped);
        Assert.AreEqual(new SKRectI(5, 6, 95, 94), unsnapped);
    }

    [TestMethod]
    public void NormalizeAndSnapHandlesReversedAndOutOfBoundsSelections()
    {
        var crop = CropMath.NormalizeAndSnap(new SKRect(120, 70, -8, -4), 100, 80, false);

        Assert.AreEqual(new SKRectI(0, 0, 100, 70), crop);
    }

    [TestMethod]
    public void CropCommandTransformsBackgroundAndRemovesOutsideShapes()
    {
        using var background = new SKBitmap(100, 80);
        var document = new AnnotationDocument(background);
        var inside = new RectangleShape
        {
            X = 20,
            Y = 25,
            Width = 10,
            Height = 10,
            Category = AnnotationCategory.Question
        };
        var outside = new EllipseShape { X = 80, Y = 65, Width = 10, Height = 10 };
        document.Shapes.Add(inside);
        document.Shapes.Add(outside);
        var stack = new CommandStack();

        stack.Do(document, new CropDocumentCommand(document, new SKRectI(10, 20, 60, 50)));

        Assert.AreEqual(50, document.Width);
        Assert.AreEqual(30, document.Height);
        Assert.HasCount(1, document.Shapes);
        Assert.AreEqual(new SKRect(10, 5, 20, 15), document.Shapes.Single().GetBounds());
        Assert.AreEqual(AnnotationCategory.Question, document.Shapes.Single().Category);

        stack.Undo(document);

        Assert.AreEqual(100, document.Width);
        Assert.AreEqual(80, document.Height);
        Assert.HasCount(2, document.Shapes);
        Assert.AreEqual(new SKRect(20, 25, 30, 35), document.Shapes[0].GetBounds());

        stack.Redo(document);

        Assert.AreEqual(50, document.Width);
        Assert.AreEqual(30, document.Height);
        Assert.HasCount(1, document.Shapes);
    }
}
