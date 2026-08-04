using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class TextOrientationTests
{
    [TestMethod]
    public void VerticalTextSwapsTheAnnotationBounds()
    {
        var horizontal = new TextShape { Text = "ABCD", FontSize = 20, Orientation = TextOrientation.Horizontal };
        var vertical = new TextShape { Text = "ABCD", FontSize = 20, Orientation = TextOrientation.Vertical };

        var horizontalBounds = horizontal.GetBounds();
        var verticalBounds = vertical.GetBounds();

        Assert.AreEqual(48f, horizontalBounds.Width, 0.001f);
        Assert.AreEqual(28f, verticalBounds.Width, 0.001f);
        Assert.AreEqual(horizontalBounds.Width, verticalBounds.Height, 0.001f);
    }

    [TestMethod]
    public void TextClonePreservesOrientation()
    {
        var original = new TextShape { Text = "Vertical", Orientation = TextOrientation.Vertical };

        var clone = (TextShape)original.Clone();

        Assert.AreEqual(TextOrientation.Vertical, clone.Orientation);
    }

    [TestMethod]
    public void ProjectSerializationRoundTripsVerticalText()
    {
        using var background = new SKBitmap(24, 24);
        var document = new AnnotationDocument(background);
        document.Shapes.Add(new TextShape { Text = "Build", Orientation = TextOrientation.Vertical });

        using var restoredBackground = new SKBitmap(24, 24);
        var restored = new AnnotationDocument(restoredBackground);
        restored.DeserializeShapes(document.SerializeShapes());

        var text = (TextShape)restored.Shapes.Single();
        Assert.AreEqual(TextOrientation.Vertical, text.Orientation);
    }
}
