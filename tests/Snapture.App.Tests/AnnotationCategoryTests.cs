using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class AnnotationCategoryTests
{
    [TestMethod]
    public void CategoryCommandIsUndoableForMultipleShapes()
    {
        using var background = new SKBitmap(80, 60);
        var document = new AnnotationDocument(background);
        var first = new RectangleShape { Width = 20, Height = 20, Category = AnnotationCategory.Question };
        var second = new EllipseShape { Width = 20, Height = 20 };
        document.Shapes.Add(first);
        document.Shapes.Add(second);
        var stack = new CommandStack();

        stack.Do(document, new SetShapeCategoryCommand(new Shape[] { first, second }, AnnotationCategory.Blocker));
        Assert.AreEqual(AnnotationCategory.Blocker, first.Category);
        Assert.AreEqual(AnnotationCategory.Blocker, second.Category);

        stack.Undo(document);
        Assert.AreEqual(AnnotationCategory.Question, first.Category);
        Assert.AreEqual(AnnotationCategory.None, second.Category);

        stack.Redo(document);
        Assert.AreEqual(AnnotationCategory.Blocker, first.Category);
        Assert.AreEqual(AnnotationCategory.Blocker, second.Category);
    }

    [TestMethod]
    public void CategoryTagsRoundTripAndRenderWithTheirColor()
    {
        using var background = new SKBitmap(120, 90);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(new RectangleShape
        {
            X = 20,
            Y = 20,
            Width = 70,
            Height = 40,
            Category = AnnotationCategory.Nit
        });

        var restored = new AnnotationDocument(new SKBitmap(120, 90));
        restored.DeserializeShapes(document.SerializeShapes());
        using var canvas = new SKCanvas(restored.Background);
        restored.Render(canvas, flattenForExport: true);

        var restoredShape = restored.Shapes.Single();
        Assert.AreEqual(AnnotationCategory.Nit, restoredShape.Category);
        Assert.AreEqual(new SKColor(137, 180, 250), Shape.CategoryColor(AnnotationCategory.Nit));
        Assert.AreNotEqual(SKColors.Transparent, restored.Background.GetPixel(82, 28));
    }

    [TestMethod]
    public void ClonePreservesCategory()
    {
        var original = new TextShape { Text = "Needs review", Category = AnnotationCategory.Question };

        var clone = original.Clone();

        Assert.AreEqual(AnnotationCategory.Question, clone.Category);
    }
}
