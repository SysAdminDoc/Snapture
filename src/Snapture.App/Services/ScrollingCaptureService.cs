using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Windows.Automation;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// First-pass scrolling capture using UIA's <c>IScrollProvider</c>. Drives the foreground
/// window's scroll-pattern from top to bottom, captures each frame via the active capture
/// engine, and stacks them vertically.
///
/// Known limitations (the v0.3.1 alpha is honest about these):
///   • Frame boundaries can show small visual duplicates because UIA reports scroll position
///     in percent, not pixels. Phase-correlation stitching ships in v0.4.
///   • Browsers and Office mostly route scroll through their own scroll-host elements; this
///     will fail silently for windows that don't expose <c>ScrollPattern</c>. ShareX and
///     Greenshot have the same issue with their UIA paths.
///   • Sticky headers / footers will repeat in every frame. Detection ships in v0.4.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScrollingCaptureService
{
    private readonly ICaptureEngine _engine;

    public ScrollingCaptureService(ICaptureEngine engine)
    {
        _engine = engine;
    }

    public async Task<Bitmap?> CaptureScrollingForegroundAsync(nint hwnd, IProgress<string>? progress = null)
    {
        if (hwnd == 0) { progress?.Report("No foreground window."); return null; }
        if (!WindowEnumerator.GetExtendedFrameBounds(hwnd, out var bounds))
        {
            progress?.Report("Could not resolve window bounds."); return null;
        }

        AutomationElement? root;
        try { root = AutomationElement.FromHandle(hwnd); }
        catch (Exception ex) { progress?.Report($"UIA failed: {ex.Message}"); return null; }
        if (root is null) { progress?.Report("UIA returned no element."); return null; }

        var scrollable = FindScrollable(root);
        if (scrollable is null)
        {
            progress?.Report("This window does not expose a UIA scroll pattern.");
            return null;
        }
        if (!scrollable.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern))
        {
            progress?.Report("ScrollPattern unavailable.");
            return null;
        }
        var scroll = (ScrollPattern)rawPattern;
        if (!scroll.Current.VerticallyScrollable)
        {
            progress?.Report("Window is not vertically scrollable.");
            return null;
        }

        double originalPercent = scroll.Current.VerticalScrollPercent;
        try
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            await Task.Delay(220);

            var frames = new List<Bitmap>();
            int safety = 0;
            const int MaxFrames = 40;
            while (safety++ < MaxFrames)
            {
                var capture = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
                frames.Add(capture.Bitmap);
                progress?.Report($"Frame {frames.Count} ({(int)scroll.Current.VerticalScrollPercent}%)");

                if (scroll.Current.VerticalScrollPercent >= 99) break;

                // Each LargeIncrement is one viewport-height unit. UIA returns percent; we step
                // until we hit 100. Sleep briefly to let lazy-load handlers settle.
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
                await Task.Delay(280);
            }

            return StackVertically(frames);
        }
        finally
        {
            try { scroll.SetScrollPercent(ScrollPattern.NoScroll, originalPercent); } catch { }
        }
    }

    private static AutomationElement? FindScrollable(AutomationElement root)
    {
        // Try the root itself first.
        if (HasScrollPattern(root)) return root;
        // Then breadth-first walk a couple of levels deep — most apps put the scroll provider
        // on the immediate content element.
        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        int budget = 200;
        while (queue.Count > 0 && budget-- > 0)
        {
            var cur = queue.Dequeue();
            try
            {
                var children = cur.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children)
                {
                    if (HasScrollPattern(child)) return child;
                    queue.Enqueue(child);
                }
            }
            catch { /* element gone — skip */ }
        }
        return null;
    }

    private static bool HasScrollPattern(AutomationElement el)
    {
        try { return el.GetSupportedPatterns().Any(p => p.Id == ScrollPattern.Pattern.Id); }
        catch { return false; }
    }

    private static Bitmap StackVertically(IReadOnlyList<Bitmap> frames)
    {
        if (frames.Count == 0) throw new InvalidOperationException("No frames to stack.");
        if (frames.Count == 1) return frames[0];

        // Use the seam-aligning stitcher. Falls back to naive concat for low-confidence pairs.
        var (stitched, _seams, _sticky) = ImageStitcher.Stitch(frames);
        // Stitcher returns a fresh bitmap when frames.Count > 1, so dispose the source frames.
        if (!ReferenceEquals(stitched, frames[0]))
            foreach (var f in frames) f.Dispose();
        return stitched;
    }
}
