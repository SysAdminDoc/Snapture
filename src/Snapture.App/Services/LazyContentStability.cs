using System.Drawing;

namespace Snapture.App.Services;

/// <summary>Small sampled-image probe used to let lazy browser content settle.</summary>
internal static class LazyContentStability
{
    private const int SampleWidth = 32;
    private const int SampleHeight = 24;

    /// <summary>
    /// Returns the mean absolute RGBA difference across a fixed 32x24 sample grid.
    /// Zero means the images are pixel-identical at the sampled points.
    /// </summary>
    public static double MeanAbsoluteDifference(Bitmap first, Bitmap second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        if (first.Width != second.Width || first.Height != second.Height)
            return double.MaxValue;

        int width = first.Width;
        int height = first.Height;
        long difference = 0;
        int samples = 0;
        for (int sy = 0; sy < SampleHeight; sy++)
        {
            int y = SampleCoordinate(sy, SampleHeight, height);
            for (int sx = 0; sx < SampleWidth; sx++)
            {
                int x = SampleCoordinate(sx, SampleWidth, width);
                Color a = first.GetPixel(x, y);
                Color b = second.GetPixel(x, y);
                difference += Math.Abs(a.R - b.R)
                    + Math.Abs(a.G - b.G)
                    + Math.Abs(a.B - b.B)
                    + Math.Abs(a.A - b.A);
                samples++;
            }
        }

        return samples == 0 ? 0 : (double)difference / (samples * 4);
    }

    public static bool IsStable(Bitmap first, Bitmap second, double threshold = 4.0)
        => MeanAbsoluteDifference(first, second) <= threshold;

    private static int SampleCoordinate(int index, int sampleCount, int length)
        => length <= 1 ? 0 : (int)((long)index * (length - 1) / (sampleCount - 1));
}
