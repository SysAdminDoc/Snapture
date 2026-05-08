using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>
/// Stacks consecutive scrolling-capture frames by finding the vertical offset that minimises
/// pixel disagreement between the bottom of frame N-1 and frame N. We use subsampled
/// sum-of-absolute-differences instead of FFT phase-correlation — at the resolutions we deal
/// with this is fast enough (≤200 ms per pair) and avoids a Math.NET dependency.
///
/// Limitations the v0.5 alpha is honest about:
///   • If no good offset is found (correlation below threshold) we fall back to naive
///     concatenation. This shows up as visible duplicate strips at scroll boundaries.
///   • Sticky headers / footers will repeat across frames; sticky-strip detection is queued
///     for v0.6.
///   • Ads / animations between frames produce small ghosting at the seam.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageStitcher
{
    private const int Subsample = 4;
    private const int StripRows = 80; // measured on the previous frame, in source pixels

    public sealed record SeamInfo(int OverlapPixels, double Confidence);

    /// <summary>Stitch a sequence of equally-wide frames vertically with seam alignment.</summary>
    public static (Bitmap Stitched, IReadOnlyList<SeamInfo> Seams) Stitch(IReadOnlyList<Bitmap> frames)
    {
        if (frames.Count == 0) throw new InvalidOperationException("No frames.");
        if (frames.Count == 1) return (frames[0], new[] { new SeamInfo(0, 1.0) });

        int width = frames.Max(f => f.Width);
        var seams = new List<SeamInfo> { new(0, 1.0) };

        // First pass: compute overlaps so we know the final height.
        var overlaps = new int[frames.Count];
        overlaps[0] = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            var (overlap, conf) = FindOverlap(frames[i - 1], frames[i]);
            overlaps[i] = overlap;
            seams.Add(new SeamInfo(overlap, conf));
        }

        int totalHeight = frames[0].Height;
        for (int i = 1; i < frames.Count; i++)
            totalHeight += Math.Max(0, frames[i].Height - overlaps[i]);

        var stacked = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(stacked))
        {
            int y = 0;
            g.DrawImage(frames[0], 0, y);
            y += frames[0].Height;
            for (int i = 1; i < frames.Count; i++)
            {
                var src = new Rectangle(0, overlaps[i], frames[i].Width, Math.Max(0, frames[i].Height - overlaps[i]));
                var dst = new Rectangle(0, y, src.Width, src.Height);
                g.DrawImage(frames[i], dst, src, GraphicsUnit.Pixel);
                y += src.Height;
            }
        }
        return (stacked, seams);
    }

    /// <summary>
    /// Returns the number of source-pixel rows of frame B that overlap the bottom of frame A,
    /// plus a [0..1] confidence score.
    /// </summary>
    public static (int Overlap, double Confidence) FindOverlap(Bitmap a, Bitmap b)
    {
        int width = Math.Min(a.Width, b.Width);
        int aH = a.Height, bH = b.Height;
        if (width <= Subsample || aH <= StripRows + Subsample || bH <= StripRows + Subsample)
            return (0, 0);

        // Pull the bottom strip of A as a downsampled gray buffer.
        int sw = Math.Max(1, width / Subsample);
        int sh = Math.Max(1, StripRows / Subsample);
        var aStrip = SampleGray(a, new Rectangle(0, aH - StripRows, width, StripRows), sw, sh);

        // Search range in B: expect the strip to land somewhere between row 0 and (bH - StripRows).
        int searchEnd = (bH - StripRows) / Subsample;
        if (searchEnd <= 0) return (0, 0);

        int bDownH = bH / Subsample;
        var bDown = SampleGray(b, new Rectangle(0, 0, width, bDownH * Subsample), sw, bDownH);

        long bestSad = long.MaxValue;
        int bestY = 0;
        for (int y = 0; y < searchEnd; y++)
        {
            long sad = 0;
            for (int sy = 0; sy < sh; sy++)
            {
                int aRow = sy * sw;
                int bRow = (y + sy) * sw;
                for (int sx = 0; sx < sw; sx++)
                {
                    int diff = aStrip[aRow + sx] - bDown[bRow + sx];
                    if (diff < 0) diff = -diff;
                    sad += diff;
                }
            }
            if (sad < bestSad) { bestSad = sad; bestY = y; }
        }

        // Compute a confidence figure normalised against the worst possible match.
        long worstPossible = (long)sw * sh * 255;
        double confidence = worstPossible == 0 ? 0 : 1.0 - (double)bestSad / worstPossible;

        // If the best match is essentially noise, give up — better naive concatenation.
        if (confidence < 0.92) return (0, confidence);

        // bestY in subsampled rows → source rows. The overlap is StripRows + bestY*Subsample.
        // We position frame B starting at the row in A that aligns to its top; the overlap
        // is everything in B above the strip.
        int overlap = bestY * Subsample + StripRows;
        if (overlap >= bH - 8) overlap = 0; // implausible — degenerate.
        return (overlap, confidence);
    }

    private static byte[] SampleGray(Bitmap bmp, Rectangle src, int outW, int outH)
    {
        var buf = new byte[outW * outH];
        var data = bmp.LockBits(src, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* p = (byte*)data.Scan0;
                int stride = data.Stride;
                for (int oy = 0; oy < outH; oy++)
                {
                    int sy = (int)((long)oy * src.Height / outH);
                    byte* row = p + sy * stride;
                    for (int ox = 0; ox < outW; ox++)
                    {
                        int sx = (int)((long)ox * src.Width / outW);
                        byte* px = row + sx * 4;
                        // BGRA → fast luminance approximation
                        buf[oy * outW + ox] = (byte)((px[0] * 28 + px[1] * 151 + px[2] * 77) >> 8);
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return buf;
    }
}
