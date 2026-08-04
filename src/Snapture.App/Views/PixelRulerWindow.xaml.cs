using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Snapture.Capture;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class PixelRulerWindow : Window
{
    private readonly Rectangle _virtualBounds;
    private readonly Bitmap? _frozenScreen;
    private System.Windows.Point _start;
    private bool _dragging;

    public PixelRulerWindow()
    {
        InitializeComponent();
        _virtualBounds = MonitorEnumerator.GetVirtualScreen();
        try
        {
            var capture = new GdiCaptureEngine().CaptureVirtualScreenAsync().GetAwaiter().GetResult();
            _frozenScreen = capture.Bitmap;
        }
        catch
        {
            _frozenScreen = null;
        }
        Left = _virtualBounds.X;
        Top = _virtualBounds.Y;
        Width = _virtualBounds.Width;
        Height = _virtualBounds.Height;
        DimRect.Width = _virtualBounds.Width;
        DimRect.Height = _virtualBounds.Height;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        Loaded += (_, _) =>
        {
            PositionHintBadge();
            Activate();
        };
        SizeChanged += (_, _) => PositionHintBadge();
        Closed += (_, _) => _frozenScreen?.Dispose();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) && _frozenScreen is not null)
        {
            ShowNearestEdge(e.GetPosition(OverlayCanvas));
            e.Handled = true;
            return;
        }
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
        ReadoutText.Text = $"X {FormatSigned(dx)} px   Y {FormatSigned(dy)} px   Length {(int)len} px   Angle {angle:F1} deg";
        ReadoutBadge.UpdateLayout();
        var badgeWidth = Math.Max(ReadoutBadge.ActualWidth, 260);
        Canvas.SetLeft(ReadoutBadge, Math.Min(Math.Max(p.X + 12, 8), ActualWidth - badgeWidth - 8));
        Canvas.SetTop(ReadoutBadge, Math.Max(p.Y - 32, 8));
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        OverlayCanvas.ReleaseMouseCapture();
        // Keep the readout visible for a moment so the user can read it.
    }

    private void ShowNearestEdge(System.Windows.Point position)
    {
        var local = new System.Drawing.Point(
            (int)Math.Round(position.X),
            (int)Math.Round(position.Y));
        var measurement = EdgeDetectionRulerService.FindNearest(_frozenScreen!, local);
        if (measurement.Nearest is not { } nearest)
        {
            ReadoutText.Text = "No high-contrast UI edge found nearby";
            ReadoutBadge.Visibility = Visibility.Visible;
        }
        else
        {
            ReadoutText.Text = $"Nearest {nearest.Direction.ToString().ToLowerInvariant()} edge: {nearest.Distance} px   contrast {nearest.Score:F0}/255";
            ReadoutBadge.Visibility = Visibility.Visible;
            MeasureLine.X1 = position.X;
            MeasureLine.Y1 = position.Y;
            MeasureLine.X2 = nearest.Location.X;
            MeasureLine.Y2 = nearest.Location.Y;
            MeasureLine.Visibility = Visibility.Visible;
        }
        ReadoutBadge.UpdateLayout();
        Canvas.SetLeft(ReadoutBadge, Math.Min(Math.Max(position.X + 12, 8), ActualWidth - Math.Max(ReadoutBadge.ActualWidth, 260) - 8));
        Canvas.SetTop(ReadoutBadge, Math.Max(position.Y - 32, 8));
    }

    private void PositionHintBadge()
    {
        HintBadge.UpdateLayout();
        var badgeWidth = Math.Max(HintBadge.ActualWidth, 320);
        var badgeHeight = Math.Max(HintBadge.ActualHeight, 54);
        Canvas.SetLeft(HintBadge, Math.Max(16, (ActualWidth - badgeWidth) / 2));
        Canvas.SetTop(HintBadge, Math.Max(16, ActualHeight - badgeHeight - 24));
    }

    private static string FormatSigned(double value)
        => value >= 0 ? $"+{(int)value}" : ((int)value).ToString();
}
