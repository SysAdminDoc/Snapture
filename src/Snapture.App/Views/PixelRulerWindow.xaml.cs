using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class PixelRulerWindow : Window
{
    private readonly Rectangle _virtualBounds;
    private System.Windows.Point _start;
    private bool _dragging;

    public PixelRulerWindow()
    {
        InitializeComponent();
        _virtualBounds = MonitorEnumerator.GetVirtualScreen();
        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        DimRect.Width = _virtualBounds.Width;
        DimRect.Height = _virtualBounds.Height;
        Canvas.SetLeft(HintBadge, (_virtualBounds.Width / 2) - 220);
        Canvas.SetTop(HintBadge, _virtualBounds.Height - 60);

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Loaded += (_, _) => Activate();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(OverlayCanvas);
        MeasureLine.X1 = _start.X; MeasureLine.Y1 = _start.Y;
        MeasureLine.X2 = _start.X; MeasureLine.Y2 = _start.Y;
        MeasureLine.Visibility = Visibility.Visible;
        ReadoutBadge.Visibility = Visibility.Visible;
        HintBadge.Visibility = Visibility.Collapsed;
        OverlayCanvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetPosition(OverlayCanvas);
        MeasureLine.X2 = p.X; MeasureLine.Y2 = p.Y;
        double dx = p.X - _start.X, dy = p.Y - _start.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
        if (angle < 0) angle += 360;
        ReadoutText.Text = $"Δx {(int)dx}px   Δy {(int)dy}px   ‖ {(int)len}px   ∠ {angle:F1}°";
        Canvas.SetLeft(ReadoutBadge, Math.Min(p.X + 12, ActualWidth - 220));
        Canvas.SetTop(ReadoutBadge, Math.Max(p.Y - 30, 4));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        OverlayCanvas.ReleaseMouseCapture();
        // Keep the readout visible for a moment so the user can read it.
    }
}
