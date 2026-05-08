using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>
/// Stacks consecutive scrolling-capture frames by finding the vertical offset that minimises
/// pixel disagreement between the bottom of frame N-1 and frame N. Subsampled
/// sum-of-absolute-differences instead of FFT phase-correlation — at the resolutions we deal
/// with this is fast enough (≤200 ms per pair) and avoids a Math.NET dependency.
///
/// v0.6 adds sticky-header / sticky-footer detection: rows from the top that are identical
/// across every frame are emitted exactly once at the very top of the stitched output, and
/// rows from the bottom that are identical across every frame are emitted exactly once at the
/// bottom. The middle pages stack with the sticky strips excluded so they don't repeat.
///
/// Remaining limitations:
///   • Ads / animations between frames produce small ghosting at the seam.
///   • If a sticky header has internal animation (e.g. a clock), the row-similarity check
///     will fail and the header reverts to repeating per frame. Configure the threshold
///     down if you have a static header that the auto-detector misses.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ImageStitcher
{
    private const int Subsample = 4;
    private const int StripRows = 80; // matched bottom-of-A vs top-of-B band, in source pixels

    /// <summary>Per-row mean-absolute-difference threshold for "this row is identical across frames."</summary>
    private const int StickyRowMaxMad = 8;
    /// <summary>Hard cap on how many top/bottom rows we'll consider sticky — guards against degenerate solid-colour sources.</summary>
    private const int MaxStickyRows = 240;

    public sealed record SeamInfo(int OverlapPixels, double Confidence);

    public sealed record StickyDetection(int TopRows, int BottomRows);

    /// <summary>Stitch a sequence of equally-wide frames vertically with seam alignment.</summary>
    public static (Bitmap Stitched, IReadOnlyList<SeamInfo> Seams, StickyDetection Sticky) Stitch(IReadOnlyList<Bitmap> frames)
    {
        if (frames.Count == 0) throw new InvalidOperationException("No frames.");
        if (frames.Count == 1) return (frames[0], new[] { new SeamInfo(0, 1.0) }, new StickyDetection(0, 0));

        int width = frames.Max(f => f.Width);
        var seams = new List<SeamInfo> { new(0, 1.0) };

        // 1. Sticky-strip detection. Rows that are pixelwise identical across every frame are
        //    UI chrome that doesn't scroll (sticky nav, sticky footers, address bars).
        var sticky = DetectStickyStrips(frames, width);

        // 2. For overlap matching, ignore the sticky bands so we align the *content* not the
        //    chrome that sits in the same place every frame.
        var overlaps = new int[frames.Count];
        overlaps[0] = 0;
        for (int i = 1; i < frames.Count; i++)
        {
            var (overlap, conf) = FindOverlap(frames[i - 1], frames[i], sticky.TopRows, sticky.BottomRows);
            overlaps[i] = overlap;
            seams.Add(new SeamInfo(overlap, conf));
        }

        // 3. Compute final height: top-sticky once, then frame[0] content body, then for each
        //    subsequent frame the rows below its overlap point and above the bottom-sticky,
        //    then bottom-sticky once.
        int firstBody = Math.Max(0, frames[0].Height - sticky.TopRows - sticky.BottomRows);
        int totalHeight = sticky.TopRows + firstBody;
        for (int i = 1; i < frames.Count; i++)
        {
            int contentTop = Math.Max(sticky.TopRows, overlaps[i]);
            int contentBottom = Math.Max(0, frames[i].Height - sticky.BottomRows);
            int rows = Math.Max(0, contentBottom - contentTop);
            totalHeight += rows;
        }
        totalHeight += sticky.BottomRows;

        var stacked = new Bitmap(width, totalHeight, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(stacked))
        {
            int y = 0;

            // Top sticky from frame 0
            if (sticky.TopRows > 0)
            {
                g.DrawImage(frames[0],
                    new Rectangle(0, 0, frames[0].Width, sticky.TopRows),
                    new Rectangle(0, 0, frames[0].Width, sticky.TopRows),
                    GraphicsUnit.Pixel);
                y += sticky.TopRows;
            }

            // Frame 0 body: rows between top-sticky and bottom-sticky
            int f0BodyTop = sticky.TopRows;
            int f0BodyBottom = Math.Max(f0BodyTop, frames[0].Height - sticky.BottomRows);
            int f0BodyRows = f0BodyBottom - f0BodyTop;
            if (f0BodyRows > 0)
            {
                var src = new Rectangle(0, f0BodyTop, frames[0].Width, f0BodyRows);
                var dst = new Rectangle(0, y, src.Width, src.Height);
                g.DrawImage(frames[0], dst, src, GraphicsUnit.Pixel);
                y += f0BodyRows;
            }

            // Frames 1..N-1 body, with overlap dedup and sticky stripped
            for (int i = 1; i < frames.Count; i++)
            {
                int contentTop = Math.Max(sticky.TopRows, overlaps[i]);
                int contentBottom = Math.Max(contentTop, frames[i].Height - sticky.BottomRows);
                int rows = contentBottom - contentTop;
                if (rows <= 0) continue;
                var src = new Rectangle(0, contentTop, frames[i].Width, rows);
                var dst = new Rectangle(0, y, src.Width, src.Height);
                g.DrawImage(frames[i], dst, src, GraphicsUnit.Pixel);
                y += rows;
            }

            // Bottom sticky from the last frame
            if (sticky.BottomRows > 0)
            {
                var last = frames[^1];
                var src = new Rectangle(0, last.Height - sticky.BottomRows, last.Width, sticky.BottomRows);
                var dst = new Rectangle(0, y, src.Width, src.Height);
                g.DrawImage(last, dst, src, GraphicsUnit.Pixel);
                y += sticky.BottomRows;
            }
        }
        return (stacked, seams, sticky);
    }

    /// <summary>
    /// Returns the number of source-pixel rows of frame B that overlap the bottom of frame A,
    /// plus a [0..1] confidence score. The strip and search are taken from the *non-sticky*
    /// region so sticky-strip pixels don't anchor the alignment.
    /// </summary>
    public static (int Overlap, double Confidence) FindOverlap(Bitmap a, Bitmap b, int stickyTop, int stickyBottom)
    {
        int width = Math.Min(a.Width, b.Width);
        int aH = a.Height, bH = b.Height;

        int aBodyTop = stickyTop;
        int aBodyBottom = Math.Max(aBodyTop, aH - stickyBottom);
        int bBodyTop = stickyTop;
        int bBodyBottom = Math.Max(bBodyTop, bH - stickyBottom);

        int aBodyHeight = aBodyBottom - aBodyTop;
        int bBodyHeight = bBodyBottom - bBodyTop;

        if (width <= Subsample || aBodyHeight <= StripRows + Subsample || bBodyHeight <= StripRows + Subsample)
            return (0, 0);

        int sw = Math.Max(1, width / Subsample);
        int sh = Math.Max(1, StripRows / Subsample);
        var aStrip = SampleGray(a, new Rectangle(0, aBodyBottom - StripRows, width, StripRows), sw, sh);

        int searchEnd = (bBodyHeight - StripRows) / Subsample;
        if (searchEnd <= 0) return (0, 0);

        // Sample the body of B (skip top sticky, since body content is what scrolls)
        int bBodyDownH = bBodyHeight / Subsample;
        var bBody = SampleGray(b, new Rectangle(0, bBodyTop, width, bBodyDownH * Subsample), sw, bBodyDownH);

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
                    int diff = aStrip[aRow + sx] - bBody[bRow + sx];
                    if (diff < 0) diff = -diff;
                    sad += diff;
                }
            }
            if (sad < bestSad) { bestSad = sad; bestY = y; }
        }

        long worstPossible = (long)sw * sh * 255;
        double confidence = worstPossible == 0 ? 0 : 1.0 - (double)bestSad / worstPossible;
        if (confidence < 0.92) return (0, confidence);

        // bestY is in subsampled rows of the body. Source-pixel overlap = stickyTop + bestY*Subsample + StripRows.
        int overlap = stickyTop + bestY * Subsample + StripRows;
        if (overlap >= bH - 8) overlap = 0;
        return (overlap, confidence);
    }

    /// <summary>
    /// Find the longest run of rows from the top (and bottom) that are pixelwise stable across
    /// every frame. Stability is measured per-row by mean absolute difference vs frame[0].
    /// </summary>
    public static StickyDetection DetectStickyStrips(IReadOnlyList<Bitmap> frames, int width)
    {
        if (frames.Count < 2) return new StickyDetection(0, 0);
        int probeH = Math.Min(MaxStickyRows, frames.Min(f => f.Height) / 3);
        if (probeH <= 0) return new StickyDetection(0, 0);

        // Sample top probeH rows + bottom probeH rows of every frame, downsampled in width.
        int sw = Math.Max(1, width / Subsample);
        var topBufs = new byte[frames.Count][];
        var botBufs = new byte[frames.Count][];
        for (int i = 0; i < frames.Count; i++)
        {
            topBufs[i] = SampleGray(frames[i], new Rectangle(0, 0, width, probeH), sw, probeH);
            botBufs[i] = SampleGray(frames[i], new Rectangle(0, frames[i].Height - probeH, width, probeH), sw, probeH);
        }

        int topSticky = CountMatchingRows(topBufs, sw, probeH, fromTop: true);
        int bottomSticky = CountMatchingRows(botBufs, sw, probeH, fromTop: false);
        return new StickyDetection(topSticky, bottomSticky);
    }

    private static int CountMatchingRows(byte[][] bufs, int sw, int probeH, bool fromTop)
    {
        int count = 0;
        for (int r = 0; r < probeH; r++)
        {
            int row = fromTop ? r : probeH - 1 - r;
            // Compute MAD between every other frame's row and frame[0]'s row.
            bool allMatch = true;
            for (int f = 1; f < bufs.Length; f++)
            {
                long sum = 0;
                for (int x = 0; x < sw; x++)
                {
                    int d = bufs[0][row * sw + x] - bufs[f][row * sw + x];
                    if (d < 0) d = -d;
                    sum += d;
                }
                int mad = (int)(sum / Math.Max(1, sw));
                if (mad > StickyRowMaxMad) { allMatch = false; break; }
            }
            if (!allMatch) break;
            count++;
        }
        return count;
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
                        buf[oy * outW + ox] = (byte)((px[0] * 28 + px[1] * 151 + px[2] * 77) >> 8);
                    }
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return buf;
    }
}
