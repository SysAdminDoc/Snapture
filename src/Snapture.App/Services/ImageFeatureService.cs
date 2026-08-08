using System.Globalization;
using System.IO;
using System.Numerics;
using SkiaSharp;

namespace Snapture.App.Services;

public sealed record ImageFeatureValues(
    string DominantColorHex,
    string PerceptualHash);

/// <summary>Computes small, local-only image signatures for history search and deduplication.</summary>
public static class ImageFeatureService
{
    private const int SampleGrid = 32;
    private const int ColorClusterCount = 5;

    public static ImageFeatureValues? Compute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            using var input = SafeImageInput.Open(path);
            using var bitmap = SKBitmap.Decode(input.Stream);
            if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
                return null;

            var colors = new SKColor[SampleGrid * SampleGrid];
            for (int y = 0; y < SampleGrid; y++)
            {
                int sourceY = Math.Min(bitmap.Height - 1, (y * bitmap.Height) / SampleGrid);
                for (int x = 0; x < SampleGrid; x++)
                {
                    int sourceX = Math.Min(bitmap.Width - 1, (x * bitmap.Width) / SampleGrid);
                    colors[(y * SampleGrid) + x] = bitmap.GetPixel(sourceX, sourceY);
                }
            }

            var dominant = FindDominantColor(colors);
            return new ImageFeatureValues(ToHex(dominant), ComputePerceptualHash(colors));
        }
        catch
        {
            return null;
        }
    }

    public static bool TryParseHex(string? value, out SKColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('#'))
            normalized = normalized[1..];
        if (normalized.Length != 6
            || !byte.TryParse(normalized[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(normalized[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
            return false;

        color = new SKColor(red, green, blue);
        return true;
    }

    public static int ColorDistance(string? leftHex, string? rightHex)
    {
        if (!TryParseHex(leftHex, out var left) || !TryParseHex(rightHex, out var right))
            return int.MaxValue;

        int red = left.Red - right.Red;
        int green = left.Green - right.Green;
        int blue = left.Blue - right.Blue;
        return (int)Math.Round(Math.Sqrt((red * red) + (green * green) + (blue * blue)));
    }

    public static int HammingDistance(string? leftHash, string? rightHash)
    {
        if (!ulong.TryParse(leftHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var left)
            || !ulong.TryParse(rightHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var right))
            return int.MaxValue;

        return BitOperations.PopCount(left ^ right);
    }

    public static bool IsNearDuplicate(string? leftHash, string? rightHash, int maxDistance = 6) =>
        HammingDistance(leftHash, rightHash) <= Math.Max(0, maxDistance);

    public static string ToHex(SKColor color) =>
        $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static SKColor FindDominantColor(IReadOnlyList<SKColor> pixels)
    {
        var centers = new Vector3[ColorClusterCount];
        for (int i = 0; i < centers.Length; i++)
            centers[i] = ToVector(pixels[(i * pixels.Count) / centers.Length]);

        var counts = new int[ColorClusterCount];
        var sums = new Vector3[ColorClusterCount];
        for (int iteration = 0; iteration < 6; iteration++)
        {
            Array.Clear(counts);
            Array.Clear(sums);
            foreach (var pixel in pixels)
            {
                int nearest = NearestCluster(ToVector(pixel), centers);
                counts[nearest]++;
                sums[nearest] += ToVector(pixel);
            }

            for (int i = 0; i < centers.Length; i++)
            {
                if (counts[i] > 0)
                    centers[i] = sums[i] / counts[i];
            }
        }

        Array.Clear(counts);
        foreach (var pixel in pixels)
            counts[NearestCluster(ToVector(pixel), centers)]++;

        int dominantIndex = 0;
        for (int i = 1; i < counts.Length; i++)
        {
            if (counts[i] > counts[dominantIndex])
                dominantIndex = i;
        }

        var dominantCenter = centers[dominantIndex];
        return new SKColor(
            (byte)Math.Clamp(Math.Round(dominantCenter.X), 0, 255),
            (byte)Math.Clamp(Math.Round(dominantCenter.Y), 0, 255),
            (byte)Math.Clamp(Math.Round(dominantCenter.Z), 0, 255));
    }

    private static int NearestCluster(Vector3 color, IReadOnlyList<Vector3> centers)
    {
        int nearest = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < centers.Count; i++)
        {
            float distance = Vector3.DistanceSquared(color, centers[i]);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    private static Vector3 ToVector(SKColor color) =>
        new(color.Red, color.Green, color.Blue);

    private static string ComputePerceptualHash(IReadOnlyList<SKColor> colors)
    {
        var grayscale = new double[SampleGrid * SampleGrid];
        for (int i = 0; i < colors.Count; i++)
        {
            var color = colors[i];
            grayscale[i] = (0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue);
        }

        var coefficients = new double[64];
        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int y = 0; y < SampleGrid; y++)
                {
                    for (int x = 0; x < SampleGrid; x++)
                    {
                        sum += grayscale[(y * SampleGrid) + x]
                            * Math.Cos(((2 * x) + 1) * u * Math.PI / (2 * SampleGrid))
                            * Math.Cos(((2 * y) + 1) * v * Math.PI / (2 * SampleGrid));
                    }
                }

                coefficients[(u * 8) + v] = Math.Abs(sum) < 0.001 ? 0 : sum;
            }
        }

        var sorted = coefficients[1..].Order().ToArray();
        double median = sorted[sorted.Length / 2];
        ulong hash = 0;
        for (int i = 0; i < coefficients.Length; i++)
        {
            hash <<= 1;
            if (i > 0 && coefficients[i] > median)
                hash |= 1;
        }

        return hash.ToString("X16", CultureInfo.InvariantCulture);
    }
}
