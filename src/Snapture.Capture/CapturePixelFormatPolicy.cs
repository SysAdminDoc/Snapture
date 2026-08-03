using Windows.Graphics.DirectX;

namespace Snapture.Capture;

public enum CapturePixelFormat
{
    Bgra8,
    Rgba16Float
}

public readonly record struct CapturePixelFormatDecision(
    CapturePixelFormat PixelFormat,
    bool IsHdrSupported,
    uint OutputColorSpace)
{
    public static CapturePixelFormatDecision Sdr => new(
        CapturePixelFormat.Bgra8,
        IsHdrSupported: false,
        D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);

    public bool UsesFp16 => PixelFormat == CapturePixelFormat.Rgba16Float;

    public DirectXPixelFormat WinRtPixelFormat => UsesFp16
        ? DirectXPixelFormat.R16G16B16A16Float
        : DirectXPixelFormat.B8G8R8A8UIntNormalized;

    public int DxgiFormat => UsesFp16
        ? D3D11Interop.DXGI_FORMAT_R16G16B16A16_FLOAT
        : D3D11Interop.DXGI_FORMAT_B8G8R8A8_UNORM;

    public string Description => UsesFp16
        ? "FP16 HDR capture"
        : "BGRA8 SDR capture";
}

public static class CapturePixelFormatPolicy
{
    /// <summary>
    /// Returns FP16 only when the display capability probe and the exact HDR output
    /// color space both agree. Any unknown state remains on the proven BGRA8 path.
    /// </summary>
    public static CapturePixelFormatDecision Resolve(bool isHdrSupported, uint outputColorSpace)
    {
        bool useFp16 = isHdrSupported
            && outputColorSpace == D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
        return new(
            useFp16 ? CapturePixelFormat.Rgba16Float : CapturePixelFormat.Bgra8,
            isHdrSupported,
            outputColorSpace);
    }

    public static CapturePixelFormatDecision ResolveForMonitor(nint hMonitor)
    {
        if (!D3D11Interop.TryGetOutputDescription1(hMonitor, out var description))
            return CapturePixelFormatDecision.Sdr;

        // DXGI_OUTPUT_DESC1 is the per-monitor desktop equivalent of the
        // view-bound DisplayInformation advanced-color check. The color space is
        // used as both the current HDR capability signal and the required output gate.
        bool isHdrSupported = description.ColorSpace
            == D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
        return Resolve(isHdrSupported, description.ColorSpace);
    }
}
