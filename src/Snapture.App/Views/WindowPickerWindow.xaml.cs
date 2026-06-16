using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class WindowPickerWindow : Window
{
    private nint _picked;
    private nint _hoverWindow;
    private readonly Rectangle _virtualBounds;
    private readonly DispatcherTimer _hoverTimer;

    public WindowPickerWindow()
    {
        InitializeComponent();
        _virtualBounds = MonitorEnumerator.GetVirtualScreen();
        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        DimRect.Width = _virtualBounds.Width;
        DimRect.Height = _virtualBounds.Height;

        // Don't activate — keep the target window's focus stack untouched.
        ShowActivated = false;
        Loaded += (_, _) =>
        {
            PositionHintBadge();
            // Make this window non-activating so menus / focus on the target window stay open.
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt32();
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
        };
        SizeChanged += (_, _) => PositionHintBadge();

        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnClick;
        MouseRightButtonDown += (_, _) => { _picked = 0; Close(); };

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _hoverTimer.Tick += (_, _) => UpdateHover();
        _hoverTimer.Start();
    }

    public nint PickWindowSync()
    {
        ShowDialog();
        return _picked;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _picked = 0; Close(); }
        else if (e.Key == Key.PageUp && _hoverWindow != 0)
        {
            // Walk up to the parent — the topmost ancestor.
            nint parent = GetAncestor(_hoverWindow, GA_PARENT);
            if (parent != 0 && parent != GetDesktopWindow())
            {
                _hoverWindow = parent;
                RefreshHighlight();
            }
        }
        else if (e.Key == Key.PageDown && _hoverWindow != 0)
        {
            // Walk down: pick the topmost child under the cursor.
            GetCursorPos(out var pt);
            nint child = WindowFromPoint(pt);
            if (child != 0 && child != _hoverWindow)
            {
                _hoverWindow = child;
                RefreshHighlight();
            }
        }
        else if (e.Key == Key.Enter && _hoverWindow != 0)
        {
            _picked = _hoverWindow;
            Close();
        }
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_hoverWindow != 0) { _picked = _hoverWindow; Close(); }
    }

    private void UpdateHover()
    {
        if (!IsLoaded) return;
        try
        {
            var pos = GetVirtualCursorPos();
            // We need the window UNDER our overlay. Hide ourselves briefly via WindowFromPoint
            // walking ancestor chain so we ignore the overlay.
            nint hwnd = WindowFromPointIgnoringSelf(pos);
            if (hwnd == 0 || hwnd == _hoverWindow) return;
            // Walk up to root (top-level)
            nint root = GetAncestor(hwnd, GA_ROOT);
            if (root == 0) root = hwnd;
            _hoverWindow = root;
            RefreshHighlight();
        }
        catch { /* swallow */ }
    }

    private void RefreshHighlight()
    {
        if (_hoverWindow == 0)
        {
            HighlightRect.Visibility = Visibility.Collapsed;
            TitleBadge.Visibility = Visibility.Collapsed;
            return;
        }
        if (!WindowEnumerator.GetExtendedFrameBounds(_hoverWindow, out var bounds))
            return;

        Canvas.SetLeft(HighlightRect, bounds.X - _virtualBounds.X);
        Canvas.SetTop(HighlightRect, bounds.Y - _virtualBounds.Y);
        HighlightRect.Width = bounds.Width;
        HighlightRect.Height = bounds.Height;
        HighlightRect.Visibility = Visibility.Visible;

        var title = GetWindowTitle(_hoverWindow);
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "Untitled window" : title;
        Canvas.SetLeft(TitleBadge, bounds.X - _virtualBounds.X + 8);
        Canvas.SetTop(TitleBadge, bounds.Y - _virtualBounds.Y + 8);
        TitleBadge.Visibility = Visibility.Visible;
    }

    private nint WindowFromPointIgnoringSelf(POINT pos)
    {
        // Hide our window from hit-testing by setting it transparent to clicks.
        // We've set WS_EX_NOACTIVATE/WS_EX_TOOLWINDOW already; also set WS_EX_TRANSPARENT
        // here by bumping the style.
        var self = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        int ex = GetWindowLongPtr(self, GWL_EXSTYLE).ToInt32();
        SetWindowLongPtr(self, GWL_EXSTYLE, (nint)(ex | WS_EX_TRANSPARENT));
        try { return WindowFromPoint(pos); }
        finally { SetWindowLongPtr(self, GWL_EXSTYLE, (nint)ex); }
    }

    private static POINT GetVirtualCursorPos()
    {
        GetCursorPos(out var pt);
        return pt;
    }

    private static string GetWindowTitle(nint hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new System.Text.StringBuilder(len + 1);
        _ = GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hoverTimer.Stop();
        base.OnClosed(e);
    }

    private void PositionHintBadge()
    {
        HintBadge.UpdateLayout();
        var badgeWidth = Math.Max(HintBadge.ActualWidth, 340);
        var badgeHeight = Math.Max(HintBadge.ActualHeight, 54);
        Canvas.SetLeft(HintBadge, Math.Max(16, (ActualWidth - badgeWidth) / 2));
        Canvas.SetTop(HintBadge, Math.Max(16, ActualHeight - badgeHeight - 24));
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int GA_PARENT = 1;
    private const int GA_ROOT = 2;

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(POINT pt);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, int gaFlags);
    [DllImport("user32.dll")] private static extern nint GetDesktopWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int nIndex, nint dwNewLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);
}
