using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Snapture.App.Services;

internal enum RecordingChromeKind
{
    Tab,
    Toolbar,
    MenuBar,
    StatusBar,
    Taskbar,
    Dock
}

internal readonly record struct RecordingChromeRegion(
    Rectangle Bounds,
    RecordingChromeKind Kind,
    string Source);

internal sealed record RecordingAutoTightenPlan(
    Rectangle CaptureBounds,
    Rectangle Crop,
    IReadOnlyList<RecordingChromeRegion> RemovedRegions)
{
    public bool IsApplied => Crop != CaptureBounds;

    public string Description
    {
        get
        {
            if (!IsApplied)
                return "auto-tighten found no safe edge chrome";

            string kinds = string.Join(", ", RemovedRegions
                .Select(region => region.Kind switch
                {
                    RecordingChromeKind.Tab => "tabs",
                    RecordingChromeKind.Toolbar => "toolbars",
                    RecordingChromeKind.MenuBar => "menus",
                    RecordingChromeKind.StatusBar => "status bars",
                    RecordingChromeKind.Taskbar => "taskbar",
                    RecordingChromeKind.Dock => "dock",
                    _ => "chrome"
                })
                .Distinct(StringComparer.OrdinalIgnoreCase));
            return $"auto-tighten removed {kinds}";
        }
    }
}

/// <summary>
/// Detects edge-mounted UIA chrome without changing focus, pointer state, or window state.
/// The pure planning method is deliberately conservative: a crop is only returned when
/// an edge control spans most of the capture and the remaining content stays usable.
/// </summary>
internal static class RecordingAutoTightenEngine
{
    private const int MaxUiAutomationElements = 600;
    private const int MaxEdgeStripPixels = 180;
    private const double MinSpan = 0.55;
    private const double MinRemainingWidth = 0.55;
    private const double MinRemainingHeight = 0.50;

    public static RecordingAutoTightenPlan BuildPlan(
        Rectangle captureBounds,
        IEnumerable<RecordingChromeRegion> regions)
    {
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
            return new RecordingAutoTightenPlan(captureBounds, Rectangle.Empty, Array.Empty<RecordingChromeRegion>());

        var visible = regions
            .Select(region => (Region: region, Bounds: Rectangle.Intersect(captureBounds, region.Bounds)))
            .Where(item => item.Bounds.Width > 0 && item.Bounds.Height > 0)
            .ToList();

        int edgeDepthX = Math.Min(MaxEdgeStripPixels, Math.Max(32, captureBounds.Width / 10));
        int edgeDepthY = Math.Min(MaxEdgeStripPixels, Math.Max(32, captureBounds.Height / 10));

        var top = visible
            .Where(item => IsHorizontalStrip(item.Bounds, captureBounds, edgeDepthY)
                && item.Bounds.Top <= captureBounds.Top + edgeDepthY)
            .ToList();
        var bottom = visible
            .Where(item => IsHorizontalStrip(item.Bounds, captureBounds, edgeDepthY)
                && item.Bounds.Bottom >= captureBounds.Bottom - edgeDepthY)
            .ToList();
        var left = visible
            .Where(item => IsVerticalStrip(item.Bounds, captureBounds, edgeDepthX)
                && item.Bounds.Left <= captureBounds.Left + edgeDepthX)
            .ToList();
        var right = visible
            .Where(item => IsVerticalStrip(item.Bounds, captureBounds, edgeDepthX)
                && item.Bounds.Right >= captureBounds.Right - edgeDepthX)
            .ToList();

        int topInset = top.Count == 0 ? 0 : top.Max(item => item.Bounds.Bottom - captureBounds.Top);
        int bottomInset = bottom.Count == 0 ? 0 : bottom.Max(item => captureBounds.Bottom - item.Bounds.Top);
        int leftInset = left.Count == 0 ? 0 : left.Max(item => item.Bounds.Right - captureBounds.Left);
        int rightInset = right.Count == 0 ? 0 : right.Max(item => captureBounds.Right - item.Bounds.Left);

        var crop = Rectangle.FromLTRB(
            captureBounds.Left + leftInset,
            captureBounds.Top + topInset,
            captureBounds.Right - rightInset,
            captureBounds.Bottom - bottomInset);

        if (!IsSafeCrop(crop, captureBounds))
            return new RecordingAutoTightenPlan(captureBounds, captureBounds, Array.Empty<RecordingChromeRegion>());

        var removed = top.Concat(bottom).Concat(left).Concat(right)
            .Select(item => item.Region with { Bounds = item.Bounds })
            .Distinct()
            .ToList();
        return new RecordingAutoTightenPlan(captureBounds, crop, removed);
    }

    public static RecordingAutoTightenPlan DetectWindow(nint hwnd, Rectangle captureBounds)
    {
        if (hwnd == 0)
            return BuildPlan(captureBounds, Array.Empty<RecordingChromeRegion>());

        try
        {
            var root = AutomationElement.FromHandle(hwnd);
            return BuildPlan(captureBounds, ReadElementTree(root));
        }
        catch
        {
            return BuildPlan(captureBounds, Array.Empty<RecordingChromeRegion>());
        }
    }

