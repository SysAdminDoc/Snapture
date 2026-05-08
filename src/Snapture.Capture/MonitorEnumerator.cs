using System.Drawing;
using System.Runtime.InteropServices;

namespace Snapture.Capture;

public sealed record MonitorInfo(
    nint Handle,
    string DeviceName,
    Rectangle Bounds,
    Rectangle WorkArea,
    bool IsPrimary,
    uint DpiX,
    uint DpiY);

public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var list = new List<MonitorInfo>();
        bool Cb(nint hMonitor, nint _, ref RECT __, nint ___)
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(hMonitor, ref mi))
            {
                uint dpiX = 96, dpiY = 96;
                _ = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out dpiX, out dpiY);
                var bounds = new Rectangle(mi.rcMonitor.left, mi.rcMonitor.top,
                    mi.rcMonitor.right - mi.rcMonitor.left, mi.rcMonitor.bottom - mi.rcMonitor.top);
                var work = new Rectangle(mi.rcWork.left, mi.rcWork.top,
                    mi.rcWork.right - mi.rcWork.left, mi.rcWork.bottom - mi.rcWork.top);
                list.Add(new MonitorInfo(hMonitor, mi.szDevice ?? "", bounds, work,
                    (mi.dwFlags & 1) == 1, dpiX, dpiY));
            }
            return true;
        }
        EnumDisplayMonitors(0, 0, Cb, 0);
        return list;
    }

    public static Rectangle GetVirtualScreen()
    {
        int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return new Rectangle(x, y, w, h);
    }

    public static MonitorInfo? FromPoint(Point virtualPoint)
    {
        var pt = new POINT { x = virtualPoint.X, y = virtualPoint.Y };
        nint h = MonitorFromPoint(pt, 2 /*MONITOR_DEFAULTTONEAREST*/);
        return Enumerate().FirstOrDefault(m => m.Handle == h);
    }

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }
    private delegate bool MonitorEnumProc(nint hMonitor, nint hdc, ref RECT lprcMonitor, nint dwData);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint lprcClip, MonitorEnumProc lpfn, nint dwData);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFOEX lpmi);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(nint hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
