using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>Editable vector rendering of a pasted Mermaid or PlantUML diagram.</summary>
public sealed class DiagramShape : Shape
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public DiagramMarkupKind DiagramKind { get; set; }
    public string Markup { get; set; } = "";
    public List<DiagramNode> Nodes { get; set; } = new();
    public List<DiagramEdge> Edges { get; set; } = new();

    public static DiagramShape FromDefinition(DiagramDefinition definition, string markup, float x, float y)
    {
        return new DiagramShape
        {
            X = x,
            Y = y,
            Width = definition.Width,
            Height = definition.Height,
            DiagramKind = definition.Kind,
            Markup = markup,
            Nodes = definition.Nodes.Select(CloneNode).ToList(),
            Edges = definition.Edges.Select(CloneEdge).ToList()
        };
    }

    public override void Render(SKCanvas canvas, AnnotationDocument doc)
    {
        if (Nodes.Count == 0 || Width <= 0 || Height <= 0) return;
        canvas.Save();
        canvas.Translate(X, Y);

        var nodes = Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        using var edgePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(166, 173, 200, 235),
            StrokeWidth = Math.Max(1.5f, StrokeThickness),
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true
        };
        ApplyShadowIfNeeded(edgePaint);
        foreach (var edge in Edges)
        {
            if (!nodes.TryGetValue(edge.From, out var from) || !nodes.TryGetValue(edge.To, out var to)) continue;
            var start = from.X + from.Width <= to.X ? new SKPoint(from.X + from.Width, from.Y + from.Height / 2) : new SKPoint(from.X, from.Y + from.Height / 2);
            var end = from.X + from.Width <= to.X ? new SKPoint(to.X, to.Y + to.Height / 2) : new SKPoint(to.X + to.Width, to.Y + to.Height / 2);
            DrawLine(canvas, edgePaint, start, end, 911);
            DrawArrowhead(canvas, end, start, edgePaint.Color);
            if (!string.IsNullOrWhiteSpace(edge.Label))
            {
                using var labelPaint = MakeTextPaint(12, new SKColor(198, 198, 210));
                canvas.DrawText(edge.Label, (start.X + end.X) / 2, (start.Y + end.Y) / 2 - 6, labelPaint);
            }
        }

        foreach (var node in Nodes)
            DrawNode(canvas, node);
        canvas.Restore();
    }

    private void DrawNode(SKCanvas canvas, DiagramNode node)
    {
        var rect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + node.Height);
        var fillColor = DiagramKind == DiagramMarkupKind.PlantUml
            ? new SKColor(58, 64, 86, 245)
            : new SKColor(49, 52, 72, 245);
        using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = fillColor, IsAntialias = true };
        using var stroke = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(203, 166, 247, 230),
            StrokeWidth = Math.Max(1.5f, StrokeThickness),
            IsAntialias = true,
            StrokeJoin = SKStrokeJoin.Round
        };
        ApplyShadowIfNeeded(fill);
        ApplyShadowIfNeeded(stroke);
        if (Sloppiness > 0)
        {
            using var rough = RoughStroke.CreateRoundedRectangle(rect, 8, Sloppiness, StrokeThickness, 919);
            canvas.DrawPath(rough, fill);
            canvas.DrawPath(rough, stroke);
        }
        else
        {
            canvas.DrawRoundRect(rect, 8, 8, fill);
            canvas.DrawRoundRect(rect, 8, 8, stroke);
        }

        using var text = MakeTextPaint(14, new SKColor(239, 241, 245));
        string label = FitLabel(node.Label, text, node.Width - 22);
        canvas.DrawText(label, node.X + node.Width / 2, node.Y + node.Height / 2 + 5, text);
    }

    private void DrawLine(SKCanvas canvas, SKPaint paint, SKPoint start, SKPoint end, int seed)
    {
        if (Sloppiness > 0)
        {
            using var rough = RoughStroke.CreateLine(start, end, Sloppiness, StrokeThickness, seed);
            canvas.DrawPath(rough, paint);
        }
        else
        {
            canvas.DrawLine(start, end, paint);
        }
    }

    private static void DrawArrowhead(SKCanvas canvas, SKPoint tip, SKPoint from, SKColor color)
    {
        var direction = ArrowGeometry.Normalize(new SKPoint(tip.X - from.X, tip.Y - from.Y));
        if (direction == SKPoint.Empty) return;
        var normal = new SKPoint(-direction.Y, direction.X);
        var basePoint = new SKPoint(tip.X - direction.X * 10, tip.Y - direction.Y * 10);
        using var head = new SKPaint { Style = SKPaintStyle.Stroke, Color = color, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        using var path = new SKPath();
        path.MoveTo(basePoint.X + normal.X * 4, basePoint.Y + normal.Y * 4);
        path.LineTo(tip);
        path.LineTo(basePoint.X - normal.X * 4, basePoint.Y - normal.Y * 4);
        canvas.DrawPath(path, head);
    }

    private static SKPaint MakeTextPaint(float size, SKColor color) => new()
    {
        Color = color,
        TextSize = size,
        TextAlign = SKTextAlign.Center,
        Typeface = SKTypeface.FromFamilyName("Cascadia Code, Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright),
        IsAntialias = true,
        SubpixelText = true
    };

    private static string FitLabel(string label, SKPaint paint, float width)
    {
        if (paint.MeasureText(label) <= width) return label;
        const string suffix = "…";
        while (label.Length > 1 && paint.MeasureText(label + suffix) > width)
            label = label[..^1];
        return label + suffix;
    }

    public override SKRect GetBounds() => new(X, Y, X + Width, Y + Height);
    public override bool HitTest(SKPoint p) => GetBounds().Contains(p);
    public override Shape Clone() => new DiagramShape
    {
        X = X, Y = Y, Width = Width, Height = Height, DiagramKind = DiagramKind, Markup = Markup,
        Nodes = Nodes.Select(CloneNode).ToList(), Edges = Edges.Select(CloneEdge).ToList(),
        StrokeColorArgb = StrokeColorArgb, FillColorArgb = FillColorArgb, StrokeThickness = StrokeThickness,
        Sloppiness = Sloppiness, DropShadow = DropShadow, Category = Category
    };
    public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    public override void ResizeTo(SKRect r)
    {
        if (Width <= 0 || Height <= 0) return;
        float sx = r.Width / Width;
        float sy = r.Height / Height;
        foreach (var node in Nodes)
        {
            node.X *= sx;
            node.Y *= sy;
            node.Width *= sx;
            node.Height *= sy;
        }
        X = r.Left;
        Y = r.Top;
        Width = r.Width;
        Height = r.Height;
    }

    private static DiagramNode CloneNode(DiagramNode node) => new()
    {
        Id = node.Id, Label = node.Label, X = node.X, Y = node.Y, Width = node.Width, Height = node.Height
    };

    private static DiagramEdge CloneEdge(DiagramEdge edge) => new()
    {
        From = edge.From, To = edge.To, Label = edge.Label
    };
}
