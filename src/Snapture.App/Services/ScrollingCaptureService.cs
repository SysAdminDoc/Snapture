using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Windows.Automation;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Scrolling capture using UIA's <c>IScrollProvider</c>. Drives the foreground window's
/// scroll-pattern from top to bottom, captures each frame via the active capture engine,
/// and stacks them vertically. Chromium 130+ exposes its web document through the default
/// Windows UIA provider; that path uses a descendant query and a percent-step fallback
/// for providers that do not implement <c>Scroll</c> reliably.
///
/// Known limitations (the v0.3.1 alpha is honest about these):
///   • Frame boundaries can show small visual duplicates because UIA reports scroll position
///     in percent, not pixels. Phase-correlation stitching ships in v0.4.
///   • Non-browser apps still need to expose <c>ScrollPattern</c>; browser-specific discovery
///     is intentionally limited to Chromium-family window classes.
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

        bool isChromium = IsChromiumWindow(root);
        var scrollable = FindScrollable(root, bounds, isChromium);
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
        var frames = new List<Bitmap>();
        try
        {
            scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
            await Task.Delay(220);

            if (isChromium)
                progress?.Report("Chromium UIA scroll backend");

            int safety = 0;
            const int MaxFrames = 40;
            while (safety++ < MaxFrames)
            {
                var capture = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
                frames.Add(capture.Bitmap);
                progress?.Report($"Frame {frames.Count} ({(int)scroll.Current.VerticalScrollPercent}%)");

                if (scroll.Current.VerticalScrollPercent >= 99) break;

                double before = scroll.Current.VerticalScrollPercent;
                if (!AdvanceScroll(scroll, isChromium, before))
                {
                    progress?.Report("Scroll provider stopped advancing.");
                    break;
                }

                // Sleep briefly to let browser compositing and lazy-load handlers settle.
                await Task.Delay(280);
            }

            return StackVertically(frames);
        }
        catch
        {
            foreach (var frame in frames) frame.Dispose();
            throw;
        }
        finally
        {
            try { scroll.SetScrollPercent(ScrollPattern.NoScroll, originalPercent); } catch { }
        }
    }

    private static AutomationElement? FindScrollable(
        AutomationElement root,
        Rectangle windowBounds,
        bool isChromium)
    {
        if (isChromium)
        {
            var browserDocument = FindChromiumDocument(root, windowBounds);
            if (browserDocument is not null) return browserDocument;
        }

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

    private static AutomationElement? FindChromiumDocument(
        AutomationElement root,
        Rectangle windowBounds)
    {
        try
        {
            if (IsVerticallyScrollable(root)) return root;

            var condition = new PropertyCondition(
                AutomationElement.IsScrollPatternAvailableProperty, true);
            var candidates = root.FindAll(TreeScope.Descendants, condition);
            AutomationElement? best = null;
            double bestArea = 0;
            int inspected = 0;
            var captureRect = new System.Windows.Rect(
                windowBounds.X, windowBounds.Y, windowBounds.Width, windowBounds.Height);

            foreach (AutomationElement candidate in candidates)
            {
                if (++inspected > 256 || !IsVerticallyScrollable(candidate)) continue;
                var rect = candidate.Current.BoundingRectangle;
                if (rect.IsEmpty || !rect.IntersectsWith(captureRect)) continue;

                double area = Math.Max(0, rect.Width) * Math.Max(0, rect.Height);
                if (area > bestArea)
                {
                    best = candidate;
                    bestArea = area;
                }
            }
            return best;
        }
        catch
        {
            return null;
        }
    }

    private static bool HasScrollPattern(AutomationElement el)
    {
        try { return el.GetSupportedPatterns().Any(p => p.Id == ScrollPattern.Pattern.Id); }
        catch { return false; }
    }

    private static bool IsVerticallyScrollable(AutomationElement el)
    {
        try
        {
            return el.TryGetCurrentPattern(ScrollPattern.Pattern, out var rawPattern)
                && rawPattern is ScrollPattern pattern
                && pattern.Current.VerticallyScrollable;
        }
        catch { return false; }
    }

    private static bool AdvanceScroll(ScrollPattern scroll, bool isChromium, double before)
    {
        try
        {
            scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
            double after = scroll.Current.VerticalScrollPercent;
            if (after > before + 0.01 || after >= 99) return true;
        }
        catch
        {
            if (!isChromium) return false;
        }

        if (!isChromium) return false;

        try
        {
            double viewSize = scroll.Current.VerticalViewSize;
            double step = viewSize is > 0 and < 100 ? viewSize : 50;
            double next = Math.Min(100, before + Math.Max(5, step));
            if (next <= before + 0.01) return false;
            scroll.SetScrollPercent(ScrollPattern.NoScroll, next);
            return scroll.Current.VerticalScrollPercent > before + 0.01
                || scroll.Current.VerticalScrollPercent >= 99;
        }
        catch { return false; }
    }

    private static bool IsChromiumWindow(AutomationElement root)
    {
        try { return IsChromiumWindowClass(root.Current.ClassName); }
        catch { return false; }
    }

    internal static bool IsChromiumWindowClass(string? className)
        => className?.StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase) == true
            || string.Equals(className, "Chrome_WidgetWin_0", StringComparison.OrdinalIgnoreCase);

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
