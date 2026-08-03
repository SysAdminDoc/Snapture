using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>
/// Finds visible overlay-style top-level windows intersecting a capture rectangle.
/// WGC normally handles ordinary compositor surfaces; a layered/tool topmost window
/// is the signal to try the Magnification API's composed-desktop path.
/// </summary>
[SupportedOSPlatform("windows")]
public static class LayeredWindowDetector
{
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOPMOST = 0x0000_0008;
    private const long WS_EX_TOOLWINDOW = 0x0000_0080;
    private const long WS_EX_LAYERED = 0x0008_0000;
    private const int DWMWA_CLOAKED = 14;

    public static bool HasVisibleOverlay(Rectangle captureBounds)
    {
        if (captureBounds.Width <= 0 || captureBounds.Height <= 0)
            return false;

        try
        {
            uint currentProcess = GetCurrentProcessId();
            bool found = false;
            EnumWindows((hwnd, _) =>
            {
                if (hwnd == 0 || !IsWindowVisible(hwnd)) return true;

                GetWindowThreadProcessId(hwnd, out uint processId);
                if (processId == currentProcess || IsCloaked(hwnd)) return true;

                long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                if (!IsOverlayStyle(style) || !GetWindowRect(hwnd, out var nativeBounds))
                    return true;

                var windowBounds = new Rectangle(
                    nativeBounds.Left,
                    nativeBounds.Top,
                    nativeBounds.Right - nativeBounds.Left,
                    nativeBounds.Bottom - nativeBounds.Top);
                found = windowBounds.Width > 0
                    && windowBounds.Height > 0
                    && windowBounds.IntersectsWith(captureBounds);
                return !found;
            }, 0);

            return found;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsOverlayStyle(long extendedStyle)
        => (extendedStyle & WS_EX_LAYERED) != 0
            || (extendedStyle & WS_EX_TOPMOST) != 0
                && (extendedStyle & WS_EX_TOOLWINDOW) != 0;

    private static bool IsCloaked(nint hwnd)
        => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) >= 0
            && cloaked != 0;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(
        nint hwnd, int attribute, out int value, int valueSize);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentProcessId();
}
