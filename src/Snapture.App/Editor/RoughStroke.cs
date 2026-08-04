using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>
/// Deterministic, lightweight path jitter for an Excalidraw-style hand-drawn finish.
/// The geometry is regenerated on paint, so the project format stores only the user's
/// sloppiness amount and never a process-specific random stream.
/// </summary>
internal static class RoughStroke
{
    public static SKPath CreateLine(SKPoint start, SKPoint end, float sloppiness, float strokeThickness, int seed)
        => CreatePolyline(new[] { start, end }, sloppiness, strokeThickness, seed);

    public static SKPath CreateRectangle(SKRect rect, float sloppiness, float strokeThickness, int seed)
        => CreatePolyline(
            new[]
            {
                new SKPoint(rect.Left, rect.Top),
                new SKPoint(rect.Right, rect.Top),
                new SKPoint(rect.Right, rect.Bottom),
                new SKPoint(rect.Left, rect.Bottom)
            }, sloppiness, strokeThickness, seed, closed: true);

    public static SKPath CreateRoundedRectangle(SKRect rect, float radius, float sloppiness,
        float strokeThickness, int seed)
    {
        float left = Math.Min(rect.Left, rect.Right);
        float top = Math.Min(rect.Top, rect.Bottom);
        float right = Math.Max(rect.Left, rect.Right);
        float bottom = Math.Max(rect.Top, rect.Bottom);
        float clampedRadius = Math.Clamp(radius, 0, Math.Min(right - left, bottom - top) / 2f);
        if (clampedRadius <= 0)
            return CreateRectangle(new SKRect(left, top, right, bottom), sloppiness, strokeThickness, seed);

        const int arcSamples = 6;
        var points = new List<SKPoint>((arcSamples * 4) + 4)
        {
            new(left + clampedRadius, top),
            new(right - clampedRadius, top)
        };
        AddArc(points, new SKPoint(right - clampedRadius, top + clampedRadius), clampedRadius, -90, 0, arcSamples);
        points.Add(new(right, bottom - clampedRadius));
        AddArc(points, new SKPoint(right - clampedRadius, bottom - clampedRadius), clampedRadius, 0, 90, arcSamples);
        points.Add(new(left + clampedRadius, bottom));
        AddArc(points, new SKPoint(left + clampedRadius, bottom - clampedRadius), clampedRadius, 90, 180, arcSamples);
        points.Add(new(left, top + clampedRadius));
        AddArc(points, new SKPoint(left + clampedRadius, top + clampedRadius), clampedRadius, 180, 270, arcSamples);
        return CreatePolyline(points, sloppiness, strokeThickness, seed, closed: true);
    }

    public static SKPath CreateEllipse(SKRect rect, float sloppiness, float strokeThickness, int seed)
    {
        const int samples = 48;
        var points = new SKPoint[samples];
        float cx = rect.MidX;
        float cy = rect.MidY;
        float rx = Math.Abs(rect.Width) / 2f;
        float ry = Math.Abs(rect.Height) / 2f;
        for (int i = 0; i < samples; i++)
        {
            double angle = i * Math.PI * 2 / samples;
            points[i] = new SKPoint(
                cx + (float)Math.Cos(angle) * rx,
                cy + (float)Math.Sin(angle) * ry);
        }
        return CreatePolyline(points, sloppiness, strokeThickness, seed, closed: true);
    }

    public static SKPath CreatePolyline(IReadOnlyList<SKPoint> points, float sloppiness,
        float strokeThickness, int seed, bool closed = false)
    {
        var path = new SKPath();
        var generated = GeneratePolylinePoints(points, sloppiness, strokeThickness, seed, closed);
        if (generated.Count == 0) return path;

        path.MoveTo(generated[0]);
        for (int i = 1; i < generated.Count; i++)
            path.LineTo(generated[i]);
        if (closed) path.Close();
        return path;
    }

    internal static IReadOnlyList<SKPoint> GeneratePolylinePoints(IReadOnlyList<SKPoint> points,
        float sloppiness, float strokeThickness, int seed, bool closed = false)
    {
        if (points.Count == 0) return Array.Empty<SKPoint>();
        if (points.Count == 1) return new[] { points[0] };

        sloppiness = Math.Clamp(sloppiness, 0f, 1f);
        if (sloppiness <= 0)
            return points.ToArray();

        float amplitude = sloppiness * (2f + Math.Clamp(strokeThickness, 1f, 24f) * 1.25f);
        var result = new List<SKPoint>(points.Count * 3);
        result.Add(points[0]);

        int segmentCount = closed ? points.Count : points.Count - 1;
        for (int segment = 0; segment < segmentCount; segment++)
        {
            var start = points[segment];
            var end = points[(segment + 1) % points.Count];
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float length = MathF.Sqrt((dx * dx) + (dy * dy));
            int samples = Math.Max(2, (int)MathF.Ceiling(length / 18f));

            for (int sample = 1; sample <= samples; sample++)
            {
                float t = sample / (float)samples;
                float x = start.X + dx * t;
                float y = start.Y + dy * t;
                bool isCorner = sample == samples;
                if (!isCorner && length > 0.01f)
                {
                    float normalX = -dy / length;
                    float normalY = dx / length;
                    float noise = Noise(seed, segment * 97 + sample * 13);
                    x += normalX * noise * amplitude;
                    y += normalY * noise * amplitude;
                }
                if (!closed || !(segment == segmentCount - 1 && isCorner))
                    result.Add(new SKPoint(x, y));
            }
        }

        return result;
    }

    internal static SKPoint GetJitter(float sloppiness, float magnitude, int seed, int index)
    {
        float amount = Math.Clamp(sloppiness, 0f, 1f) * Math.Max(0, magnitude);
        return new SKPoint(Noise(seed, index) * amount, Noise(seed + 41, index + 17) * amount);
    }

    private static void AddArc(List<SKPoint> points, SKPoint center, float radius,
        float startDegrees, float endDegrees, int samples)
    {
        for (int i = 1; i <= samples; i++)
        {
            float degrees = startDegrees + ((endDegrees - startDegrees) * i / samples);
            float radians = degrees * MathF.PI / 180f;
            points.Add(new SKPoint(
                center.X + MathF.Cos(radians) * radius,
                center.Y + MathF.Sin(radians) * radius));
        }
    }

    private static float Noise(int seed, int index)
    {
        double value = Math.Sin((seed * 12.9898) + (index * 78.233)) * 43758.5453;
        return (float)((value - Math.Floor(value)) * 2.0 - 1.0);
    }
}
