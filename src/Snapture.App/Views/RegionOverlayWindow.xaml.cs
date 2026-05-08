using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class RegionOverlayWindow : Window
{
    public Int32Rect? SelectedVirtualRegion { get; private set; }

    private readonly Bitmap _frozenScreen;
    private readonly System.Drawing.Rectangle _virtualBounds;
    private System.Windows.Point _dragStart;
    private bool _isDragging;

    public RegionOverlayWindow(Bitmap frozenVirtualScreen, System.Drawing.Rectangle virtualBounds)
    {
        InitializeComponent();
        _frozenScreen = frozenVirtualScreen;
        _virtualBounds = virtualBounds;

        Left = virtualBounds.X;
        Top = virtualBounds.Y;
        Width = virtualBounds.Width;
        Height = virtualBounds.Height;

        BackgroundImage.Source = ToBitmapSource(_frozenScreen);
        BackgroundImage.Width = virtualBounds.Width;
        BackgroundImage.Height = virtualBounds.Height;

        DimRect.Width = virtualBounds.Width;
        DimRect.Height = virtualBounds.Height;

        Canvas.SetLeft(HintBadge, (virtualBounds.Width / 2) - 200);
        Canvas.SetTop(HintBadge, virtualBounds.Height - 60);

        Loaded += (_, _) => Activate();
        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SelectedVirtualRegion = null; Close(); }
        else if (e.Key == Key.Enter && SelectionRect.Visibility == Visibility.Visible)
        {
            CaptureSelection();
            Close();
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(OverlayCanvas);
        Canvas.SetLeft(SelectionRect, _dragStart.X);
        Canvas.SetTop(SelectionRect, _dragStart.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        HintBadge.Visibility = Visibility.Collapsed;
        OverlayCanvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var p = e.GetPosition(OverlayCanvas);
        double x = Math.Min(_dragStart.X, p.X);
        double y = Math.Min(_dragStart.Y, p.Y);
        double w = Math.Abs(p.X - _dragStart.X);
        double h = Math.Abs(p.Y - _dragStart.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        SizeText.Text = $"{(int)w} × {(int)h}";
        Canvas.SetLeft(SizeBadge, Math.Min(x + w + 8, ActualWidth - 90));
        Canvas.SetTop(SizeBadge, Math.Max(y - 26, 4));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        OverlayCanvas.ReleaseMouseCapture();
        if (SelectionRect.Width < 4 || SelectionRect.Height < 4)
        {
            SelectionRect.Visibility = Visibility.Collapsed;
            SizeBadge.Visibility = Visibility.Collapsed;
            HintBadge.Visibility = Visibility.Visible;
            return;
        }
        CaptureSelection();
        Close();
    }

    private void CaptureSelection()
    {
        int x = (int)Math.Round(Canvas.GetLeft(SelectionRect));
        int y = (int)Math.Round(Canvas.GetTop(SelectionRect));
        int w = (int)Math.Round(SelectionRect.Width);
        int h = (int)Math.Round(SelectionRect.Height);
        SelectedVirtualRegion = new Int32Rect(_virtualBounds.X + x, _virtualBounds.Y + y, w, h);
    }

    public System.Drawing.Rectangle? GetSelectedRegionAsRectangle()
        => SelectedVirtualRegion is null
            ? null
            : new System.Drawing.Rectangle(SelectedVirtualRegion.Value.X, SelectedVirtualRegion.Value.Y,
                SelectedVirtualRegion.Value.Width, SelectedVirtualRegion.Value.Height);

    private static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}
