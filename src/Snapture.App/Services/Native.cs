using System.Runtime.InteropServices;

namespace Snapture.App.Services;

internal static class Native
{
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_NONE = 0x0000;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint VK_SNAPSHOT = 0x2C; // PrintScreen

    /// <summary>
    /// Maps a key-name string (matching <see cref="System.Windows.Input.Key"/>'s ToString) to a Win32 VK.
    /// Falls back to PrintScreen so a malformed setting doesn't drop the hotkey entirely.
    /// </summary>
    public static uint NameToVirtualKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName)) return VK_SNAPSHOT;
        if (keyName.Equals("PrintScreen", StringComparison.OrdinalIgnoreCase)) return VK_SNAPSHOT;
        if (System.Enum.TryParse<System.Windows.Input.Key>(keyName, ignoreCase: true, out var key))
        {
            int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
            if (vk != 0) return (uint)vk;
        }
        return VK_SNAPSHOT;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
