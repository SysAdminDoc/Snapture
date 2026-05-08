using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.Capture;

[SupportedOSPlatform("windows")]
public sealed class GdiCaptureEngine : ICaptureEngine
{
    public string Name => "GDI";

    public Task<CaptureResult> CaptureRegionAsync(Rectangle virtualRegion, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var bmp = CaptureScreenRect(virtualRegion);
            return new CaptureResult(bmp, virtualRegion, DateTime.UtcNow, "Region");
        }, ct);

    public Task<CaptureResult> CaptureVirtualScreenAsync(CancellationToken ct = default)
        => Task.Run(() =>
        {
            var v = MonitorEnumerator.GetVirtualScreen();
            var bmp = CaptureScreenRect(v);
            return new CaptureResult(bmp, v, DateTime.UtcNow, "VirtualScreen");
        }, ct);

    public Task<CaptureResult> CaptureMonitorAsync(MonitorInfo monitor, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var bmp = CaptureScreenRect(monitor.Bounds);
            return new CaptureResult(bmp, monitor.Bounds, DateTime.UtcNow, $"Monitor:{monitor.DeviceName}");
        }, ct);

    public Task<CaptureResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default)
        => Task.Run(() =>
        {
            if (!WindowEnumerator.GetExtendedFrameBounds(hwnd, out var bounds))
                throw new InvalidOperationException("Could not resolve window bounds.");
            var bmp = CaptureWindowPrintWindow(hwnd, bounds);
            return new CaptureResult(bmp, bounds, DateTime.UtcNow, "Window", hwnd);
        }, ct);

    private static Bitmap CaptureScreenRect(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            throw new ArgumentException("Capture region has zero or negative size.");
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }

    private static Bitmap CaptureWindowPrintWindow(nint hwnd, Rectangle bounds)
    {
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            nint hdc = g.GetHdc();
            try
            {
                bool ok = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
                if (!ok)
                {
                    g.ReleaseHdc(hdc);
                    bmp.Dispose();
                    return CaptureScreenRect(bounds);
                }
            }
            finally { g.ReleaseHdc(hdc); }
        }
        return bmp;
    }

    private const uint PW_RENDERFULLCONTENT = 0x00000002;
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint hdcBlt, uint nFlags);
}
