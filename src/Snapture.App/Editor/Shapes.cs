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
[JsonDerivedType(typeof(SpotlightShape), typeDiscriminator: "spotlight")]
[JsonDerivedType(typeof(RulerShape),    typeDiscriminator: "ruler")]
public abstract class Shape
{
    public uint StrokeColorArgb { get; set; } = 0xFFE74C3C; // red default
    public uint FillColorArgb { get; set; } = 0x00000000;
    public float StrokeThickness { get; set; } = 3f;
    /// <summary>0 is clean geometry; 1 is the loosest hand-drawn stroke.</summary>
    public float Sloppiness { get; set; }
    public bool DropShadow { get; set; }

    public abstract void Render(SKCanvas canvas, AnnotationDocument doc);
    public abstract SKRect GetBounds();
    public abstract bool HitTest(SKPoint point);

    /// <summary>Creates a deep copy of this shape.</summary>
    public abstract Shape Clone();

    /// <summary>Translates the shape by the given pixel offset.</summary>
    public abstract void Offset(float dx, float dy);

    /// <summary>Resizes this shape to fit the given bounds rectangle.</summary>
    public virtual void ResizeTo(SKRect newBounds)
    {
        var old = GetBounds();
        if (old.Width <= 0 || old.Height <= 0) return;
        float sx = newBounds.Width / old.Width;
        float sy = newBounds.Height / old.Height;
        Offset(newBounds.Left - old.Left, newBounds.Top - old.Top);
        ScaleFrom(newBounds.Left, newBounds.Top, sx, sy);
    }

    protected virtual void ScaleFrom(float originX, float originY, float sx, float sy) { }

    protected void ApplyShadowIfNeeded(SKPaint paint)
    {
        if (DropShadow)
            paint.ImageFilter = SKImageFilter.CreateDropShadow(2, 3, 4, 4, new SKColor(0, 0, 0, 100));
    }

    protected SKPaint MakeStrokePaint()
    {
        var p = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = ToColor(StrokeColorArgb),
            StrokeWidth = StrokeThickness,
            IsAntialias = true,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round
        };
        ApplyShadowIfNeeded(p);
        return p;
    }

    protected SKPaint MakeFillPaint()
    {
        var p = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToColor(FillColorArgb),
            IsAntialias = true
        };
        ApplyShadowIfNeeded(p);
        return p;
    }

    protected static SKColor ToColor(uint argb) =>
        new((byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF),
            (byte)((argb >> 24) & 0xFF));
}

public enum ArrowStyle
{
    Classic,
    Modern
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
            if (Sloppiness > 0 && CornerRadius <= 0)
            {
                using var rough = RoughStroke.CreateRectangle(rect, Sloppiness, StrokeThickness, 97);
                canvas.DrawPath(rough, fill);
            }
            else if (CornerRadius > 0) canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, fill);
            else canvas.DrawRect(rect, fill);
        }
        if (!Filled)
        {
            using var stroke = MakeStrokePaint();
            if (Sloppiness > 0 && CornerRadius <= 0)
            {
                using var rough = RoughStroke.CreateRectangle(rect, Sloppiness, StrokeThickness, 101);
                canvas.DrawPath(rough, stroke);
            }
            else if (CornerRadius > 0) canvas.DrawRoundRect(rect, CornerRadius, CornerRadius, stroke);
            else canvas.DrawRect(rect, stroke);
        }
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new RectangleShape
    {
        X = X, Y = Y, Width = Width, Height = Height, CornerRadius = CornerRadius, Filled = Filled,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
}

