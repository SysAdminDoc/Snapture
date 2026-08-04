using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>Shared quadratic-arrow geometry used by rendering and hit testing.</summary>
internal static class ArrowGeometry
{
    private const float MaxCurveOffsetFraction = 0.45f;

    public static SKPoint GetControlPoint(SKPoint start, SKPoint end, float curve)
    {
        var midpoint = new SKPoint((start.X + end.X) / 2f, (start.Y + end.Y) / 2f);
        float dx = end.X - start.X;
        float dy = end.Y - start.Y;
        float length = MathF.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.001f)
            return midpoint;

        if (!float.IsFinite(curve))
            curve = 0;
        curve = Math.Clamp(curve, -1f, 1f);
        float offset = curve * length * MaxCurveOffsetFraction;
        return new SKPoint(midpoint.X - dy / length * offset, midpoint.Y + dx / length * offset);
    }

    public static SKPoint PointOnQuadratic(SKPoint start, SKPoint control, SKPoint end, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float inverse = 1f - t;
        return new SKPoint(
            inverse * inverse * start.X + 2f * inverse * t * control.X + t * t * end.X,
            inverse * inverse * start.Y + 2f * inverse * t * control.Y + t * t * end.Y);
    }

    public static SKPoint TangentAt(SKPoint start, SKPoint control, SKPoint end, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        float inverse = 1f - t;
        return new SKPoint(
            2f * (inverse * (control.X - start.X) + t * (end.X - control.X)),
            2f * (inverse * (control.Y - start.Y) + t * (end.Y - control.Y)));
    }

    public static IReadOnlyList<SKPoint> SampleQuadratic(SKPoint start, SKPoint control, SKPoint end, int segments = 24)
    {
        segments = Math.Max(2, segments);
        var points = new SKPoint[segments + 1];
        for (int i = 0; i <= segments; i++)
            points[i] = PointOnQuadratic(start, control, end, i / (float)segments);
        return points;
    }

    public static SKRect GetBounds(SKPoint start, SKPoint end, float curve, float inflate)
    {
        var control = GetControlPoint(start, end, curve);
        var points = SampleQuadratic(start, control, end);
        float minX = points.Min(point => point.X);
        float minY = points.Min(point => point.Y);
        float maxX = points.Max(point => point.X);
        float maxY = points.Max(point => point.Y);
        float padding = Math.Max(0, inflate);
        return new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
    }

    public static SKPoint Normalize(SKPoint vector)
    {
        float length = MathF.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return length <= 0.001f ? SKPoint.Empty : new SKPoint(vector.X / length, vector.Y / length);
    }
}
