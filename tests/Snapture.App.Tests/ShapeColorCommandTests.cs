using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ShapeColorCommandTests
{
    [TestMethod]
    public void SetShapeColorCommandIsUndoableForMultipleShapes()
    {
        using var background = new SKBitmap(32, 32);
        var document = new AnnotationDocument(background);
        var first = new RectangleShape { StrokeColorArgb = 0xFFFF0000 };
        var second = new EllipseShape { StrokeColorArgb = 0xFF00FF00 };
        document.Shapes.Add(first);
        document.Shapes.Add(second);
        var command = new SetShapeColorCommand(new Shape[] { first, second }, 0xFF336699);
        var stack = new CommandStack();

        stack.Do(document, command);
        Assert.AreEqual(0xFF336699u, first.StrokeColorArgb);
        Assert.AreEqual(0xFF336699u, second.StrokeColorArgb);

        stack.Undo(document);
        Assert.AreEqual(0xFFFF0000u, first.StrokeColorArgb);
        Assert.AreEqual(0xFF00FF00u, second.StrokeColorArgb);

        stack.Redo(document);
        Assert.AreEqual(0xFF336699u, first.StrokeColorArgb);
        Assert.AreEqual(0xFF336699u, second.StrokeColorArgb);
    }
}