/// <summary>Drops a measurement line onto the canvas showing pixel distance and angle.</summary>
public sealed class RulerShape : Shape
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }

    public RulerShape() { StrokeColorArgb = 0xFF3498DB; StrokeThickness = 2f; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        using var paint = MakeStrokePaint();
        if (Sloppiness > 0)
        {
            using var rough = RoughStroke.CreateLine(new SKPoint(X1, Y1), new SKPoint(X2, Y2), Sloppiness, StrokeThickness, 211);
            canvas.DrawPath(rough, paint);
        }
        else canvas.DrawLine(X1, Y1, X2, Y2, paint);

        float dx = X2 - X1, dy = Y2 - Y1;
        float length = MathF.Sqrt(dx * dx + dy * dy);
        float angle = MathF.Atan2(dy, dx) * 180f / MathF.PI;
        string label = $"{length:F0}px · {angle:F1}°";

        float mx = (X1 + X2) / 2, my = (Y1 + Y2) / 2;
        using var textPaint = new SKPaint
        {
            Color = ToColor(StrokeColorArgb),
            IsAntialias = true,
            TextSize = 12,
            Typeface = SKTypeface.FromFamilyName("Segoe UI"),
            TextAlign = SKTextAlign.Center
        };
        canvas.DrawText(label, mx, my - 6, textPaint);

        float endLen = 6;
        using var endPaint = MakeStrokePaint();
        float perpX = -dy / length * endLen, perpY = dx / length * endLen;
        if (length > 1)
        {
            canvas.DrawLine(X1 + perpX, Y1 + perpY, X1 - perpX, Y1 - perpY, endPaint);
            canvas.DrawLine(X2 + perpX, Y2 + perpY, X2 - perpX, Y2 - perpY, endPaint);
        }
    }

    public override SKRect GetBounds()
    {
        float l = Math.Min(X1, X2), t = Math.Min(Y1, Y2);
        float r = Math.Max(X1, X2), b = Math.Max(Y1, Y2);
        return new SKRect(l, t, r, b);
    }

    public override bool HitTest(SKPoint p)
    {
        float dx = X2 - X1, dy = Y2 - Y1;
        float len2 = dx * dx + dy * dy;
        if (len2 < 1) return false;
        float t = Math.Clamp(((p.X - X1) * dx + (p.Y - Y1) * dy) / len2, 0, 1);
        float cx = X1 + t * dx, cy = Y1 + t * dy;
        float dist = MathF.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
        return dist <= StrokeThickness + 4;
    }

    public override Shape Clone() => new RulerShape
    {
        X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };

    public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
    public override void ResizeTo(SKRect r)
    {
        var old = GetBounds();
        if (old.Width > 0) { X1 = r.Left + (X1 - old.Left) / old.Width * r.Width; X2 = r.Left + (X2 - old.Left) / old.Width * r.Width; }
        else { X1 = r.MidX; X2 = r.MidX; }
        if (old.Height > 0) { Y1 = r.Top + (Y1 - old.Top) / old.Height * r.Height; Y2 = r.Top + (Y2 - old.Top) / old.Height * r.Height; }
        else { Y1 = r.MidY; Y2 = r.MidY; }
    }
}

/// <summary>Darkens everything outside the rectangle while keeping the inside sharp.</summary>
public sealed class SpotlightShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    public SpotlightShape() { StrokeColorArgb = 0xCC000000; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        var inner = new SKRect(X, Y, X + Width, Y + Height);
        var outer = new SKRect(0, 0, doc.Width, doc.Height);
        using var path = new SKPath();
        path.AddRect(outer);
        path.AddRect(inner);
        path.FillType = SKPathFillType.EvenOdd;
        using var paint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            Color = ToColor(StrokeColorArgb),
            IsAntialias = true
        };
        canvas.DrawPath(path, paint);
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new SpotlightShape
    {
        X = X, Y = Y, Width = Width, Height = Height,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
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
            if (Sloppiness > 0)
            {
                using var rough = RoughStroke.CreateEllipse(rect, Sloppiness, StrokeThickness, 293);
                canvas.DrawPath(rough, fill);
            }
            else canvas.DrawOval(rect, fill);
        }
        else
        {
            using var stroke = MakeStrokePaint();
            if (Sloppiness > 0)
            {
                using var rough = RoughStroke.CreateEllipse(rect, Sloppiness, StrokeThickness, 307);
                canvas.DrawPath(rough, stroke);
            }
            else canvas.DrawOval(rect, stroke);
        }
    }
    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new EllipseShape
    {
        X = X, Y = Y, Width = Width, Height = Height, Filled = Filled,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
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
        if (Sloppiness > 0)
        {
            using var rough = RoughStroke.CreateLine(new SKPoint(X1, Y1), new SKPoint(X2, Y2), Sloppiness, StrokeThickness, 401);
            canvas.DrawPath(rough, stroke);
        }
        else canvas.DrawLine(X1, Y1, X2, Y2, stroke);
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
    public override void ResizeTo(SKRect r)
    {
        var old = GetBounds();
        if (old.Width > 0) { X1 = r.Left + (X1 - old.Left) / old.Width * r.Width; X2 = r.Left + (X2 - old.Left) / old.Width * r.Width; }
        else { X1 = r.MidX; X2 = r.MidX; }
        if (old.Height > 0) { Y1 = r.Top + (Y1 - old.Top) / old.Height * r.Height; Y2 = r.Top + (Y2 - old.Top) / old.Height * r.Height; }
        else { Y1 = r.MidY; Y2 = r.MidY; }
    }
}

public sealed class ArrowShape : Shape
{
    public float X1 { get; set; }
    public float Y1 { get; set; }
    public float X2 { get; set; }
    public float Y2 { get; set; }
    public bool Bidirectional { get; set; }
    public bool Reversed { get; set; }
    public bool Dashed { get; set; }
    /// <summary>Classic filled triangle or modern rounded open chevron.</summary>
    public ArrowStyle Style { get; set; }
    /// <summary>Signed normalized bend amount in the range -1 to 1.</summary>
    public float Curve { get; set; }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        using var stroke = MakeStrokePaint();
        if (Dashed) stroke.PathEffect = SKPathEffect.CreateDash(new[] { 8f, 8f }, 0);
        var start = new SKPoint(X1, Y1);
        var end = new SKPoint(X2, Y2);
        var control = ArrowGeometry.GetControlPoint(start, end, Curve);
        if (Sloppiness > 0)
        {
            var points = Math.Abs(Curve) > 0.0001f
                ? ArrowGeometry.SampleQuadratic(start, control, end)
                : new[] { start, end };
            using var rough = RoughStroke.CreatePolyline(points, Sloppiness, StrokeThickness, 503);
            canvas.DrawPath(rough, stroke);
        }
        else if (Math.Abs(Curve) > 0.0001f)
        {
            using var path = new SKPath();
            path.MoveTo(start);
            path.QuadTo(control, end);
            canvas.DrawPath(path, stroke);
        }
        else canvas.DrawLine(start, end, stroke);

