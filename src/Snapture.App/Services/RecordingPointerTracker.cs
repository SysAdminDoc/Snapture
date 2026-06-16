using System.Drawing;
using System.Runtime.InteropServices;
using Serilog;

namespace Snapture.App.Services;

internal enum RecordingPointerButton
{
    Left,
    Right,
    Middle
}

internal readonly record struct RecordingPointerClick(
    Point ScreenPoint,
    RecordingPointerButton Button,
    DateTime TimestampUtc);

internal readonly record struct RecordingPointerEffect(
    Point Position,
    RecordingPointerButton Button,
    double AgeMilliseconds);

internal readonly record struct RecordingPointerFrame(
    Point? CursorPosition,
    IReadOnlyList<RecordingPointerEffect> Clicks);

internal sealed class RecordingPointerTracker : IDisposable
{
    private const int MaxRecentClicks = 64;
    private const double ClickLifetimeMilliseconds = CursorOverlayRenderer.ClickAnimationMilliseconds;

    private readonly object _lock = new();
    private readonly List<RecordingPointerClick> _recentClicks = new();

    private nint _hookHandle;
    private LowLevelMouseProc? _proc;
    private bool _disposed;

    public event Action<RecordingPointerClick>? PointerClicked;

    public bool IsRunning => _hookHandle != 0;

    public void Start()
    {
        ThrowIfDisposed();
        if (_hookHandle != 0) return;

        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        if (_hookHandle == 0)
            Log.Warning("VideoRecorder.PointerHookUnavailable {Error}", Marshal.GetLastPInvokeError());
    }

    public RecordingPointerFrame CaptureFrame(Rectangle captureBounds, DateTime nowUtc)
    {
        ThrowIfDisposed();

        Point? cursor = null;
        if (GetCursorPos(out var point))
            cursor = ToLocalPoint(new Point(point.x, point.y), captureBounds);

        List<RecordingPointerEffect> effects = new();
        lock (_lock)
        {
            for (int i = _recentClicks.Count - 1; i >= 0; i--)
            {
                var click = _recentClicks[i];
                double age = (nowUtc - click.TimestampUtc).TotalMilliseconds;
                if (age > ClickLifetimeMilliseconds || age < 0)
                {
                    _recentClicks.RemoveAt(i);
                    continue;
                }

                var local = ToLocalPoint(click.ScreenPoint, captureBounds);
                if (local is { } p)
                    effects.Add(new RecordingPointerEffect(p, click.Button, age));
            }
        }

        effects.Reverse();
        return new RecordingPointerFrame(cursor, effects);
    }

    public void ClearClicks()
    {
        lock (_lock)
        {
            _recentClicks.Clear();
        }
    }

    internal static Point? ToLocalPoint(Point screenPoint, Rectangle captureBounds)
    {
        if (!captureBounds.Contains(screenPoint))
            return null;

        return new Point(screenPoint.X - captureBounds.Left, screenPoint.Y - captureBounds.Top);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && TryGetButton(wParam.ToInt32(), out var button))
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var click = new RecordingPointerClick(
                new Point(info.pt.x, info.pt.y),
                button,
                DateTime.UtcNow);

            lock (_lock)
            {
                _recentClicks.Add(click);
                while (_recentClicks.Count > MaxRecentClicks)
                    _recentClicks.RemoveAt(0);
            }

            PointerClicked?.Invoke(click);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static bool TryGetButton(int message, out RecordingPointerButton button)
    {
        button = message switch
        {
            WM_LBUTTONDOWN => RecordingPointerButton.Left,
            WM_RBUTTONDOWN => RecordingPointerButton.Right,
            WM_MBUTTONDOWN => RecordingPointerButton.Middle,
            _ => default
        };

        return message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN;
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
        ClearClicks();
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
