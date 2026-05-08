using System.Drawing;

namespace Snapture.Capture;

public interface ICaptureEngine
{
    string Name { get; }
    Task<CaptureResult> CaptureRegionAsync(Rectangle virtualRegion, CancellationToken ct = default);
    Task<CaptureResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default);
    Task<CaptureResult> CaptureMonitorAsync(MonitorInfo monitor, CancellationToken ct = default);
    Task<CaptureResult> CaptureVirtualScreenAsync(CancellationToken ct = default);
}
