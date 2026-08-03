using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>Current HDR luminance metadata for one DXGI output.</summary>
public readonly record struct HdrCalibrationInfo(
    string DeviceName,
    bool IsHdrOutput,
    float MaxLuminance,
    float MaxFullFrameLuminance)
{
    public bool IsSuspicious => IsHdrOutput && HdrCalibrationProbe.IsSuspicious(MaxLuminance);
}

/// <summary>
/// Reads the display metadata Windows uses for advanced-color output and identifies
/// common uncalibrated or placeholder peak-luminance values. The probe is read-only;
/// Windows remains the authority for changing HDR calibration.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class HdrCalibrationProbe
{
    /// <summary>Peak luminance below this value is unlikely to describe a calibrated HDR display.</summary>
    public const float SuspiciousMaxLuminanceNits = 100f;

    public static bool IsSuspicious(float maxLuminance)
        => !float.IsFinite(maxLuminance) || maxLuminance < SuspiciousMaxLuminanceNits;

    public static bool TryGetForMonitor(nint hMonitor, out HdrCalibrationInfo info)
    {
        info = default;
        if (!D3D11Interop.TryGetOutputDescription1(hMonitor, out var description))
            return false;

        string deviceName = GetDeviceName(description);
        bool isHdrOutput = description.ColorSpace
            == D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
        info = new HdrCalibrationInfo(
            deviceName,
            isHdrOutput,
            description.MaxLuminance,
            description.MaxFullFrameLuminance);
        return true;
    }

    public static IReadOnlyList<HdrCalibrationInfo> FindSuspiciousMonitors()
    {
        var suspicious = new List<HdrCalibrationInfo>();
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            if (TryGetForMonitor(monitor.Handle, out var info) && info.IsSuspicious)
                suspicious.Add(info);
        }
        return suspicious;
    }

    private static unsafe string GetDeviceName(D3D11Interop.DXGI_OUTPUT_DESC1 description)
    {
        char* name = description.DeviceName;
        return new string(name);
    }
}
