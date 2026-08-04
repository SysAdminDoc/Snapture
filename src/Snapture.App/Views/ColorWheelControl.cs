using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using Snapture.App.Editor;

namespace Snapture.App.Views;

/// <summary>
/// A compact HSV colour wheel used by the editor's canvas context action. The ring and
/// inner disc are vector-rendered so the popup stays crisp on scaled displays.
/// </summary>
internal sealed class ColorWheelControl : FrameworkElement
{
    private const double Size = 240;
    private const double Center = Size / 2;
    private const double ColorRadius = 103;
    private const double InnerRadius = 77;

    private uint _selectedColorArgb = 0xFFFF0000;
    private bool _dragging;

    public event EventHandler<uint>? ColorSelected;

    public uint SelectedColorArgb
    {
        get => _selectedColorArgb;
        set
        {
            _selectedColorArgb = value;
            InvalidateVisual();
        }
    }

    public ColorWheelControl()
    {
        Width = Size;
        Height = Size;
        Focusable = false;
        Cursor = Cursors.Cross;
        AutomationProperties.SetName(this, "Colour wheel");
        AutomationProperties.SetHelpText(this, "Choose a colour by hue and saturation.");
    }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override Size ArrangeOverride(Size finalRect) => new(Size, Size);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var panel = new SolidColorBrush(Color.FromArgb(248, 28, 31, 38));
        panel.Freeze();
        drawingContext.DrawRoundedRectangle(panel, null, new Rect(0, 0, Size, Size), 18, 18);

        var center = new Point(Center, Center);
        const int hueSegments = 72;
        const int saturationBands = 16;
        for (int band = 0; band < saturationBands; band++)
        {
            double inner = InnerRadius * band / saturationBands;
            double outer = InnerRadius * (band + 1) / saturationBands;
            double saturation = outer / InnerRadius;
            for (int segment = 0; segment < hueSegments; segment++)
            {
                double start = segment * 360.0 / hueSegments;
                double end = (segment + 1) * 360.0 / hueSegments;
                DrawSector(drawingContext, center, inner, outer, start, end,
                    ToMediaColor(ColorWheelMath.FromHsv(start, saturation, 1.0)));
            }
        }

        for (int segment = 0; segment < hueSegments; segment++)
        {
            double start = segment * 360.0 / hueSegments;
            double end = (segment + 1) * 360.0 / hueSegments;
            DrawSector(drawingContext, center, InnerRadius, ColorRadius, start, end,
                ToMediaColor(ColorWheelMath.FromHsv(start, 1.0, 1.0)));
        }

        var hsv = ColorWheelMath.ToHsv(_selectedColorArgb);
        double markerRadius = Math.Min(InnerRadius, hsv.Saturation * InnerRadius);
        double markerAngle = hsv.Hue * Math.PI / 180.0;
        var marker = new Point(
            Center + Math.Cos(markerAngle) * markerRadius,
            Center + Math.Sin(markerAngle) * markerRadius);
        var markerFill = new SolidColorBrush(ToMediaColor(_selectedColorArgb));
        markerFill.Freeze();
        var markerStroke = new Pen(Brushes.White, 2);
        drawingContext.DrawEllipse(markerFill, markerStroke, marker, 7, 7);

        var current = new SolidColorBrush(ToMediaColor(_selectedColorArgb));
        current.Freeze();
        drawingContext.DrawEllipse(current,
            new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 1),
            center, 12, 12);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (TryGetColor(e.GetPosition(this), out var color))
        {
            _dragging = true;
            CaptureMouse();
            SelectColor(color);
            e.Handled = true;
        }
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && e.LeftButton == MouseButtonState.Pressed &&
            TryGetColor(e.GetPosition(this), out var color))
        {
            SelectColor(color);
            e.Handled = true;
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            if (TryGetColor(e.GetPosition(this), out var color))
                SelectColor(color);
            _dragging = false;
            ReleaseMouseCapture();
            ColorSelected?.Invoke(this, _selectedColorArgb);
            e.Handled = true;
        }
        base.OnMouseLeftButtonUp(e);
    }

    private bool TryGetColor(Point point, out uint color)
    {
        double x = point.X - Center;
        double y = point.Y - Center;
        return ColorWheelMath.TryFromPoint(x, y, ColorRadius, (byte)(_selectedColorArgb >> 24), out color);
    }

    private void SelectColor(uint color)
    {
        _selectedColorArgb = color;
        InvalidateVisual();
    }

    private static void DrawSector(DrawingContext drawingContext, Point center, double innerRadius,
        double outerRadius, double startDegrees, double endDegrees, Color color)
    {
        // Slight overlap prevents WPF's antialiased geometry edges from exposing the
        // dark popup background as a distracting grid between adjacent colour cells.
        const double overlapAngle = 0.45;
        const double overlapRadius = 0.7;
        innerRadius = Math.Max(0, innerRadius - overlapRadius);
        outerRadius += overlapRadius;
        startDegrees -= overlapAngle;
        endDegrees += overlapAngle;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var outerStart = Polar(center, outerRadius, startDegrees);
            var outerEnd = Polar(center, outerRadius, endDegrees);
            var innerEnd = Polar(center, innerRadius, endDegrees);
            var innerStart = Polar(center, innerRadius, startDegrees);
            context.BeginFigure(outerStart, true, true);
            context.ArcTo(outerEnd, new Size(outerRadius, outerRadius), 0, false,
                SweepDirection.Clockwise, true, false);
            if (innerRadius <= 0.01)
            {
                context.LineTo(center, true, false);
            }
            else
            {
                context.LineTo(innerEnd, true, false);
                context.ArcTo(innerStart, new Size(innerRadius, innerRadius), 0, false,
                    SweepDirection.Counterclockwise, true, false);
            }
            context.Close();
        }
        geometry.Freeze();
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        drawingContext.DrawGeometry(brush, null, geometry);
    }

    private static Point Polar(Point center, double radius, double degrees)
    {
        double angle = degrees * Math.PI / 180.0;
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }

    private static Color ToMediaColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
}
