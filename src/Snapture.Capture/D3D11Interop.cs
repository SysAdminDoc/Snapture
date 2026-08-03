using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>
/// Hand-rolled COM interop bridging Direct3D 11 (Win32) to <c>IDirect3DDevice</c> (WinRT).
/// Saves a couple of NuGet dependencies. The pieces we need:
///   1. <c>D3D11CreateDevice</c> from D3D11.dll.
///   2. <c>CreateDirect3D11DeviceFromDXGIDevice</c> from D3D11.dll — yields an
///      <c>IInspectable</c> we hand to WinRT as <c>IDirect3DDevice</c>.
///   3. <c>IDirect3DDxgiInterfaceAccess</c> — pulled from a <c>Direct3DSurface</c>
///      to get back to an <c>ID3D11Texture2D</c>.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class D3D11Interop
{
    public const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;

    public const int D3D11_USAGE_STAGING = 3;
    public const int D3D11_CPU_ACCESS_READ = 0x20000;
    public const int D3D11_MAP_READ = 1;

    public const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const int DXGI_FORMAT_R16G16B16A16_FLOAT = 10;
    public const uint DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709 = 0;
    public const uint DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020 = 12;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);

    private static readonly Guid IID_IDXGIFactory1 = new("770AAE78-F26F-4DBA-A829-253C83D1B387");
    private static readonly Guid IID_IDXGIOutput6 = new("068346E8-AAEC-4B84-ADD7-137F513F77A1");

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC { public uint Count; public uint Quality; }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>
    /// Native layout returned by IDXGIOutput6::GetDesc1. The fixed device-name
    /// buffer is retained so the fields after it stay at their documented offsets.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct DXGI_OUTPUT_DESC1
    {
        public fixed char DeviceName[32];
        public DXGI_RECT DesktopCoordinates;
        public int AttachedToDesktop;
        public int Rotation;
        public nint Monitor;
        public uint BitsPerColor;
        public uint ColorSpace;
        public float RedPrimaryX;
        public float RedPrimaryY;
        public float GreenPrimaryX;
        public float GreenPrimaryY;
        public float BluePrimaryX;
        public float BluePrimaryY;
        public float WhitePointX;
        public float WhitePointY;
        public float MinLuminance;
        public float MaxLuminance;
        public float MaxFullFrameLuminance;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_MAPPED_SUBRESOURCE
    {
        public nint pData;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", ExactSpelling = true)]
    public static extern int D3D11CreateDevice(
        nint pAdapter, int driverType, nint software, uint flags,
        nint pFeatureLevels, uint featureLevels, uint sdkVersion,
        out nint ppDevice, out int featureLevel, out nint ppImmediateContext);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    public static extern int CreateDirect3D11DeviceFromDXGIDevice(
        nint dxgiDevice, out nint graphicsDevice);

    /// <summary>Pulled off any Direct3DSurface to reach the underlying ID3D11Texture2D.</summary>
    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(ref Guid iid);
    }

    public static readonly Guid IID_ID3D11Resource = new("DC8E63F3-D12B-4952-B47B-5E45026A862D");
    public static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    public static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    /// <summary>Releases a raw COM pointer (no-op on null).</summary>
    [DllImport("ole32.dll", EntryPoint = "CoTaskMemFree")]
    public static extern void CoTaskMemFree(nint p);

    /// <summary>
    /// Finds the DXGI output for an HMONITOR and reads its current advanced-color
    /// description. This is deliberately queried per monitor: DisplayInformation's
    /// UWP GetForCurrentView API is tied to an app view and cannot identify a tray or
    /// worker-thread capture target in this WPF desktop app.
    /// </summary>
    [SupportedOSPlatform("windows10.0.17763.0")]
    public static unsafe bool TryGetOutputDescription1(nint hMonitor, out DXGI_OUTPUT_DESC1 description)
    {
        description = default;
        if (hMonitor == 0 || !OperatingSystem.IsWindows()) return false;

        nint factory = 0;
        try
        {
            var iidFactory = IID_IDXGIFactory1;
            int hr = CreateDXGIFactory1(in iidFactory, out factory);
            if (hr < 0 || factory == 0) return false;

            var factoryVtbl = *(nint**)factory;
            var enumAdapters = (delegate* unmanaged[Stdcall]<nint, uint, out nint, int>)factoryVtbl[12];
            for (uint adapterIndex = 0; ; adapterIndex++)
            {
                hr = enumAdapters(factory, adapterIndex, out nint adapter);
                if (hr == DXGI_ERROR_NOT_FOUND) break;
                if (hr < 0 || adapter == 0) continue;

                try
                {
                    var adapterVtbl = *(nint**)adapter;
                    var enumOutputs = (delegate* unmanaged[Stdcall]<nint, uint, out nint, int>)adapterVtbl[7];
                    for (uint outputIndex = 0; ; outputIndex++)
                    {
                        hr = enumOutputs(adapter, outputIndex, out nint output);
                        if (hr == DXGI_ERROR_NOT_FOUND) break;
                        if (hr < 0 || output == 0) continue;

                        try
                        {
                            int hrQI = Marshal.QueryInterface(output, in IID_IDXGIOutput6, out nint output6);
                            if (hrQI < 0 || output6 == 0) continue;

                            try
                            {
                                // IDXGIOutput6 inherits five output interfaces. The
                                // generated Windows bindings dispatch GetDesc1 at 27.
                                var outputVtbl = *(nint**)output6;
                                var getDesc1 = (delegate* unmanaged[Stdcall]<nint, out DXGI_OUTPUT_DESC1, int>)outputVtbl[27];
                                hr = getDesc1(output6, out var candidate);
                                if (hr >= 0 && candidate.Monitor == hMonitor)
                                {
                                    description = candidate;
                                    return true;
                                }
                            }
                            finally { Marshal.Release(output6); }
                        }
                        finally { Marshal.Release(output); }
                    }
                }
                finally { Marshal.Release(adapter); }
            }
        }
        catch
        {
            description = default;
            return false;
        }
        finally
        {
            if (factory != 0) Marshal.Release(factory);
        }

        return false;
    }

    [DllImport("dxgi.dll", EntryPoint = "CreateDXGIFactory1", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(in Guid riid, out nint ppFactory);
}
