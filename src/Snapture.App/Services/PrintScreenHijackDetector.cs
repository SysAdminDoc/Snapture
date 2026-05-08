using Microsoft.Win32;

namespace Snapture.App.Services;

/// <summary>
/// Win11 24H2 quietly enables a setting that routes PrintScreen to the Snipping Tool by
/// default. When that flag is on, our PrintScreen hotkey will compete with the OS — so we
/// tell the user once and offer a one-click toggle to reclaim the key.
/// </summary>
public static class PrintScreenHijackDetector
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";

    public static bool IsHijacked()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (key is null) return false;
            object? raw = key.GetValue(ValueName);
            if (raw is null) return false;
            return Convert.ToInt32(raw) != 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Set the registry flag to <c>0</c> so PrintScreen returns to apps. Returns true on success.</summary>
    public static bool Reclaim()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(KeyPath);
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
