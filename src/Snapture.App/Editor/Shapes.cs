using System.Text.Json.Serialization;
using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>
/// One annotation shape. Concrete subtypes are JSON-polymorphic via the discriminator below
/// so the .snapture project file round-trips cleanly.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RectangleShape),  typeDiscriminator: "rect")]
[JsonDerivedType(typeof(EllipseShape),    typeDiscriminator: "ellipse")]
[JsonDerivedType(typeof(LineShape),       typeDiscriminator: "line")]
[JsonDerivedType(typeof(ArrowShape),      typeDiscriminator: "arrow")]
[JsonDerivedType(typeof(FreehandShape),   typeDiscriminator: "freehand")]
[JsonDerivedType(typeof(TextShape),       typeDiscriminator: "text")]
[JsonDerivedType(typeof(HighlightShape),  typeDiscriminator: "highlight")]
[JsonDerivedType(typeof(BlurShape),       typeDiscriminator: "blur")]
[JsonDerivedType(typeof(RedactShape),     typeDiscriminator: "redact")]
[JsonDerivedType(typeof(StepShape),       typeDiscriminator: "step")]
public abstract class Shape
{
    public uint StrokeColorArgb { get; set; } = 0xFFE74C3C; // red default
    public uint FillColorArgb { get; set; } = 0x00000000;
    public float StrokeThickness { get; set; } = 3f;

    public abstract void Render(SKCanvas canvas, AnnotationDocument doc);
    public abstract SKRect GetBounds();
    public abstract bool HitTest(SKPoint point);

    /// <summary>Creates a deep copy of this shape.</summary>
    public abstract Shape Clone();

    /// <summary>Translates the shape by the given pixel offset.</summary>
    public abstract void Offset(float dx, float dy);

