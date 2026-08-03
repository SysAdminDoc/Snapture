using System.Buffers.Binary;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Snapture.Capture;

/// <summary>ICC bytes associated with one Windows display output.</summary>
public sealed record DisplayColorProfile(
    string DeviceName,
    string ProfilePath,
    byte[] Data);

/// <summary>
/// Resolves the active display profile without changing system color management.
/// DXGI identifies the exact output and its advanced-color state; WCS supplies the
/// profile path, with the legacy display DC as a compatibility fallback.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class DisplayColorProfileProbe
{
    private const uint WcsProfileManagementScopeSystemWide = 0;
    private const uint WcsProfileManagementScopeCurrentUser = 1;
    private const uint ColorProfileTypeIcc = 0;
    private const uint ColorProfileSubtypeNone = 4;
    private const uint ColorProfileSubtypeStandardDisplay = 7;
    private const uint ColorProfileSubtypeExtendedDisplay = 8;
    private const int MaximumProfileBytes = 16 * 1024 * 1024;

    public static bool TryGetForMonitor(nint hMonitor, out DisplayColorProfile profile)
    {
        profile = null!;
        if (!D3D11Interop.TryGetOutputDescription1(hMonitor, out var description))
            return false;

        string deviceName = D3D11Interop.GetOutputDeviceName(description);
        bool isAdvancedColor = description.ColorSpace
            == D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
        string? profilePath = TryGetWcsProfilePath(deviceName, isAdvancedColor)
            ?? TryGetIcmProfilePath(deviceName);
        if (string.IsNullOrWhiteSpace(profilePath))
            return false;

        string? resolvedPath = ResolveProfilePath(profilePath);
        if (resolvedPath is null || !TryReadIcc(resolvedPath, out var bytes))
            return false;

        profile = new DisplayColorProfile(deviceName, resolvedPath, bytes);
        return true;
    }

    /// <summary>
    /// Gets a profile only when the capture bounds fit wholly on one monitor. A
    /// multi-monitor composite has no single correct ICC profile and is left untagged.
    /// </summary>
    public static bool TryGetForBounds(Rectangle bounds, out DisplayColorProfile profile)
    {
        profile = null!;
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        var matches = MonitorEnumerator.Enumerate()
            .Where(monitor => monitor.Bounds.Contains(bounds))
            .ToList();
        return matches.Count == 1 && TryGetForMonitor(matches[0].Handle, out profile);
    }

    public static bool IsIccProfile(ReadOnlySpan<byte> data)
    {
        if (data.Length < 128 || data.Length > MaximumProfileBytes)
            return false;
        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(0, sizeof(uint)));
        return declaredSize >= 128
            && declaredSize <= data.Length
            && data.Slice(36, 4).SequenceEqual("acsp"u8);
    }

    private static string? TryGetWcsProfilePath(string deviceName, bool isAdvancedColor)
    {
        uint[] subtypes = isAdvancedColor
            ? new[] { ColorProfileSubtypeExtendedDisplay, ColorProfileSubtypeStandardDisplay, ColorProfileSubtypeNone }
            : new[] { ColorProfileSubtypeStandardDisplay, ColorProfileSubtypeNone };
        uint[] scopes = { WcsProfileManagementScopeCurrentUser, WcsProfileManagementScopeSystemWide };

        foreach (uint scope in scopes)
        foreach (uint subtype in subtypes)
        {
            try
            {
                if (!WcsGetDefaultColorProfileSize(
                        scope, deviceName, ColorProfileTypeIcc, subtype, 0, out uint byteCount)
                    || byteCount < sizeof(char)
                    || byteCount > 64 * 1024)
                    continue;

                var path = new StringBuilder(checked((int)(byteCount / sizeof(char)) + 1));
                if (WcsGetDefaultColorProfile(
                        scope, deviceName, ColorProfileTypeIcc, subtype, 0, byteCount, path)
                    && path.Length > 0)
                    return path.ToString();
            }
            catch
            {
                // A missing WCS association is normal; try the next scope/subtype.
            }
        }

        return null;
    }

    private static string? TryGetIcmProfilePath(string deviceName)
    {
        nint dc = 0;
        try
        {
            dc = CreateDC("DISPLAY", deviceName, null, 0);
            if (dc == 0) return null;

            uint characterCount = 0;
            _ = GetICMProfile(dc, ref characterCount, null);
            if (characterCount == 0 || characterCount > 32 * 1024)
                return null;

            var path = new StringBuilder(checked((int)characterCount + 1));
            return GetICMProfile(dc, ref characterCount, path) && path.Length > 0
                ? path.ToString()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dc != 0) DeleteDC(dc);
        }
    }

    private static string? ResolveProfilePath(string path)
    {
        try
        {
            if (File.Exists(path)) return Path.GetFullPath(path);
            if (Path.IsPathRooted(path)) return null;

            string colorDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32", "spool", "drivers", "color");
            string candidate = Path.Combine(colorDirectory, path);
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryReadIcc(string path, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 128 || info.Length > MaximumProfileBytes)
                return false;

            bytes = File.ReadAllBytes(path);
            return IsIccProfile(bytes)
                && BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(0, sizeof(uint))) <= bytes.Length;
        }
        catch
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WcsGetDefaultColorProfileSize(
        uint scope,
        string deviceName,
        uint profileType,
        uint profileSubtype,
        uint profileId,
        out uint profileNameSize);

    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WcsGetDefaultColorProfile(
        uint scope,
        string deviceName,
        uint profileType,
        uint profileSubtype,
        uint profileId,
        uint profileNameSize,
        StringBuilder profileName);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateDC(
        string driver,
        string device,
        string? output,
        nint initData);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetICMProfileW", SetLastError = true)]
    private static extern bool GetICMProfile(
        nint hdc,
        ref uint profileNameSize,
        StringBuilder? profileName);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteDC(nint hdc);
}