    public static RecordingAutoTightenPlan DetectMonitor(Rectangle captureBounds)
    {
        List<RecordingChromeRegion> regions = new();
        try
        {
            var root = AutomationElement.RootElement;
            var children = root.FindAll(TreeScope.Children, Condition.TrueCondition);
            foreach (AutomationElement child in children)
                TryAddRegion(child, captureBounds, regions);
        }
        catch
        {
            // The monitor capture still works when UIA is unavailable.
        }

        AddVisibleTaskbars(captureBounds, regions);
        return BuildPlan(captureBounds, regions);
    }

    private static IEnumerable<RecordingChromeRegion> ReadElementTree(AutomationElement root)
    {
        Queue<AutomationElement> pending = new();
        pending.Enqueue(root);
        int budget = MaxUiAutomationElements;

        while (pending.Count > 0 && budget-- > 0)
        {
            var current = pending.Dequeue();
            if (TryReadRegion(current, out var region))
                yield return region;

            try
            {
                var children = current.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children)
                    pending.Enqueue(child);
            }
            catch
            {
                // Elements can disappear while a recording starts; skip that branch.
            }
        }
    }

    private static void AddVisibleTaskbars(Rectangle captureBounds, ICollection<RecordingChromeRegion> regions)
    {
        foreach (string className in new[] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" })
        {
            nint hwnd = FindWindowEx(0, 0, className, null);
            while (hwnd != 0)
            {
                if (IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var nativeRect))
                {
                    var bounds = Rectangle.FromLTRB(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
                    if (bounds.IntersectsWith(captureBounds))
                    {
                        try
                        {
                            var element = AutomationElement.FromHandle(hwnd);
                            if (element is not null)
                                TryAddRegion(element, captureBounds, regions, RecordingChromeKind.Taskbar);
                        }
                        catch { }
                    }
                }

                hwnd = FindWindowEx(0, hwnd, className, null);
            }
        }
    }

    private static void TryAddRegion(
        AutomationElement element,
        Rectangle captureBounds,
        ICollection<RecordingChromeRegion> regions,
        RecordingChromeKind? forcedKind = null)
    {
        if (!TryReadRegion(element, out var region, forcedKind))
            return;

        if (region.Bounds.IntersectsWith(captureBounds))
            regions.Add(region);
    }

    private static bool TryReadRegion(
        AutomationElement element,
        out RecordingChromeRegion region,
        RecordingChromeKind? forcedKind = null)
    {
        region = default;
        try
        {
            var current = element.Current;
            if (current.IsOffscreen)
                return false;

            var rect = current.BoundingRectangle;
            if (rect.IsEmpty || double.IsNaN(rect.X) || double.IsNaN(rect.Y)
                || rect.Width < 1 || rect.Height < 1)
                return false;

            var kind = forcedKind ?? Classify(current);
            if (kind is null)
                return false;

            region = new RecordingChromeRegion(
                new Rectangle((int)Math.Round(rect.X), (int)Math.Round(rect.Y),
                    Math.Max(1, (int)Math.Round(rect.Width)), Math.Max(1, (int)Math.Round(rect.Height))),
                kind.Value,
                current.ClassName ?? current.Name ?? kind.Value.ToString());
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static RecordingChromeKind? Classify(AutomationElement.AutomationElementInformation current)
    {
        string controlType = current.ControlType?.ProgrammaticName?.Replace("ControlType.", "", StringComparison.OrdinalIgnoreCase) ?? "";
        string descriptor = string.Join(" ", controlType, current.ClassName, current.AutomationId, current.Name)
            .ToLowerInvariant();

        if (descriptor.Contains("shell_traywnd", StringComparison.Ordinal)
            || descriptor.Contains("shell_secondarytraywnd", StringComparison.Ordinal)
            || descriptor.Contains("taskbar", StringComparison.Ordinal))
            return RecordingChromeKind.Taskbar;
        if (descriptor.Contains("dock", StringComparison.Ordinal))
            return RecordingChromeKind.Dock;

        return controlType switch
        {
            "Tab" or "TabItem" => RecordingChromeKind.Tab,
            "ToolBar" => RecordingChromeKind.Toolbar,
            "MenuBar" => RecordingChromeKind.MenuBar,
            "StatusBar" => RecordingChromeKind.StatusBar,
            _ when descriptor.Contains("toolbar", StringComparison.Ordinal) => RecordingChromeKind.Toolbar,
            _ when descriptor.Contains("menubar", StringComparison.Ordinal) => RecordingChromeKind.MenuBar,
            _ when descriptor.Contains("statusbar", StringComparison.Ordinal) => RecordingChromeKind.StatusBar,
            _ => null
        };
    }

    private static bool IsHorizontalStrip(Rectangle region, Rectangle capture, int edgeDepth)
        => region.Width >= capture.Width * MinSpan
            && region.Height <= edgeDepth
            && region.Height <= capture.Height * 0.20;

    private static bool IsVerticalStrip(Rectangle region, Rectangle capture, int edgeDepth)
        => region.Height >= capture.Height * MinSpan
            && region.Width <= edgeDepth
            && region.Width <= capture.Width * 0.20;

    private static bool IsSafeCrop(Rectangle crop, Rectangle capture)
    {
        if (crop.Width <= 0 || crop.Height <= 0)
            return false;
        if (crop.Width < Math.Max(320, capture.Width * MinRemainingWidth)
            || crop.Height < Math.Max(180, capture.Height * MinRemainingHeight))
            return false;

        return crop.Left >= capture.Left && crop.Top >= capture.Top
            && crop.Right <= capture.Right && crop.Bottom <= capture.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parentHandle, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hwnd);
}
