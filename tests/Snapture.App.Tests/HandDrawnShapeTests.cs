using SkiaSharp;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class HandDrawnShapeTests
{
    [TestMethod]
    public void SloppinessChangesEveryAnnotationRendererWithoutChangingBounds()
    {
        var factories = new (string Name, Func<Shape> Create)[]
        {
            ("Rectangle", () => new RectangleShape { X = 20, Y = 20, Width = 100, Height = 60, Filled = true }),
            ("SpeechBalloon", () => new SpeechBalloonShape { X = 20, Y = 20, Width = 100, Height = 60 }),
            ("Ruler", () => new RulerShape { X1 = 20, Y1 = 30, X2 = 150, Y2 = 100 }),
            ("Spotlight", () => new SpotlightShape { X = 30, Y = 30, Width = 120, Height = 70 }),
            ("Ellipse", () => new EllipseShape { X = 20, Y = 20, Width = 100, Height = 60, Filled = true }),
            ("Line", () => new LineShape { X1 = 20, Y1 = 30, X2 = 150, Y2 = 100 }),
            ("Arrow", () => new ArrowShape { X1 = 20, Y1 = 30, X2 = 150, Y2 = 100, Style = ArrowStyle.Modern }),
            ("Freehand", () => new FreehandShape { Points = new() { new(20, 30), new(75, 100), new(150, 50) } }),
            ("Text", () => new TextShape { X = 20, Y = 30, Text = "Sketch" }),
            ("Highlight", () => new HighlightShape { X = 20, Y = 20, Width = 100, Height = 60, StrokeColorArgb = 0xFFFFD43B }),
            ("Blur", () => new BlurShape { X = 20, Y = 20, Width = 100, Height = 60 }),
            ("Redact", () => new RedactShape { X = 20, Y = 20, Width = 100, Height = 60 }),
            ("Step", () => new StepShape { X = 80, Y = 80, Radius = 28 })
        };

        foreach (var (name, create) in factories)
        {
            var shape = create();
            var bounds = shape.GetBounds();
            using var clean = Render(shape, sloppiness: 0);
            shape.Sloppiness = 1;
            using var rough = Render(shape, sloppiness: 1);

            Assert.AreEqual(bounds, shape.GetBounds(), $"{name} changed bounds while rendering.");
            Assert.IsTrue(PixelsDiffer(clean, rough), $"{name} does not respond to sloppiness.");
        }
    }

    [TestMethod]
    public void RoundedRectangleAndTextJitterAreDeterministic()
    {
        using var first = RoughStroke.CreateRoundedRectangle(new SKRect(10, 10, 130, 80), 18, 0.75f, 3, 401);
        using var second = RoughStroke.CreateRoundedRectangle(new SKRect(10, 10, 130, 80), 18, 0.75f, 3, 401);

        var firstPoints = ReadPathPoints(first);
        var secondPoints = ReadPathPoints(second);
        CollectionAssert.AreEqual(firstPoints, secondPoints);
        Assert.AreNotEqual(SKPoint.Empty, RoughStroke.GetJitter(0.75f, 3, 719, 6));
        Assert.AreEqual(SKPoint.Empty, RoughStroke.GetJitter(0, 3, 719, 6));
    }

    private static SKBitmap Render(Shape shape, float sloppiness)
    {
        shape.Sloppiness = sloppiness;
        var bitmap = new SKBitmap(220, 160);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(56, 58, 80));
        shape.Render(canvas, new AnnotationDocument(bitmap));
        return bitmap;
    }

    private static bool PixelsDiffer(SKBitmap first, SKBitmap second)
    {
        for (int y = 0; y < first.Height; y++)
        {
            for (int x = 0; x < first.Width; x++)
            {
                if (first.GetPixel(x, y) != second.GetPixel(x, y))
                    return true;
            }
        }

        return false;
    }

    private static SKPoint[] ReadPathPoints(SKPath path)
    {
        var points = new List<SKPoint>();
        using var iterator = path.CreateRawIterator();
        var values = new SKPoint[4];
        SKPathVerb verb;
        while ((verb = iterator.Next(values)) != SKPathVerb.Done)
        {
            if (verb is SKPathVerb.Move or SKPathVerb.Line)
                points.Add(values[0]);
        }

        return points.ToArray();
    }
}
