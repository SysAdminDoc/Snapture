using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace Snapture.Capture;

public sealed record WindowInfo(nint Handle, string Title, string ClassName, Rectangle Bounds, uint ProcessId);

public static class WindowEnumerator
{
    public static IReadOnlyList<WindowInfo> EnumerateTopLevel()
    {
        var list = new List<WindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            if (!IsCapturable(hwnd)) return true;
            var title = GetTitle(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true;
            if (!GetExtendedFrameBounds(hwnd, out var bounds)) return true;
            if (bounds.Width < 4 || bounds.Height < 4) return true;
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            list.Add(new WindowInfo(hwnd, title, GetClassName(hwnd), bounds, pid));
            return true;
        }, 0);
        return list;
    }

    public static WindowInfo? FromPoint(Point virtualPoint)
    {
        var pt = new POINT { x = virtualPoint.X, y = virtualPoint.Y };
        nint hwnd = WindowFromPoint(pt);
        if (hwnd == 0) return null;
        nint root = GetAncestor(hwnd, GA_ROOT);
        if (root == 0) root = hwnd;
        if (!GetExtendedFrameBounds(root, out var bounds)) return null;
        GetWindowThreadProcessId(root, out uint pid);
        return new WindowInfo(root, GetTitle(root), GetClassName(root), bounds, pid);
    }

    public static bool GetExtendedFrameBounds(nint hwnd, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT r, Marshal.SizeOf<RECT>()) == 0)
        {
            bounds = new Rectangle(r.left, r.top, r.right - r.left, r.bottom - r.top);
            return true;
        }
        if (GetWindowRect(hwnd, out r))
        {
            bounds = new Rectangle(r.left, r.top, r.right - r.left, r.bottom - r.top);
            return true;
        }
        return false;
    }

    private static bool IsCapturable(nint hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        if ((style & WS_DISABLED) != 0) return false;
        if ((ex & WS_EX_TOOLWINDOW) != 0) return false;
        var cloaked = 0;
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
        if (cloaked != 0) return false;
        return true;
    }

    private static string GetTitle(nint hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len == 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string GetClassName(nint hwnd)
    {
        var sb = new StringBuilder(256);
        _ = GetClassNameW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private const int GWL_STYLE = -16, GWL_EXSTYLE = -20, GA_ROOT = 2;
    private const long WS_DISABLED = 0x08000000L, WS_EX_TOOLWINDOW = 0x00000080L;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetWindowText(nint hwnd, StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")] private static extern int GetClassNameW(nint hwnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RECT lpRect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, int gaFlags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
