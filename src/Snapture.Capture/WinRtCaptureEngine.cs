using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Snapture.Capture;

/// <summary>
/// Capture engine backed by <c>Windows.Graphics.Capture</c> (WGC).
/// Single-frame mode: spin up a free-threaded frame pool, capture exactly one frame,
/// blit to a CPU staging texture, copy into a managed <see cref="Bitmap"/>, tear down.
/// Falls back to <see cref="GdiCaptureEngine"/> internally if WGC throws or returns
/// an empty/black surface (the WDA_EXCLUDEFROMCAPTURE case).
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WinRtCaptureEngine : ICaptureEngine, IDisposable
{
    public string Name => "WinRT";

    public bool IncludeSecondaryWindows { get; set; }
    public bool IncludeCursor { get; set; } = true;

    private readonly GdiCaptureEngine _fallback = new();
    private readonly object _deviceLock = new();
    private nint _d3dDevice;
    private nint _d3dContext;
    private IDirect3DDevice? _direct3DDevice;
    private bool _disposed;

    /// <summary>
    /// True if WGC is reachable on this OS build.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            try
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                    return false;
                return GraphicsCaptureSession.IsSupported();
            }
            catch
            {
                return false;
            }
        }
    }

    public Task<CaptureResult> CaptureRegionAsync(Rectangle virtualRegion, CancellationToken ct = default)
        => CaptureVirtualRegionInternalAsync(virtualRegion, "Region", ct);

    public Task<CaptureResult> CaptureVirtualScreenAsync(CancellationToken ct = default)
    {
        var v = MonitorEnumerator.GetVirtualScreen();
        return CaptureVirtualRegionInternalAsync(v, "VirtualScreen", ct);
    }

    public Task<CaptureResult> CaptureMonitorAsync(MonitorInfo monitor, CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                var bmp = CaptureMonitorBitmap(monitor.Handle);
                if (BitmapIsEmpty(bmp))
                {
                    bmp.Dispose();
                    return _fallback.CaptureMonitorAsync(monitor, ct).GetAwaiter().GetResult();
                }
                return new CaptureResult(bmp, monitor.Bounds, DateTime.UtcNow, $"Monitor:{monitor.DeviceName}");
            }
            catch
            {
                return _fallback.CaptureMonitorAsync(monitor, ct).GetAwaiter().GetResult();
            }
        }, ct);

    public Task<CaptureResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default)
        => Task.Run(() =>
        {
            try
            {
                if (!WindowEnumerator.GetExtendedFrameBounds(hwnd, out var bounds))
                    throw new InvalidOperationException("Could not resolve window bounds.");
                var bmp = CaptureWindowBitmap(hwnd);
                if (BitmapIsEmpty(bmp))
                {
                    // Likely WDA_EXCLUDEFROMCAPTURE — surface a sentinel for the orchestrator.
                    bmp.Dispose();
                    throw new CaptureExcludedException(
                        "The OS marks this window as excluded from capture (WDA_EXCLUDEFROMCAPTURE). " +
                        "1Password / Bitwarden / banking windows do this on purpose.");
                }
                return new CaptureResult(bmp, bounds, DateTime.UtcNow, "Window", hwnd);
            }
            catch (CaptureExcludedException) { throw; }
            catch
            {
                return _fallback.CaptureWindowAsync(hwnd, ct).GetAwaiter().GetResult();
            }
        }, ct);

    private Task<CaptureResult> CaptureVirtualRegionInternalAsync(Rectangle virtualRegion, string source, CancellationToken ct)
        => Task.Run(() =>
        {
            try
            {
                // Strategy: capture each intersecting monitor, blit each piece into the
                // composite bitmap. This handles per-monitor DPI cleanly because WGC
                // surfaces are already in device pixels.
                var composite = new Bitmap(virtualRegion.Width, virtualRegion.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(composite))
                {
                    g.Clear(Color.Black);
                    foreach (var mon in MonitorEnumerator.Enumerate())
                    {
                        var inter = Rectangle.Intersect(mon.Bounds, virtualRegion);
                        if (inter.Width <= 0 || inter.Height <= 0) continue;
                        using var monBmp = CaptureMonitorBitmap(mon.Handle);
                        if (BitmapIsEmpty(monBmp)) continue;
                        var src = new Rectangle(
                            inter.X - mon.Bounds.X,
                            inter.Y - mon.Bounds.Y,
                            inter.Width,
                            inter.Height);
                        var dst = new Rectangle(
                            inter.X - virtualRegion.X,
                            inter.Y - virtualRegion.Y,
                            inter.Width,
                            inter.Height);
                        // Scale per-monitor pixels into the virtual-screen bitmap
                        g.DrawImage(monBmp, dst, src, GraphicsUnit.Pixel);
                    }
                }
                return new CaptureResult(composite, virtualRegion, DateTime.UtcNow, source);
            }
            catch
            {
                return _fallback.CaptureRegionAsync(virtualRegion, ct).GetAwaiter().GetResult();
            }
        }, ct);

    // ---- WinRT capture core --------------------------------------------------

    private Bitmap CaptureMonitorBitmap(nint hMonitor)
    {
        EnsureDevice();
        var item = CaptureItemFactory.CreateForMonitor(hMonitor)
            ?? throw new InvalidOperationException("CreateForMonitor returned null.");
        return CaptureSingleFrame(item, CapturePixelFormatPolicy.ResolveForMonitor(hMonitor));
    }

    private Bitmap CaptureWindowBitmap(nint hwnd)
    {
        EnsureDevice();
        var item = CaptureItemFactory.CreateForWindow(hwnd)
            ?? throw new InvalidOperationException("CreateForWindow returned null.");
        nint hMonitor = 0;
        if (WindowEnumerator.GetExtendedFrameBounds(hwnd, out var bounds))
        {
            hMonitor = MonitorEnumerator.FromPoint(
                new System.Drawing.Point(bounds.Left + Math.Max(0, bounds.Width / 2),
                    bounds.Top + Math.Max(0, bounds.Height / 2)))?.Handle ?? 0;
        }
        return CaptureSingleFrame(item, CapturePixelFormatPolicy.ResolveForMonitor(hMonitor));
    }

    private Bitmap CaptureSingleFrame(GraphicsCaptureItem item, CapturePixelFormatDecision pixelFormat)
    {
        try
        {
            return CaptureSingleFrameCore(item, pixelFormat);
        }
        catch when (pixelFormat.UsesFp16)
        {
            // Some WGC/D3D combinations advertise advanced color but reject FP16
            // pools. Preserve capture availability by retrying the SDR path.
            return CaptureSingleFrameCore(item, CapturePixelFormatDecision.Sdr);
        }
    }

    private Bitmap CaptureSingleFrameCore(GraphicsCaptureItem item, CapturePixelFormatDecision pixelFormat)
    {
        if (_direct3DDevice is null) throw new InvalidOperationException("D3D device not initialised.");

        var size = item.Size;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Capture item has zero size.");

        var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _direct3DDevice,
            pixelFormat.WinRtPixelFormat,
            2,
            size);

        using var session = pool.CreateCaptureSession(item);
        TrySetBorderRequired(session, false);
        TrySetCursorCapture(session, IncludeCursor);
        TrySetIncludeSecondaryWindows(session, IncludeSecondaryWindows);

        using var ready = new ManualResetEventSlim(false);
        Direct3D11CaptureFrame? captured = null;
        Direct3D11CaptureFramePool localPool = pool; // capture for closure
        TypedEventHandler<Direct3D11CaptureFramePool, object> handler = (sender, _) =>
        {
            try
            {
                var f = sender.TryGetNextFrame();
                if (f is not null && captured is null)
                {
                    captured = f;
                    ready.Set();
                }
                else
                {
                    f?.Dispose();
                }
            }
            catch { ready.Set(); }
        };
        pool.FrameArrived += handler;
        session.StartCapture();

        if (!ready.Wait(TimeSpan.FromSeconds(2)))
        {
            pool.FrameArrived -= handler;
            pool.Dispose();
            throw new TimeoutException("WGC frame did not arrive within 2s.");
        }
        pool.FrameArrived -= handler;

        if (captured is null)
        {
            pool.Dispose();
            throw new InvalidOperationException("WGC returned no frame.");
        }

        try
        {
            return CopyFrameToBitmap(captured, size.Width, size.Height, pixelFormat);
        }
        finally
        {
            captured.Dispose();
            pool.Dispose();
        }
    }

    private unsafe Bitmap CopyFrameToBitmap(
        Direct3D11CaptureFrame frame,
        int width,
        int height,
        CapturePixelFormatDecision pixelFormat)
    {
        // Get ID3D11Texture2D from the frame's surface.
        nint surfacePtr = Marshal.GetIUnknownForObject(frame.Surface);
        nint texPtr;
        try
        {
            var iidAccess = typeof(D3D11Interop.IDirect3DDxgiInterfaceAccess).GUID;
            int hrQI = Marshal.QueryInterface(surfacePtr, in iidAccess, out nint accessPtr);
            if (hrQI < 0) Marshal.ThrowExceptionForHR(hrQI);
            try
            {
                var access = (D3D11Interop.IDirect3DDxgiInterfaceAccess)
                    Marshal.GetObjectForIUnknown(accessPtr);
                var iidTex = D3D11Interop.IID_ID3D11Texture2D;
                texPtr = access.GetInterface(ref iidTex);
            }
            finally
            {
                Marshal.Release(accessPtr);
            }
        }
        finally
        {
            Marshal.Release(surfacePtr);
        }

        try
        {
            // Build a CPU-readable staging texture, copy GPU → staging, map, copy out.
            var desc = new D3D11Interop.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = (uint)pixelFormat.DxgiFormat,
                SampleDesc = new D3D11Interop.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11Interop.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11Interop.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            // ID3D11Device::CreateTexture2D vtbl slot 5 (after IUnknown's 3 + 2 inherited)
            // Slot map for ID3D11Device:
            //  0..2   IUnknown (QI/AddRef/Release)
            //  3      CreateBuffer
            //  4      CreateTexture1D
            //  5      CreateTexture2D
            // Use a managed wrapper via direct vtable invocation.
            nint stagingTex = CreateTexture2D(_d3dDevice, ref desc);
            try
            {
                CopyResource(_d3dContext, stagingTex, texPtr);

                var mapped = MapResource(_d3dContext, stagingTex);
                try
                {
                    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                    var rect = new Rectangle(0, 0, width, height);
                    var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        if (pixelFormat.UsesFp16)
                        {
                            var source = new ReadOnlySpan<byte>(
                                (void*)mapped.pData,
                                checked((int)(mapped.RowPitch * (uint)height)));
                            var destination = new Span<byte>(
                                (void*)data.Scan0,
                                checked(data.Stride * height));
                            HdrFrameConverter.ConvertRgba16FloatToBgra(
                                source, checked((int)mapped.RowPitch), destination, data.Stride,
                                0, 0, width, height);
                        }
                        else
                        {
                            byte* src = (byte*)mapped.pData;
                            byte* dst = (byte*)data.Scan0;
                            int dstPitch = data.Stride;
                            int rowBytes = width * 4;
                            for (int y = 0; y < height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + y * (long)mapped.RowPitch,
                                    dst + y * (long)dstPitch,
                                    dstPitch,
                                    rowBytes);
                            }
                        }
                    }
                    finally { bmp.UnlockBits(data); }
                    return bmp;
                }
                finally { UnmapResource(_d3dContext, stagingTex); }
            }
            finally { Marshal.Release(stagingTex); }
        }
        finally { Marshal.Release(texPtr); }
    }

    // ---- D3D11 device + thin vtable callers ---------------------------------

    private void EnsureDevice()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WinRtCaptureEngine));
        if (_direct3DDevice is not null) return;
        lock (_deviceLock)
        {
            if (_direct3DDevice is not null) return;
            int hr = D3D11Interop.D3D11CreateDevice(
                pAdapter: 0,
                driverType: D3D11Interop.D3D_DRIVER_TYPE_HARDWARE,
                software: 0,
                flags: (uint)D3D11Interop.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                pFeatureLevels: 0,
                featureLevels: 0,
                sdkVersion: 7,
                ppDevice: out _d3dDevice,
                featureLevel: out _,
                ppImmediateContext: out _d3dContext);
            if (hr < 0)
            {
                // Fall back to WARP if no HW adapter is available
                hr = D3D11Interop.D3D11CreateDevice(0, D3D11Interop.D3D_DRIVER_TYPE_WARP, 0,
                    (uint)D3D11Interop.D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0, 7,
                    out _d3dDevice, out _, out _d3dContext);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            }

            // QI for IDXGIDevice
            var iidDxgi = D3D11Interop.IID_IDXGIDevice;
            int hrQI = Marshal.QueryInterface(_d3dDevice, in iidDxgi, out nint dxgiDevice);
            if (hrQI < 0) Marshal.ThrowExceptionForHR(hrQI);
            try
            {
                int hrCreate = D3D11Interop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint inspectable);
                if (hrCreate < 0) Marshal.ThrowExceptionForHR(hrCreate);
                try
                {
                    _direct3DDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
                }
                finally { Marshal.Release(inspectable); }
            }
            finally { Marshal.Release(dxgiDevice); }
        }
    }

    private static unsafe nint CreateTexture2D(nint device, ref D3D11Interop.D3D11_TEXTURE2D_DESC desc)
    {
        // ID3D11Device::CreateTexture2D — vtable slot 5
        var vtbl = *(nint**)device;
        var fn = (delegate* unmanaged[Stdcall]<nint, ref D3D11Interop.D3D11_TEXTURE2D_DESC, nint, out nint, int>)vtbl[5];
        int hr = fn(device, ref desc, 0, out nint tex);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return tex;
    }

    private static unsafe void CopyResource(nint context, nint dst, nint src)
    {
        // ID3D11DeviceContext::CopyResource — vtable slot 47
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint, void>)vtbl[47];
        fn(context, dst, src);
    }

    private static unsafe D3D11Interop.D3D11_MAPPED_SUBRESOURCE MapResource(nint context, nint resource)
    {
        // ID3D11DeviceContext::Map — vtable slot 14
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, out D3D11Interop.D3D11_MAPPED_SUBRESOURCE, int>)vtbl[14];
        int hr = fn(context, resource, 0, D3D11Interop.D3D11_MAP_READ, 0, out var mapped);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return mapped;
    }

    private static unsafe void UnmapResource(nint context, nint resource)
    {
        // ID3D11DeviceContext::Unmap — vtable slot 15
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)vtbl[15];
        fn(context, resource, 0);
    }

    private static void TrySetBorderRequired(GraphicsCaptureSession session, bool value)
    {
        try
        {
            if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "IsBorderRequired"))
                session.IsBorderRequired = value;
        }
        catch { /* OS < 22H2 */ }
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool value)
    {
        try
        {
            if (ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "IsCursorCaptureEnabled"))
            {
#pragma warning disable CA1416
                session.IsCursorCaptureEnabled = value;
#pragma warning restore CA1416
            }
        }
        catch { }
    }

    private static void TrySetIncludeSecondaryWindows(GraphicsCaptureSession session, bool value)
    {
        if (!value) return;
        try
        {
            if (!ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "IncludeSecondaryWindows"))
                return;
            var prop = session.GetType().GetProperty("IncludeSecondaryWindows");
            prop?.SetValue(session, true);
        }
        catch { }
    }

    private static unsafe bool BitmapIsEmpty(Bitmap bmp)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0) return true;
        // Sample 16 evenly-distributed pixels; if every channel is zero, treat as empty.
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte* p = (byte*)data.Scan0;
            for (int gy = 0; gy < 4; gy++)
            for (int gx = 0; gx < 4; gx++)
            {
                int x = (gx * (bmp.Width - 1)) / 3;
                int y = (gy * (bmp.Height - 1)) / 3;
                byte* px = p + y * data.Stride + x * 4;
                if (px[0] != 0 || px[1] != 0 || px[2] != 0) return false;
            }
            return true;
        }
        finally { bmp.UnlockBits(data); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_d3dContext != 0) Marshal.Release(_d3dContext);
        if (_d3dDevice != 0) Marshal.Release(_d3dDevice);
    }
}

/// <summary>Thrown when WGC returns a black surface for a window with WDA_EXCLUDEFROMCAPTURE.</summary>
public sealed class CaptureExcludedException : Exception
{
    public CaptureExcludedException(string message) : base(message) { }
}