    protected SKPaint MakeStrokePaint() => new()
    {
        Style = SKPaintStyle.Stroke,
        Color = ToColor(StrokeColorArgb),
        StrokeWidth = StrokeThickness,
        IsAntialias = true,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    protected SKPaint MakeFillPaint() => new()
    {
        Style = SKPaintStyle.Fill,
        Color = ToColor(FillColorArgb),
        IsAntialias = true
    };

    protected static SKColor ToColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
}

public sealed class RectangleShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float CornerRadius { get; set; }
    public bool Filled { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var rect = new SKRect(X, Y, X + Width, Y + Height);
        if (Filled || (FillColorArgb >> 24) != 0)
        {
            using var fill = MakeFillPaint();
            if (Filled) fill.Color = ToColor(StrokeColorArgb);
            if (CornerRadius > 0) canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fill);
            else canvas.DrawRect(rect, fill);
        }
        if (!Filled)
        {
            using var stroke = MakeStrokePaint();
            if (CornerRadius > 0) canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, stroke);
            else canvas.DrawRect(rect, stroke);
        }
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new RectangleShape
    {
        X = X, Y = Y, Width = Width, Height = Height, CornerRadius = CornerRadius, Filled = Filled,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

public sealed class EllipseShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public bool Filled { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var rect = new SKRect(X, Y, X + Width, Y + Height);
        if (Filled)
        {
            using var fill = MakeFillPaint();
            fill.Color = ToColor(StrokeColorArgb);
            canvas.DrawOval(rect, fill);
        }
        else
        {
            using var stroke = MakeStrokePaint();
            canvas.DrawOval(rect, stroke);
        }
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new EllipseShape
    {
        X = X, Y = Y, Width = Width, Height = Height, Filled = Filled,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

public sealed class LineShape : Shape
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public bool Dashed { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        using var stroke = MakeStrokePaint();
        if (Dashed) stroke.PathEffect = SKPathEffect.CreateDash(new[] { 8f, 8f }, 0);
        canvas.DrawLine(X1, Y1, X2, Y2, stroke);
    }
    public override SKRect GetBounds() => SKRect.Create(
        Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Abs(X2 - X1), Math.Abs(Y2 - Y1));
    public override bool HitTest(SKPoint p)
    {
        var bounds = GetBounds();
        bounds.Inflate(StrokeThickness * 2, StrokeThickness * 2);
        return bounds.Contains(p);
    }
    public override Shape Clone() => new LineShape
    {
        X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2, Dashed = Dashed,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
}

public sealed class ArrowShape : Shape
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public bool Bidirectional { get; set; }
    public bool Dashed { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        using var stroke = MakeStrokePaint();
        if (Dashed) stroke.PathEffect = SKPathEffect.CreateDash(new[] { 8f, 8f }, 0);
        canvas.DrawLine(X1, Y1, X2, Y2, stroke);
        DrawArrowhead(canvas, stroke, X1, Y1, X2, Y2);
        if (Bidirectional) DrawArrowhead(canvas, stroke, X2, Y2, X1, Y1);
    }

    private void DrawArrowhead(SKCanvas canvas, SKPaint stroke, float fromX, float fromY, float tipX, float tipY)
    {
        float dx = tipX - fromX, dy = tipY - fromY;
        float angle = MathF.Atan2(dy, dx);
        float headLen = Math.Max(12f, StrokeThickness * 4);
        float wingAngle = MathF.PI / 6;
        float wx1 = tipX - headLen * MathF.Cos(angle - wingAngle);
        float wy1 = tipY - headLen * MathF.Sin(angle - wingAngle);
        float wx2 = tipX - headLen * MathF.Cos(angle + wingAngle);
        float wy2 = tipY - headLen * MathF.Sin(angle + wingAngle);
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = stroke.Color, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(tipX, tipY);
        path.LineTo(wx1, wy1);
        path.LineTo(wx2, wy2);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    public override SKRect GetBounds() => SKRect.Create(
        Math.Min(X1, X2) - 8, Math.Min(Y1, Y2) - 8,
        Math.Abs(X2 - X1) + 16, Math.Abs(Y2 - Y1) + 16);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new ArrowShape
    {
        X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2, Bidirectional = Bidirectional, Dashed = Dashed,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
}

public sealed class FreehandShape : Shape
{
    public List<SKPoint> Points { get; set; } = new();

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        if (Points.Count < 2) return;
        using var stroke = MakeStrokePaint();
        using var path = new SKPath();
        path.MoveTo(Points[0]);
        for (int i = 1; i < Points.Count; i++) path.LineTo(Points[i]);
        canvas.DrawPath(path, stroke);
    }

    public override SKRect GetBounds()
    {
        if (Points.Count == 0) return SKRect.Empty;
        float minX = Points[0].X, minY = Points[0].Y, maxX = minX, maxY = minY;
        foreach (var pt in Points)
        {
            if (pt.X < minX) minX = pt.X; if (pt.X > maxX) maxX = pt.X;
            if (pt.Y < minY) minY = pt.Y; if (pt.Y > maxY) maxY = pt.Y;
        }
        return new SKRect(minX, minY, maxX, maxY);
    }
    public override bool HitTest(SKPoint p)
    {
        var b = GetBounds(); b.Inflate(StrokeThickness * 2, StrokeThickness * 2);
        return b.Contains(p);
    }
    public override Shape Clone() => new FreehandShape
    {
        Points = new List<SKPoint>(Points),
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new SKPoint(Points[i].X + dx, Points[i].Y + dy);
    }
}

public sealed class TextShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public string Text { get; set; } = "";
    public float FontSize { get; set; } = 22f;
    public string FontFamily { get; set; } = "Segoe UI";
    public bool Bold { get; set; }
    public bool Italic { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        if (string.IsNullOrEmpty(Text)) return;
        var weight = Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;
        using var typeface = SKTypeface.FromFamilyName(FontFamily, weight, SKFontStyleWidth.Normal, slant);
        using var paint = new SKPaint
        {
            Color = ToColor(StrokeColorArgb),
            IsAntialias = true,
            TextSize = FontSize,
            Typeface = typeface,
            TextAlign = SKTextAlign.Left,
            SubpixelText = true
        };
        canvas.DrawText(Text, X, Y + FontSize, paint);
    }

    public override SKRect GetBounds()
    {
        // Rough estimate; precise measurement requires SKFont.MeasureText which we keep cheap.
        return new SKRect(X, Y, X + Math.Max(40, Text.Length * FontSize * 0.6f), Y + FontSize * 1.4f);
    }
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new TextShape
    {
        X = X, Y = Y, Text = Text, FontSize = FontSize, FontFamily = FontFamily, Bold = Bold, Italic = Italic,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

public sealed class HighlightShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var rect = new SKRect(X, Y, X + Width, Y + Height);
        var c = ToColor(StrokeColorArgb);
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = new SKColor(c.Red, c.Green, c.Blue, 0x66),
            IsAntialias = true,
            BlendMode = SKBlendMode.Multiply
        };
        canvas.DrawRect(rect, paint);
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new HighlightShape
    {
        X = X, Y = Y, Width = Width, Height = Height,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

public sealed class BlurShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float BlurRadius { get; set; } = 12f;
    /// <summary>If true, do mosaic/pixelate instead of Gaussian blur.</summary>
    public bool Pixelate { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var rect = new SKRect(X, Y, X + Width, Y + Height);
        var rectI = SKRectI.Round(rect);
        rectI.Intersect(new SKRectI(0, 0, doc.Width, doc.Height));
        if (rectI.Width <= 0 || rectI.Height <= 0) return;

        // Snapshot the underlying region from the background bitmap.
        using var snap = new SKBitmap(rectI.Width, rectI.Height);
        using (var src = new SKCanvas(snap))
        {
            src.DrawBitmap(doc.Background, new SKRect(rectI.Left, rectI.Top, rectI.Right, rectI.Bottom),
                           new SKRect(0, 0, rectI.Width, rectI.Height));
        }

        using var paint = new SKPaint { IsAntialias = false };
        if (Pixelate)
        {
            int block = Math.Max(6, (int)BlurRadius);
            int sw = Math.Max(1, snap.Width / block);
            int sh = Math.Max(1, snap.Height / block);
            using var small = snap.Resize(new SKImageInfo(sw, sh), SKFilterQuality.None);
            paint.FilterQuality = SKFilterQuality.None;
            canvas.DrawBitmap(small,
                new SKRect(0, 0, sw, sh),
                new SKRect(rectI.Left, rectI.Top, rectI.Right, rectI.Bottom),
                paint);
        }
        else
        {
            paint.ImageFilter = SKImageFilter.CreateBlur(BlurRadius, BlurRadius);
            canvas.DrawBitmap(snap,
                new SKRect(0, 0, snap.Width, snap.Height),
                new SKRect(rectI.Left, rectI.Top, rectI.Right, rectI.Bottom),
                paint);
        }
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new BlurShape
    {
        X = X, Y = Y, Width = Width, Height = Height, BlurRadius = BlurRadius, Pixelate = Pixelate,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

/// <summary>Solid-fill redaction. The only safe way to hide secrets — blur is reversible.</summary>
public sealed class RedactShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public RedactShape() { StrokeColorArgb = 0xFF111111; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var rect = new SKRect(X, Y, X + Width, Y + Height);
        using var paint = new SKPaint { Style = SKPaintStyle.Fill, Color = ToColor(StrokeColorArgb), IsAntialias = false };
        canvas.DrawRect(rect, paint);
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new RedactShape
    {
        X = X, Y = Y, Width = Width, Height = Height,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}

public sealed class StepShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Radius { get; set; } = 16f;
    public string Label { get; set; } = "1";

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = ToColor(StrokeColorArgb), IsAntialias = true };
        using var border = new SKPaint { Style = SKPaintStyle.Stroke, Color = SKColors.White, StrokeWidth = 2.5f, IsAntialias = true };
        canvas.DrawCircle(X, Y, Radius, fill);
        canvas.DrawCircle(X, Y, Radius, border);
        using var typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        using var text = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = Radius * 1.1f,
            Typeface = typeface,
            TextAlign = SKTextAlign.Center
        };
        canvas.DrawText(Label, X, Y + Radius * 0.4f, text);
    }
    public override SKRect GetBounds() => new(X - Radius, Y - Radius, X + Radius, Y + Radius);
    public override bool HitTest(SKPoint p)
    {
        float dx = p.X - X, dy = p.Y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }
    public override Shape Clone() => new StepShape
    {
        X = X, Y = Y, Radius = Radius, Label = Label,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}
