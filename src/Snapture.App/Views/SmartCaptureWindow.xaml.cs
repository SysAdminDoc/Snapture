using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class SmartCaptureWindow : Window
{
    private readonly Rectangle _virtualBounds;
    private readonly DispatcherTimer _hoverTimer;
    private AutomationElement? _hoverElement;
    private AutomationElement? _lockedElement; // when user PgUps the manual stack
    private System.Drawing.Rectangle _selectedBounds;

    public System.Drawing.Rectangle? SelectedBounds => _selectedBounds.IsEmpty ? null : _selectedBounds;
    public string? SelectedDescription { get; private set; }

    public SmartCaptureWindow()
    {
        InitializeComponent();
        _virtualBounds = MonitorEnumerator.GetVirtualScreen();
        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        DimRect.Width = _virtualBounds.Width;
        DimRect.Height = _virtualBounds.Height;
        Canvas.SetLeft(HintBadge, (_virtualBounds.Width / 2) - 280);
        Canvas.SetTop(HintBadge, _virtualBounds.Height - 60);

        ShowActivated = false;

        Loaded += OnLoaded;
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnLeftClick;
        MouseRightButtonDown += (_, _) => { _selectedBounds = Rectangle.Empty; Close(); };

        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _hoverTimer.Tick += (_, _) => RefreshHover();
        _hoverTimer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_NOACTIVATE = 0x08000000;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        int ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt32();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
    }

    public bool PickSync()
    {
        var ok = ShowDialog();
        return SelectedBounds is not null;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _selectedBounds = Rectangle.Empty; Close(); return; }
        if (e.Key == Key.PageUp && _lockedElement is not null)
        {
            // Climb to parent in the raw view.
            try
            {
                var parent = TreeWalker.RawViewWalker.GetParent(_lockedElement);
                if (parent is not null && parent != AutomationElement.RootElement)
                {
                    _lockedElement = parent;
                    PaintFromElement(parent);
                }
            }
            catch { }
        }
        else if (e.Key == Key.PageDown)
        {
            // Reset to the leaf under the cursor.
            _lockedElement = null;
        }
        else if (e.Key == Key.Enter && _lockedElement is not null)
        {
            CommitSelection(_lockedElement);
        }
    }

    private void OnLeftClick(object sender, MouseButtonEventArgs e)
    {
        var target = _lockedElement ?? _hoverElement;
        if (target is not null) CommitSelection(target);
    }

    private void RefreshHover()
    {
        if (_lockedElement is not null) return; // user is in manual-walk mode
        try
        {
            GetCursorPos(out var pt);
            var pickAt = HideAndQuery(pt);
            if (pickAt is null) return;
            if (ReferenceEquals(pickAt, _hoverElement)) return;
            _hoverElement = pickAt;
            PaintFromElement(pickAt);
        }
        catch { /* swallow transient UIA errors */ }
    }

    /// <summary>
    /// Briefly turn this overlay click-through so <see cref="AutomationElement.FromPoint"/>
    /// returns the underlying app's element rather than our own canvas.
    /// </summary>
    private AutomationElement? HideAndQuery(POINT pt)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        int ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt32();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)(ex | WS_EX_TRANSPARENT));
        try
        {
            return AutomationElement.FromPoint(new System.Windows.Point(pt.x, pt.y));
        }
        finally
        {
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        }
    }

    private void PaintFromElement(AutomationElement el)
    {
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty || double.IsNaN(r.Width) || r.Width < 1 || r.Height < 1)
            {
                HighlightRect.Visibility = Visibility.Collapsed;
                DescBadge.Visibility = Visibility.Collapsed;
                return;
            }
            Canvas.SetLeft(HighlightRect, r.X - _virtualBounds.X);
            Canvas.SetTop(HighlightRect, r.Y - _virtualBounds.Y);
            HighlightRect.Width = r.Width;
            HighlightRect.Height = r.Height;
            HighlightRect.Visibility = Visibility.Visible;

            string controlType = "Element";
            string elName = "";
            try { controlType = el.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "Element"; } catch { }
            try { elName = el.Current.Name ?? ""; } catch { }

            ElementType.Text = controlType + (_lockedElement is not null ? "  (locked — PgUp climb, PgDn release)" : "");
            ElementName.Text = string.IsNullOrEmpty(elName) ? "(no name)" : elName;
            ElementDims.Text = $"{(int)r.Width}×{(int)r.Height}  @  {(int)r.X},{(int)r.Y}";

            double bx = r.X - _virtualBounds.X + 8;
            double by = r.Y - _virtualBounds.Y + 8;
            if (bx + 540 > ActualWidth) bx = Math.Max(8, r.X - _virtualBounds.X + r.Width - 540);
            if (by + 60 > ActualHeight) by = Math.Max(8, r.Y - _virtualBounds.Y - 60);
            Canvas.SetLeft(DescBadge, bx);
            Canvas.SetTop(DescBadge, by);
            DescBadge.Visibility = Visibility.Visible;
        }
        catch
        {
            HighlightRect.Visibility = Visibility.Collapsed;
            DescBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void CommitSelection(AutomationElement el)
    {
        try
        {
            var r = el.Current.BoundingRectangle;
            if (r.IsEmpty) return;
            _selectedBounds = new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);
            string controlType = "Element";
            string elName = "";
            try { controlType = el.Current.ControlType?.ProgrammaticName?.Replace("ControlType.", "") ?? "Element"; } catch { }
            try { elName = el.Current.Name ?? ""; } catch { }
            SelectedDescription = string.IsNullOrEmpty(elName) ? controlType : $"{controlType}: {elName}";
            DialogResult = true;
            Close();
        }
        catch { /* swallow */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        _hoverTimer.Stop();
        base.OnClosed(e);
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x; public int y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int nIndex, nint dwNewLong);
}
