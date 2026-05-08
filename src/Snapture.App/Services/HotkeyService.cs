using System.Windows.Interop;

namespace Snapture.App.Services;

public sealed class HotkeyService : IDisposable
{
    private readonly Dictionary<int, Action> _handlers = new();
    private HwndSource? _source;
    private nint _hwnd;
    private int _nextId = 0x9000;

    public void Initialize()
    {
        var parameters = new HwndSourceParameters("SnaptureHotkeyHost")
        {
            Width = 0,
            Height = 0,
            ParentWindow = -3, // HWND_MESSAGE
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        _hwnd = _source.Handle;
    }

    public int Register(uint modifiers, uint virtualKey, Action handler)
    {
        if (_source is null) Initialize();
        int id = System.Threading.Interlocked.Increment(ref _nextId);
        if (!Native.RegisterHotKey(_hwnd, id, modifiers | Native.MOD_NOREPEAT, virtualKey))
            throw new InvalidOperationException($"RegisterHotKey failed for {modifiers}+{virtualKey}.");
        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
            Native.UnregisterHotKey(_hwnd, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys.ToList())
            Native.UnregisterHotKey(_hwnd, id);
        _handlers.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            try { h(); } catch { /* swallow */ }
        }
        return 0;
    }

    public void Dispose()
    {
        foreach (var id in _handlers.Keys.ToList()) Unregister(id);
        _source?.Dispose();
        _source = null;
    }
}
