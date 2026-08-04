using System.Drawing;

namespace Snapture.App.Services;

public enum EdgeDirection
{
    Left,
    Right,
    Top,
    Bottom
}

public sealed record EdgeRulerOptions(
    int MaxDistance = 2_000,
    int SampleSpan = 8,
    double Threshold = 24)
{
    public void Validate()
    {
        if (MaxDistance is < 1 or > 16_384)
            throw new ArgumentOutOfRangeException(nameof(MaxDistance));
        if (SampleSpan is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(SampleSpan));
        if (double.IsNaN(Threshold) || double.IsInfinity(Threshold) || Threshold is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(Threshold));
    }
}

public sealed record EdgeRulerHit(
    EdgeDirection Direction,
    int Distance,
    Point Location,
    double Score);

public sealed record EdgeRulerMeasurement(
    Point Origin,
    EdgeRulerHit? Left,
    EdgeRulerHit? Right,
    EdgeRulerHit? Top,
    EdgeRulerHit? Bottom)
{
    public IReadOnlyList<EdgeRulerHit> Hits => new[] { Left, Right, Top, Bottom }
        .OfType<EdgeRulerHit>()
        .OrderBy(hit => hit.Distance)
        .ToArray();

    public EdgeRulerHit? Nearest => Hits.FirstOrDefault();
}

/// <summary>Finds the nearest high-contrast UI edge around a sampled screen pixel.</summary>
public static class EdgeDetectionRulerService
{
    public static EdgeRulerMeasurement FindNearest(
        Bitmap image,
        Point origin,
        EdgeRulerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var resolved = options ?? new EdgeRulerOptions();
        resolved.Validate();
        if (image.Width == 0 || image.Height == 0)
            throw new ArgumentException("The sampled image is empty.", nameof(image));

        var point = new Point(
            Math.Clamp(origin.X, 0, image.Width - 1),
            Math.Clamp(origin.Y, 0, image.Height - 1));
        return new EdgeRulerMeasurement(
            point,
            FindHorizontal(image, point, -1, resolved),
            FindHorizontal(image, point, 1, resolved),
            FindVertical(image, point, -1, resolved),
            FindVertical(image, point, 1, resolved));
    }

    private static EdgeRulerHit? FindHorizontal(Bitmap image, Point origin, int direction, EdgeRulerOptions options)
    {
        for (int distance = 1; distance <= options.MaxDistance; distance++)
        {
            int x = origin.X + direction * distance;
            if (x <= 0 || x >= image.Width - 1)
                break;
            double score = AverageVerticalDifference(image, x - 1, x, origin.Y, options.SampleSpan);
            if (score >= options.Threshold)
            {
                return new EdgeRulerHit(
                    direction < 0 ? EdgeDirection.Left : EdgeDirection.Right,
                    distance,
                    new Point(x, origin.Y),
                    score);
            }
        }
        return null;
    }

    private static EdgeRulerHit? FindVertical(Bitmap image, Point origin, int direction, EdgeRulerOptions options)
    {
        for (int distance = 1; distance <= options.MaxDistance; distance++)
        {
            int y = origin.Y + direction * distance;
            if (y <= 0 || y >= image.Height - 1)
                break;
            double score = AverageHorizontalDifference(image, y - 1, y, origin.X, options.SampleSpan);
            if (score >= options.Threshold)
            {
                return new EdgeRulerHit(
                    direction < 0 ? EdgeDirection.Top : EdgeDirection.Bottom,
                    distance,
                    new Point(origin.X, y),
                    score);
            }
        }
        return null;
    }

    private static double AverageVerticalDifference(Bitmap image, int firstX, int secondX, int centerY, int span)
    {
        int from = Math.Max(0, centerY - span / 2);
        int to = Math.Min(image.Height - 1, centerY + span / 2);
        double total = 0;
        int count = 0;
        for (int y = from; y <= to; y++)
        {
            total += ColorDifference(image.GetPixel(firstX, y), image.GetPixel(secondX, y));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double AverageHorizontalDifference(Bitmap image, int firstY, int secondY, int centerX, int span)
    {
        int from = Math.Max(0, centerX - span / 2);
        int to = Math.Min(image.Width - 1, centerX + span / 2);
        double total = 0;
        int count = 0;
        for (int x = from; x <= to; x++)
        {
            total += ColorDifference(image.GetPixel(x, firstY), image.GetPixel(x, secondY));
            count++;
        }
        return count == 0 ? 0 : total / count;
    }

    private static double ColorDifference(Color first, Color second)
        => (Math.Abs(first.R - second.R) + Math.Abs(first.G - second.G) + Math.Abs(first.B - second.B)) / 3d;
}