        var startDirection = ArrowGeometry.Normalize(ArrowGeometry.TangentAt(start, control, end, 0));
        var endDirection = ArrowGeometry.Normalize(ArrowGeometry.TangentAt(start, control, end, 1));
        if (Reversed || Bidirectional)
            DrawArrowhead(canvas, stroke, start, new SKPoint(-startDirection.X, -startDirection.Y));
        if (!Reversed || Bidirectional)
            DrawArrowhead(canvas, stroke, end, endDirection);
    }

    private void DrawArrowhead(SKCanvas canvas, SKPaint stroke, SKPoint tip, SKPoint direction)
    {
        direction = ArrowGeometry.Normalize(direction);
        if (direction == SKPoint.Empty) return;

        float headLength = Style == ArrowStyle.Modern
            ? Math.Max(15f, StrokeThickness * 5f)
            : Math.Max(12f, StrokeThickness * 4f);
        var normal = new SKPoint(-direction.Y, direction.X);
        var basePoint = new SKPoint(tip.X - direction.X * headLength, tip.Y - direction.Y * headLength);

        if (Style == ArrowStyle.Modern)
        {
            float halfWidth = Math.Max(5f, StrokeThickness * 2.1f);
            var wing1 = new SKPoint(basePoint.X + normal.X * halfWidth, basePoint.Y + normal.Y * halfWidth);
            var wing2 = new SKPoint(basePoint.X - normal.X * halfWidth, basePoint.Y - normal.Y * halfWidth);
            using var head = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = stroke.Color,
                StrokeWidth = Math.Max(2f, StrokeThickness * 1.35f),
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            ApplyShadowIfNeeded(head);
            using var chevron = new SKPath();
            chevron.MoveTo(wing1);
            chevron.LineTo(tip);
            chevron.LineTo(wing2);
            canvas.DrawPath(chevron, head);
            return;
        }

        float wingWidth = MathF.Tan(MathF.PI / 6) * headLength;
        var classicWing1 = new SKPoint(basePoint.X + normal.X * wingWidth, basePoint.Y + normal.Y * wingWidth);
        var classicWing2 = new SKPoint(basePoint.X - normal.X * wingWidth, basePoint.Y - normal.Y * wingWidth);
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = stroke.Color, IsAntialias = true };
        ApplyShadowIfNeeded(fill);
        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(classicWing1);
        path.LineTo(classicWing2);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    public override SKRect GetBounds() => ArrowGeometry.GetBounds(
        new SKPoint(X1, Y1), new SKPoint(X2, Y2), Curve,
        Math.Max(8f, Math.Max(12f, StrokeThickness * 5f) + StrokeThickness));
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new ArrowShape
    {
        X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2, Bidirectional = Bidirectional, Reversed = Reversed, Dashed = Dashed, Style = Style, Curve = Curve,
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
    public override void ResizeTo(SKRect r)
    {
        var old = GetBounds();
        if (old.Width > 0) { X1 = r.Left + (X1 - old.Left) / old.Width * r.Width; X2 = r.Left + (X2 - old.Left) / old.Width * r.Width; }
        else { X1 = r.MidX; X2 = r.MidX; }
        if (old.Height > 0) { Y1 = r.Top + (Y1 - old.Top) / old.Height * r.Height; Y2 = r.Top + (Y2 - old.Top) / old.Height * r.Height; }
        else { Y1 = r.MidY; Y2 = r.MidY; }
    }
}

public sealed class FreehandShape : Shape
{
    public List<SKPoint> Points { get; set; } = new();

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        if (Points.Count < 2) return;
        using var stroke = MakeStrokePaint();
        using var path = RoughStroke.CreatePolyline(Points, Sloppiness, StrokeThickness, 607);
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy)
    {
        for (int i = 0; i < Points.Count; i++)
            Points[i] = new SKPoint(Points[i].X + dx, Points[i].Y + dy);
    }
    public override void ResizeTo(SKRect r)
    {
        var old = GetBounds();
        if (old.Width <= 0 || old.Height <= 0 || Points.Count == 0) return;
        for (int i = 0; i < Points.Count; i++)
        {
            float nx = r.Left + (Points[i].X - old.Left) / old.Width * r.Width;
            float ny = r.Top + (Points[i].Y - old.Top) / old.Height * r.Height;
            Points[i] = new SKPoint(nx, ny);
        }
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r) { X = r.Left; Y = r.Top; Width = r.Width; Height = r.Height; }
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
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness, Sloppiness = Sloppiness, DropShadow = DropShadow
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
}
