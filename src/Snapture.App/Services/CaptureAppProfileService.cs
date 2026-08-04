using System.Runtime.InteropServices;
using System.Text;

namespace Snapture.App.Services;

/// <summary>Maps a Win32 window class name to one of Snapture's built-in capture presets.</summary>
public sealed record CaptureAppProfile(string WindowClassName, string PresetKey);

public static class CaptureAppProfileService
{
    private const int MaxClassNameLength = 256;

    public static CaptureAppProfile? Find(
        string? windowClassName,
        IEnumerable<CaptureAppProfile>? profiles)
    {
        var normalizedClass = NormalizeClassName(windowClassName);
        if (normalizedClass is null || profiles is null)
            return null;

        foreach (var profile in profiles)
        {
            if (profile is null)
                continue;

            var configuredClass = NormalizeClassName(profile.WindowClassName);
            if (!string.Equals(
                    normalizedClass,
                    configuredClass,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            var preset = CapturePresetService.Find(profile.PresetKey);
            if (preset is not null && preset.Key != CapturePresetService.CustomKey)
                return new CaptureAppProfile(configuredClass!, preset.Key);
        }

        return null;
    }

    public static bool ApplyForClass(
        string? windowClassName,
        SnaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var profile = Find(windowClassName, settings.PerAppCaptureProfiles);
        return profile is not null && CapturePresetService.Apply(profile.PresetKey, settings);
    }

    public static bool ApplyForWindow(nint hwnd, SnaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return hwnd != 0 && ApplyForClass(GetWindowClassName(hwnd), settings);
    }

    public static string? GetWindowClassName(nint hwnd)
    {
        if (hwnd == 0)
            return null;

        var buffer = new StringBuilder(MaxClassNameLength);
        int length = GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : null;
    }

    public static bool IsValidClassName(string? value) => NormalizeClassName(value) is not null;

    public static IReadOnlyList<CaptureAppProfile> Normalize(
        IEnumerable<CaptureAppProfile>? profiles)
    {
        if (profiles is null)
            return Array.Empty<CaptureAppProfile>();

        var result = new List<CaptureAppProfile>();
        foreach (var profile in profiles)
        {
            if (profile is null)
                continue;

            var className = NormalizeClassName(profile.WindowClassName);
            var preset = CapturePresetService.Find(profile.PresetKey);
            if (className is null || preset is null || preset.Key == CapturePresetService.CustomKey)
                continue;

            int existing = result.FindIndex(item => string.Equals(
                item.WindowClassName, className, StringComparison.OrdinalIgnoreCase));
            var normalized = new CaptureAppProfile(className, preset.Key);
            if (existing >= 0)
                result[existing] = normalized with
                {
                    WindowClassName = result[existing].WindowClassName
                };
            else
                result.Add(normalized);
        }

        return result;
    }

    private static string? NormalizeClassName(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length >= MaxClassNameLength)
            return null;
        foreach (char c in trimmed)
        {
            if (char.IsControl(c))
                return null;
        }
        return trimmed;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);
}
