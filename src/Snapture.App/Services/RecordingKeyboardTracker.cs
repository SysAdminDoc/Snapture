using System.Runtime.InteropServices;
using Serilog;

namespace Snapture.App.Services;

internal readonly record struct RecordingKeystrokeEffect(
    string Text,
    int RepeatCount,
    double AgeMilliseconds);

internal readonly record struct RecordingKeystrokeFrame(
    IReadOnlyList<RecordingKeystrokeEffect> Keystrokes)
{
    public bool HasVisualActivity => Keystrokes.Count > 0;
}

internal readonly record struct RecordingKeyPress(
    string Text,
    DateTime TimestampUtc);

internal sealed class RecordingKeyboardTracker : IDisposable
{
    private const int MaxRecentKeystrokes = 6;
    private const int MergeWindowMilliseconds = 900;

    private readonly object _lock = new();
    private readonly HashSet<int> _pressedKeys = new();
    private readonly List<TrackedKeystroke> _recentKeystrokes = new();

    private nint _hookHandle;
    private LowLevelKeyboardProc? _proc;
    private bool _disposed;

    public event Action<RecordingKeyPress>? KeyPressed;

    public bool IsRunning => _hookHandle != 0;

    public void Start()
    {
        ThrowIfDisposed();
        if (_hookHandle != 0) return;

        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hookHandle == 0)
            Log.Warning("VideoRecorder.KeyboardHookUnavailable {Error}", Marshal.GetLastPInvokeError());
    }

    public RecordingKeystrokeFrame CaptureFrame(DateTime nowUtc)
    {
        ThrowIfDisposed();

        List<RecordingKeystrokeEffect> effects = new();
        lock (_lock)
        {
            for (int i = _recentKeystrokes.Count - 1; i >= 0; i--)
            {
                var keystroke = _recentKeystrokes[i];
                double age = (nowUtc - keystroke.TimestampUtc).TotalMilliseconds;
                if (age > KeystrokeOverlayRenderer.DisplayMilliseconds || age < 0)
                {
                    _recentKeystrokes.RemoveAt(i);
                    continue;
                }

                effects.Add(new RecordingKeystrokeEffect(keystroke.Text, keystroke.RepeatCount, age));
            }
        }

        effects.Reverse();
        return new RecordingKeystrokeFrame(effects);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pressedKeys.Clear();
            _recentKeystrokes.Clear();
        }
    }

    internal static string? FormatKeyChord(int virtualKey, IReadOnlySet<int> pressedKeys)
    {
        if (IsModifier(virtualKey))
            return null;

        string? key = KeyName(virtualKey);
        if (key is null)
            return null;

        List<string> parts = new(5);
        if (ContainsAny(pressedKeys, VK_LWIN, VK_RWIN))
            parts.Add("WIN");
        if (ContainsAny(pressedKeys, VK_CONTROL, VK_LCONTROL, VK_RCONTROL))
            parts.Add("CTRL");
        if (ContainsAny(pressedKeys, VK_MENU, VK_LMENU, VK_RMENU))
            parts.Add("ALT");
        if (ContainsAny(pressedKeys, VK_SHIFT, VK_LSHIFT, VK_RSHIFT))
            parts.Add("SHIFT");

        parts.Add(key);
        return string.Join("+", parts);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            int message = wParam.ToInt32();
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            int virtualKey = checked((int)info.vkCode);

            if (message is WM_KEYDOWN or WM_SYSKEYDOWN)
                OnKeyDown(virtualKey);
            else if (message is WM_KEYUP or WM_SYSKEYUP)
                OnKeyUp(virtualKey);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private void OnKeyDown(int virtualKey)
    {
        RecordingKeyPress? keyPress = null;
        lock (_lock)
        {
            bool wasAlreadyDown = !_pressedKeys.Add(virtualKey);
            if (wasAlreadyDown || IsModifier(virtualKey))
                return;

            string? text = FormatKeyChord(virtualKey, _pressedKeys);
            if (text is null)
                return;

            DateTime now = DateTime.UtcNow;
            if (_recentKeystrokes.Count > 0)
            {
                var last = _recentKeystrokes[^1];
                if (last.Text == text && (now - last.TimestampUtc).TotalMilliseconds <= MergeWindowMilliseconds)
                {
                    _recentKeystrokes[^1] = last with
                    {
                        RepeatCount = last.RepeatCount + 1,
                        TimestampUtc = now
                    };
                }
                else
                {
                    _recentKeystrokes.Add(new TrackedKeystroke(text, 1, now));
                }
            }
            else
            {
                _recentKeystrokes.Add(new TrackedKeystroke(text, 1, now));
            }

            while (_recentKeystrokes.Count > MaxRecentKeystrokes)
                _recentKeystrokes.RemoveAt(0);
            keyPress = new RecordingKeyPress(text, now);
        }

        KeyPressed?.Invoke(keyPress.Value);
    }

    private void OnKeyUp(int virtualKey)
    {
        lock (_lock)
        {
            _pressedKeys.Remove(virtualKey);
        }
    }

    private static bool ContainsAny(IReadOnlySet<int> keys, params int[] candidates)
    {
        foreach (int candidate in candidates)
        {
            if (keys.Contains(candidate))
                return true;
        }

        return false;
    }

    private static bool IsModifier(int virtualKey)
        => virtualKey is VK_SHIFT or VK_LSHIFT or VK_RSHIFT
            or VK_CONTROL or VK_LCONTROL or VK_RCONTROL
            or VK_MENU or VK_LMENU or VK_RMENU
            or VK_LWIN or VK_RWIN;

    private static string? KeyName(int virtualKey)
    {
        if (virtualKey is >= 0x41 and <= 0x5A)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= 0x30 and <= 0x39)
            return ((char)virtualKey).ToString();
        if (virtualKey is >= VK_NUMPAD0 and <= VK_NUMPAD9)
            return $"NUM{virtualKey - VK_NUMPAD0}";
        if (virtualKey is >= VK_F1 and <= VK_F24)
            return $"F{virtualKey - VK_F1 + 1}";

        return virtualKey switch
        {
            VK_BACK => "BKSP",
            VK_TAB => "TAB",
            VK_RETURN => "ENTER",
            VK_ESCAPE => "ESC",
            VK_SPACE => "SPACE",
            VK_PRIOR => "PGUP",
            VK_NEXT => "PGDN",
            VK_END => "END",
            VK_HOME => "HOME",
            VK_LEFT => "LEFT",
            VK_UP => "UP",
            VK_RIGHT => "RIGHT",
            VK_DOWN => "DOWN",
            VK_INSERT => "INS",
            VK_DELETE => "DEL",
            VK_SNAPSHOT => "PRTSCR",
            VK_CAPITAL => "CAPS",
            VK_OEM_PLUS => "PLUS",
            VK_OEM_MINUS => "MINUS",
            VK_OEM_COMMA => "COMMA",
            VK_OEM_PERIOD => "PERIOD",
            VK_OEM_1 => "SEMI",
            VK_OEM_2 => "SLASH",
            VK_OEM_3 => "TILDE",
            VK_OEM_4 => "LBRACKET",
            VK_OEM_5 => "BACKSLASH",
            VK_OEM_6 => "RBRACKET",
            VK_OEM_7 => "QUOTE",
            _ => null
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hookHandle != 0)
            UnhookWindowsHookEx(_hookHandle);

        _hookHandle = 0;
        _proc = null;
        Clear();
    }

    private readonly record struct TrackedKeystroke(string Text, int RepeatCount, DateTime TimestampUtc);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_BACK = 0x08;
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_CAPITAL = 0x14;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_PRIOR = 0x21;
    private const int VK_NEXT = 0x22;
    private const int VK_END = 0x23;
    private const int VK_HOME = 0x24;
    private const int VK_LEFT = 0x25;
    private const int VK_UP = 0x26;
    private const int VK_RIGHT = 0x27;
    private const int VK_DOWN = 0x28;
    private const int VK_SNAPSHOT = 0x2C;
    private const int VK_INSERT = 0x2D;
    private const int VK_DELETE = 0x2E;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_NUMPAD0 = 0x60;
    private const int VK_NUMPAD9 = 0x69;
    private const int VK_F1 = 0x70;
    private const int VK_F24 = 0x87;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;
    private const int VK_OEM_1 = 0xBA;
    private const int VK_OEM_PLUS = 0xBB;
    private const int VK_OEM_COMMA = 0xBC;
    private const int VK_OEM_MINUS = 0xBD;
    private const int VK_OEM_PERIOD = 0xBE;
    private const int VK_OEM_2 = 0xBF;
    private const int VK_OEM_3 = 0xC0;
    private const int VK_OEM_4 = 0xDB;
    private const int VK_OEM_5 = 0xDC;
    private const int VK_OEM_6 = 0xDD;
    private const int VK_OEM_7 = 0xDE;

    private delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
