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

    private double _scale = 1.0;
    private double _opacity = 1.0;
    private bool _clickThrough;
    private bool _borderVisible = true;
    private bool _shadowVisible = true;
    private MenuItem? _borderMenu;
    private MenuItem? _shadowMenu;
    private MenuItem? _clickThroughMenu;

    public PinWindow(BitmapSource image)
    {
        InitializeComponent();
        PinnedImage.Source = image;
        PinnedImage.Width = image.PixelWidth;
        PinnedImage.Height = image.PixelHeight;

        MouseLeftButtonDown += OnLeftDown;
        PreviewMouseWheel += OnWheel;
        KeyDown += OnKeyDown;
        ContextMenu = BuildMenu();
        ContextMenu.Opened += (_, _) => SyncMenuState();
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
            case Key.H when Keyboard.Modifiers == ModifierKeys.None: ToggleOtherPinsVisibility(); break;
            case Key.O when Keyboard.Modifiers == ModifierKeys.None: SoloThis(); break;
        }
    }

    private void ToggleClickThrough()
        => SetClickThrough(!_clickThrough);

    private void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE = -20;
        const int WS_EX_TRANSPARENT = 0x00000020;
        int ex = (int)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if (_clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, (nint)ex);
        if (_clickThroughMenu is not null) _clickThroughMenu.IsChecked = _clickThrough;
    }

    private void ToggleBorder()
        => SetBorderVisible(!_borderVisible);

    private void SetBorderVisible(bool visible)
    {
        _borderVisible = visible;
        FrameBorder.BorderThickness = _borderVisible ? new Thickness(2) : new Thickness(0);
        if (_borderMenu is not null) _borderMenu.IsChecked = _borderVisible;
    }

    private void ToggleShadow()
        => SetShadowVisible(!_shadowVisible);

    private void SetShadowVisible(bool visible)
    {
        _shadowVisible = visible;
        FrameBorder.Effect = _shadowVisible ? DropShadow : null;
        if (_shadowMenu is not null) _shadowMenu.IsChecked = _shadowVisible;
    }

    private void SetOpacity(double v)
    {
        _opacity = Math.Clamp(v, 0.2, 1.0);
        Opacity = _opacity;
    }

    private void ToggleOtherPinsVisibility()
    {
        var hiddenOtherExists = AllPins.Any(p => !ReferenceEquals(p, this) && !p.IsVisible);
        if (hiddenOtherExists)
        {
            foreach (var p in AllPins.ToArray()) p.Show();
            Activate();
            return;
        }

        foreach (var p in AllPins.ToArray())
            if (!ReferenceEquals(p, this))
                p.Hide();
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
        var copy = new MenuItem { Header = "Copy image" };
        copy.Click += (_, _) => Clipboard.SetImage((BitmapSource)PinnedImage.Source);
        menu.Items.Add(copy);

        var resetScale = new MenuItem { Header = "Actual size" };
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
            var mi = new MenuItem { Header = $"{p}% opacity" };
            mi.Click += (_, _) => SetOpacity(p / 100.0);
            opacityMenu.Items.Add(mi);
        }
        menu.Items.Add(opacityMenu);

        _borderMenu = new MenuItem { Header = "Show border", IsCheckable = true, IsChecked = _borderVisible, InputGestureText = "B" };
        _borderMenu.Click += (_, _) => SetBorderVisible(_borderMenu.IsChecked);
        menu.Items.Add(_borderMenu);

        _shadowMenu = new MenuItem { Header = "Show shadow", IsCheckable = true, IsChecked = _shadowVisible, InputGestureText = "S" };
        _shadowMenu.Click += (_, _) => SetShadowVisible(_shadowMenu.IsChecked);
        menu.Items.Add(_shadowMenu);

        _clickThroughMenu = new MenuItem { Header = "Let clicks pass through", IsCheckable = true, IsChecked = _clickThrough, InputGestureText = "Alt+click" };
        _clickThroughMenu.Click += (_, _) => SetClickThrough(_clickThroughMenu.IsChecked);
        menu.Items.Add(_clickThroughMenu);

        var solo = new MenuItem { Header = "Show only this pin", InputGestureText = "O" };
        solo.Click += (_, _) => SoloThis();
        menu.Items.Add(solo);

        var hideOthers = new MenuItem { Header = "Hide or restore other pins", InputGestureText = "H" };
        hideOthers.Click += (_, _) => ToggleOtherPinsVisibility();
        menu.Items.Add(hideOthers);

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Close pin", InputGestureText = "Esc" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        return menu;
    }

    private void SyncMenuState()
    {
        if (_borderMenu is not null) _borderMenu.IsChecked = _borderVisible;
        if (_shadowMenu is not null) _shadowMenu.IsChecked = _shadowVisible;
        if (_clickThroughMenu is not null) _clickThroughMenu.IsChecked = _clickThrough;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int nIndex, nint dwNewLong);
}
