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
internal static class D3D11Interop
{
    public const int D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;
    public const int D3D_DRIVER_TYPE_HARDWARE = 1;
    public const int D3D_DRIVER_TYPE_WARP = 5;

    public const int D3D11_USAGE_STAGING = 3;
    public const int D3D11_CPU_ACCESS_READ = 0x20000;
    public const int D3D11_MAP_READ = 1;

    public const int DXGI_FORMAT_B8G8R8A8_UNORM = 87;
    public const int DXGI_FORMAT_R16G16B16A16_FLOAT = 10;

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
}
