using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapture.Capture;
using DrawingPoint = System.Drawing.Point;

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
        var p = e.GetPosition(OverlayCanvas);
        UpdateLoupe(p);

        if (!_isDragging) return;
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

    private const int LoupeRegion = 20; // 20×20 source pixels
    private const double LoupeZoom = 6.0;

    private void UpdateLoupe(System.Windows.Point p)
    {
        try
        {
            int sx = (int)p.X - LoupeRegion / 2;
            int sy = (int)p.Y - LoupeRegion / 2;
            sx = Math.Clamp(sx, 0, _frozenScreen.Width - LoupeRegion);
            sy = Math.Clamp(sy, 0, _frozenScreen.Height - LoupeRegion);

            using var crop = new Bitmap(LoupeRegion, LoupeRegion, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop))
            {
                g.DrawImage(_frozenScreen,
                    new System.Drawing.Rectangle(0, 0, LoupeRegion, LoupeRegion),
                    new System.Drawing.Rectangle(sx, sy, LoupeRegion, LoupeRegion),
                    GraphicsUnit.Pixel);
            }
            // Render scaled
            int outSize = (int)(LoupeRegion * LoupeZoom);
            using var scaled = new Bitmap(outSize, outSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(crop, 0, 0, outSize, outSize);
            }
            LoupeImage.Source = ToBitmapSource(scaled);
            int cx = (int)p.X, cy = (int)p.Y;
            cx = Math.Clamp(cx, 0, _frozenScreen.Width - 1);
            cy = Math.Clamp(cy, 0, _frozenScreen.Height - 1);
            var px = _frozenScreen.GetPixel(cx, cy);
            LoupeText.Text = $"{cx},{cy} #{px.R:X2}{px.G:X2}{px.B:X2}";

            double lx = p.X + 24;
            double ly = p.Y + 24;
            if (lx + 130 > ActualWidth) lx = p.X - 144;
            if (ly + 130 > ActualHeight) ly = p.Y - 144;
            Canvas.SetLeft(LoupeBorder, lx);
            Canvas.SetTop(LoupeBorder, ly);
            LoupeBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            LoupeBorder.Visibility = Visibility.Collapsed;
        }
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
