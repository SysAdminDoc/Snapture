using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class PinWindow : Window
{
    private static readonly List<PinWindow> AllPins = new();
    private static readonly PinSelectionState<PinWindow> Selection = new();

    private double _scale = 1.0;
    private double _opacity = 1.0;
    private bool _clickThrough;
    private bool _borderVisible = true;
    private bool _shadowVisible = true;
    private double _dragAnchorLeft;
    private double _dragAnchorTop;
    private Dictionary<PinWindow, (double Left, double Top)>? _dragOrigins;
    private MenuItem? _borderMenu;
    private MenuItem? _shadowMenu;
    private MenuItem? _clickThroughMenu;
    private MenuItem? _selectionSummaryMenu;
    private MenuItem? _closeSelectedMenu;

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
        ContextMenuOpening += (_, _) =>
        {
            if (!Selection.Contains(this))
                SelectOnly(this);
        };
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            Selection.Remove(this);
            AllPins.Remove(this);
            RefreshSelectionVisuals();
        };
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
        UpdateSelectionVisual();
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

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            ToggleSelection(this);
            Focus();
            e.Handled = true;
            return;
        }

        if (!Selection.Contains(this) || Selection.Count <= 1)
            SelectOnly(this);

        var dragPins = Selection.TargetsFor(this).ToArray();
        _dragAnchorLeft = Left;
        _dragAnchorTop = Top;
        _dragOrigins = dragPins.ToDictionary(pin => pin, pin => (pin.Left, pin.Top));
        LocationChanged += OnDragLocationChanged;
        try { DragMove(); } catch { }
        finally
        {
            LocationChanged -= OnDragLocationChanged;
            _dragOrigins = null;
        }
        e.Handled = true;
    }

    private void OnDragLocationChanged(object? sender, EventArgs e)
    {
        if (_dragOrigins is null)
            return;

        var deltaX = Left - _dragAnchorLeft;
        var deltaY = Top - _dragAnchorTop;
        foreach (var (pin, origin) in _dragOrigins)
        {
            if (ReferenceEquals(pin, this))
                continue;
            pin.Left = origin.Left + deltaX;
            pin.Top = origin.Top + deltaY;
        }
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            var delta = e.Delta > 0 ? 0.05 : -0.05;
            foreach (var pin in Selection.TargetsFor(this))
                pin.SetOpacity(pin._opacity + delta);
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
            case Key.Delete: CloseSelectedPins(); break;
            case Key.A when Keyboard.Modifiers == ModifierKeys.Control: SelectAllPins(); break;
        }
    }

    private static void SelectOnly(PinWindow pin)
    {
        Selection.SelectOnly(pin);
        RefreshSelectionVisuals();
    }

    private static void ToggleSelection(PinWindow pin)
    {
        Selection.Toggle(pin);
        RefreshSelectionVisuals();
    }

    private static void SelectAllPins()
    {
        Selection.SelectAll(AllPins.ToArray());
        RefreshSelectionVisuals();
    }

    private static void ClearSelection()
    {
        Selection.Clear();
        RefreshSelectionVisuals();
    }

    private static void RefreshSelectionVisuals()
    {
        foreach (var pin in AllPins.ToArray())
            pin.UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        var selected = Selection.Contains(this);
        FrameBorder.BorderThickness = _borderVisible
            ? new Thickness(selected ? 3 : 2)
            : new Thickness(0);
        FrameBorder.BorderBrush = FindBrush(selected ? "AppAccent" : "AppBorderStrong")
            ?? FrameBorder.BorderBrush;
    }

    private static Brush? FindBrush(string key) =>
        Application.Current?.TryFindResource(key) as Brush;

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
        UpdateSelectionVisual();
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

    private void SetOpacityForSelection(double value)
    {
        foreach (var pin in Selection.TargetsFor(this))
            pin.SetOpacity(value);
    }

    private void CloseSelectedPins()
    {
        foreach (var pin in Selection.TargetsFor(this).ToArray())
            pin.Close();
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
            mi.Click += (_, _) => SetOpacityForSelection(p / 100.0);
            opacityMenu.Items.Add(mi);
        }
        menu.Items.Add(opacityMenu);

        var selectionMenu = new MenuItem { Header = "Selection" };
        _selectionSummaryMenu = new MenuItem { IsEnabled = false };
        selectionMenu.Items.Add(_selectionSummaryMenu);
        selectionMenu.Items.Add(new Separator());

        var selectOnly = new MenuItem { Header = "Select only this pin" };
        selectOnly.Click += (_, _) => SelectOnly(this);
        selectionMenu.Items.Add(selectOnly);

        var selectAll = new MenuItem { Header = "Select all pins", InputGestureText = "Ctrl+A" };
        selectAll.Click += (_, _) => SelectAllPins();
        selectionMenu.Items.Add(selectAll);

        var clear = new MenuItem { Header = "Clear selection" };
        clear.Click += (_, _) => ClearSelection();
        selectionMenu.Items.Add(clear);

        _closeSelectedMenu = new MenuItem { Header = "Close selected pins", InputGestureText = "Delete" };
        _closeSelectedMenu.Click += (_, _) => CloseSelectedPins();
        selectionMenu.Items.Add(_closeSelectedMenu);
        menu.Items.Add(selectionMenu);

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
        if (_selectionSummaryMenu is not null)
        {
            _selectionSummaryMenu.Header = Selection.Count switch
            {
                0 => "No pins selected",
                1 => "1 pin selected — drag to move it",
                var count => $"{count} pins selected — drag one to move together"
            };
        }
        if (_closeSelectedMenu is not null)
            _closeSelectedMenu.Header = Selection.Count > 1
                ? $"Close {Selection.Count} selected pins"
                : "Close selected pin";
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int nIndex, nint dwNewLong);
}
