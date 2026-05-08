using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapture.App.Views;

public partial class PinWindow : Window
{
    private static readonly List<PinWindow> AllPins = new();
    private static bool _hidden;

    private double _scale = 1.0;
    private double _opacity = 1.0;
    private bool _clickThrough;
    private bool _borderVisible = true;
    private bool _shadowVisible = true;

    public PinWindow(BitmapSource image)
    {
        InitializeComponent();
        PinnedImage.Source = image;
        PinnedImage.Width = image.PixelWidth;
        PinnedImage.Height = image.PixelHeight;

        MouseLeftButtonDown += OnLeftDown;
        MouseRightButtonDown += (_, _) => Close();
        PreviewMouseWheel += OnWheel;
        KeyDown += OnKeyDown;
        ContextMenu = BuildMenu();
        Loaded += OnLoaded;
        Closed += (_, _) => AllPins.Remove(this);
        AllPins.Add(this);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Position near cursor on first show.
        try
        {
            var pt = System.Windows.Forms.Cursor.Position;
            Left = pt.X - 40;
            Top = pt.Y - 40;
        }
        catch { }
        Focus();
    }

    private void OnLeftDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            // Toggle click-through while Alt is held — handy when overlaying live UI.
            ToggleClickThrough();
            e.Handled = true;
            return;
        }
        try { DragMove(); } catch { }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _opacity = Math.Clamp(_opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1.0);
            Opacity = _opacity;
        }
        else
        {
            _scale = Math.Clamp(_scale + (e.Delta > 0 ? 0.1 : -0.1), 0.25, 4.0);
            var b = (BitmapSource)PinnedImage.Source;
            PinnedImage.Width = b.PixelWidth * _scale;
            PinnedImage.Height = b.PixelHeight * _scale;
        }
        e.Handled = true;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.B when Keyboard.Modifiers == ModifierKeys.None: ToggleBorder(); break;
            case Key.S when Keyboard.Modifiers == ModifierKeys.None: ToggleShadow(); break;
            case Key.H when Keyboard.Modifiers == ModifierKeys.None: ToggleAllVisibility(); break;
            case Key.O when Keyboard.Modifiers == ModifierKeys.None: SoloThis(); break;
        }
    }

    private void ToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        int ex = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if (_clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
    }

    private void ToggleBorder()
    {
        _borderVisible = !_borderVisible;
        FrameBorder.BorderThickness = _borderVisible ? new Thickness(2) : new Thickness(0);
    }

    private void ToggleShadow()
    {
        _shadowVisible = !_shadowVisible;
        FrameBorder.Effect = _shadowVisible ? DropShadow : null;
    }

    private void SetOpacity(double v)
    {
        _opacity = Math.Clamp(v, 0.2, 1.0);
        Opacity = _opacity;
    }

    private static void ToggleAllVisibility()
    {
        _hidden = !_hidden;
        foreach (var p in AllPins.ToArray())
        {
            if (_hidden) p.Hide(); else p.Show();
        }
    }

    private void SoloThis()
    {
        foreach (var p in AllPins)
            if (!ReferenceEquals(p, this)) p.Hide();
        Activate();
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => Clipboard.SetImage((BitmapSource)PinnedImage.Source);
        menu.Items.Add(copy);

        var resetScale = new MenuItem { Header = "Reset 100% (scroll-wheel zooms)" };
        resetScale.Click += (_, _) =>
        {
            _scale = 1.0;
            var b = (BitmapSource)PinnedImage.Source;
            PinnedImage.Width = b.PixelWidth;
            PinnedImage.Height = b.PixelHeight;
        };
        menu.Items.Add(resetScale);

        var opacityMenu = new MenuItem { Header = "Opacity" };
        foreach (var pct in new[] { 25, 50, 75, 100 })
        {
            int p = pct;
            var mi = new MenuItem { Header = $"{p}%" };
            mi.Click += (_, _) => SetOpacity(p / 100.0);
            opacityMenu.Items.Add(mi);
        }
        menu.Items.Add(opacityMenu);

        var border = new MenuItem { Header = "Toggle border (B)" };
        border.Click += (_, _) => ToggleBorder();
        menu.Items.Add(border);

        var shadow = new MenuItem { Header = "Toggle shadow (S)" };
        shadow.Click += (_, _) => ToggleShadow();
        menu.Items.Add(shadow);

        var clickThrough = new MenuItem { Header = "Click-through (Alt+click toggles)" };
        clickThrough.Click += (_, _) => ToggleClickThrough();
        menu.Items.Add(clickThrough);

        var solo = new MenuItem { Header = "Solo this pin (O)" };
        solo.Click += (_, _) => SoloThis();
        menu.Items.Add(solo);

        var hideAll = new MenuItem { Header = "Hide / show all pins (H)" };
        hideAll.Click += (_, _) => ToggleAllVisibility();
        menu.Items.Add(hideAll);

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Close (Esc)" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        return menu;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int nIndex, nint dwNewLong);
}
