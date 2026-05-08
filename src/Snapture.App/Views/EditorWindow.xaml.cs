using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Snapture.App.Editor;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class EditorWindow : Window
{
    public enum EditorTool
    {
        Select, Rectangle, Ellipse, Line, Arrow, Freehand, Text, Highlight, Blur, Redact, Step, Crop
    }

    private static readonly (EditorTool tool, string label, Key hotkey, string glyph)[] ToolButtons =
    {
        (EditorTool.Select,    "Select / move",       Key.V, "↘"),
        (EditorTool.Rectangle, "Rectangle (R)",       Key.R, "▭"),
        (EditorTool.Ellipse,   "Ellipse (E)",         Key.E, "◯"),
        (EditorTool.Line,      "Line (L)",            Key.L, "／"),
        (EditorTool.Arrow,     "Arrow (A)",           Key.A, "➜"),
        (EditorTool.Freehand,  "Freehand pen (F)",    Key.F, "✎"),
        (EditorTool.Text,      "Text (T)",            Key.T, "T"),
        (EditorTool.Highlight, "Highlight (H)",       Key.H, "▣"),
        (EditorTool.Blur,      "Blur / pixelate (B)", Key.B, "▦"),
        (EditorTool.Redact,    "Redact secrets (X)",  Key.X, "■"),
        (EditorTool.Step,      "Step counter (N)",    Key.N, "①"),
        (EditorTool.Crop,      "Crop (C)",            Key.C, "✂"),
    };

    private static readonly uint[] DefaultPalette =
    {
        0xFFE74C3C, 0xFFF39C12, 0xFFFFD43B, 0xFF2ECC71, 0xFF3498DB, 0xFF9B59B6,
        0xFF111111, 0xFFFFFFFF, 0xFF7F8C8D, 0xFFE67E22, 0xFF1ABC9C, 0xFFEC407A
    };

    private readonly AnnotationDocument _doc;
    private readonly CommandStack _commands = new();
    private string? _projectPath;
    private string? _exportPath;
    private EditorTool _activeTool = EditorTool.Select;
    private uint _activeColor = 0xFFE74C3C;
    private float _strokeThickness = 3f;
    private readonly List<uint> _recentColors = new();
    private int _stepCounter = 1;

    // In-progress shape (during drag)
    private Shape? _draftShape;
    private SKPoint _dragStart;
    private bool _dragging;


    public EditorWindow(BitmapSource image, string? savedPath, CaptureResult capture)
    {
        InitializeComponent();
        _doc = new AnnotationDocument(BitmapSourceToSKBitmap(image));
        _exportPath = savedPath;
        BuildToolButtons();
        BuildColorPalette();
        UpdateRecentColors();
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        StatusText.Text = capture.Source is { } src ? $"Captured: {src}" : "Ready";
        PathText.Text = savedPath ?? "(unsaved)";
        KeyDown += OnKeyDown;
        Canvas.InvalidateVisual();
    }

    public EditorWindow(string projectOrImagePath) : this(LoadFromDisk(projectOrImagePath, out var doc), doc, projectOrImagePath)
    {
    }

    private EditorWindow(BitmapSource bs, AnnotationDocument doc, string path)
    {
        InitializeComponent();
        _doc = doc;
        _projectPath = path.EndsWith(SnapFileFormat.Extension, StringComparison.OrdinalIgnoreCase) ? path : null;
        _exportPath = _projectPath is null ? path : null;
        BuildToolButtons();
        BuildColorPalette();
        UpdateRecentColors();
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        StatusText.Text = $"Loaded {Path.GetFileName(path)}";
        PathText.Text = path;
        KeyDown += OnKeyDown;
        Canvas.InvalidateVisual();
    }

    private static BitmapSource LoadFromDisk(string path, out AnnotationDocument doc)
    {
        if (path.EndsWith(SnapFileFormat.Extension, StringComparison.OrdinalIgnoreCase))
        {
            doc = SnapFileFormat.Load(path);
        }
        else
        {
            using var fs = File.OpenRead(path);
            var bg = SKBitmap.Decode(fs) ?? throw new InvalidDataException("Could not decode image.");
            doc = new AnnotationDocument(bg);
        }
        return SkiaToBitmapSource(doc.RenderToBitmap());
    }

    // ---- Tool palette ---------------------------------------------------------

    private void BuildToolButtons()
    {
        ToolStack.Children.Clear();
        foreach (var (tool, tip, hotkey, glyph) in ToolButtons)
        {
            var btn = new Button
            {
                Content = glyph,
                ToolTip = tip,
                Width = 44,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 4),
                Tag = tool,
                FontSize = 18
            };
            EditorTool captured = tool;
            btn.Click += (_, _) => SetActiveTool(captured);
            ToolStack.Children.Add(btn);
        }
        SetActiveTool(_activeTool);
    }

    private void SetActiveTool(EditorTool tool)
    {
        _activeTool = tool;
        foreach (Button b in ToolStack.Children.OfType<Button>())
        {
            bool active = (EditorTool)b.Tag! == tool;
            b.Background = active
                ? (Brush)FindResource("Mauve")
                : (Brush)FindResource("Surface0");
            b.Foreground = active ? Brushes.Black : (Brush)FindResource("Text");
            b.BorderThickness = new Thickness(active ? 0 : 1);
        }
        Canvas.Cursor = tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
        StatusText.Text = $"Tool: {tool}";
    }

    private void BuildColorPalette()
    {
        ColorPalette.Children.Clear();
        foreach (var argb in DefaultPalette)
        {
            uint captured = argb;
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(ToWpfColor(argb)),
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(ToWpfColor(0xFF45475A)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (_, _) => SetActiveColor(captured);
            ColorPalette.Children.Add(swatch);
        }
    }

    private void SetActiveColor(uint argb)
    {
        _activeColor = argb;
        if (_recentColors.Contains(argb)) _recentColors.Remove(argb);
        _recentColors.Insert(0, argb);
        if (_recentColors.Count > 6) _recentColors.RemoveAt(_recentColors.Count - 1);
        UpdateRecentColors();
        StatusText.Text = $"Color: #{argb:X8}";
    }

    private void UpdateRecentColors()
    {
        RecentColors.Children.Clear();
        foreach (var argb in _recentColors)
        {
            uint captured = argb;
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(ToWpfColor(argb)),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };
            swatch.MouseLeftButtonDown += (_, _) => SetActiveColor(captured);
            RecentColors.Children.Add(swatch);
        }
    }

    private static Color ToWpfColor(uint argb) =>
        Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    // ---- Hotkeys --------------------------------------------------------------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) { _commands.Undo(_doc); Canvas.InvalidateVisual(); e.Handled = true; return; }
            if (e.Key == Key.Y) { _commands.Redo(_doc); Canvas.InvalidateVisual(); e.Handled = true; return; }
            if (e.Key == Key.S) { OnSaveProjectClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.E) { OnExportPngClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.O) { OnOpenClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.C) { OnCopyClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
        }
        foreach (var (tool, _, hk, _) in ToolButtons)
        {
            if (e.Key == hk && Keyboard.Modifiers == ModifierKeys.None)
            {
                SetActiveTool(tool);
                e.Handled = true;
                return;
            }
        }
        if (e.Key == Key.Delete)
        {
            // No selected-shape model yet; clear last shape.
            if (_doc.Shapes.Count > 0)
            {
                var last = _doc.Shapes[^1];
                _commands.Do(_doc, new RemoveShapeCommand(last));
                Canvas.InvalidateVisual();
            }
        }
    }

    // ---- Canvas paint + interaction ------------------------------------------

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Apply frame backdrop wrappers
        bool gradient = GradientCheck.IsChecked == true;
        bool shadow = ShadowCheck.IsChecked == true;
        bool rounded = RoundedCheck.IsChecked == true;
        bool codeChrome = CodeChromeCheck.IsChecked == true;

        if (gradient)
        {
            using var bg = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0),
                    new SKPoint(e.Info.Width, e.Info.Height),
                    new[] { new SKColor(50, 30, 90), new SKColor(170, 80, 200) },
                    null,
                    SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(0, 0, e.Info.Width, e.Info.Height, bg);
        }

        // Compose the document into a temp bitmap so adjustments + frame style can be layered.
        using var inner = _doc.RenderToBitmap();
        ApplyAdjustments(inner);

        // Code-window chrome shows in the export only; in the canvas preview the SKElement is
        // sized to the document, so adding a 36-pixel bar would be clipped. The status text
        // hints at this when the toggle is on.
        var rect = new SKRect(0, 0, _doc.Width, _doc.Height);
        if (shadow)
        {
            using var shadowPaint = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateDropShadowOnly(0, 6, 12, 12, new SKColor(0, 0, 0, 160))
            };
            canvas.DrawBitmap(inner, rect, shadowPaint);
        }

        using var clipPath = new SKPath();
        if (rounded) clipPath.AddRoundRect(new SKRoundRect(rect, 18));
        else clipPath.AddRect(rect);

        canvas.Save();
        canvas.ClipPath(clipPath, antialias: true);
        canvas.DrawBitmap(inner, rect);
        canvas.Restore();
        if (codeChrome)
            StatusText.Text = "Code window chrome will appear on export (preview omits it).";

        // Live preview of the shape currently being drawn
        if (_draftShape is not null)
            _draftShape.Render(canvas, _doc);
    }

    private void ApplyAdjustments(SKBitmap bmp)
    {
        float bright = (float)BrightnessSlider.Value;
        float contrast = (float)ContrastSlider.Value;
        bool gray = GrayscaleCheck.IsChecked == true;
        bool invert = InvertCheck.IsChecked == true;
        if (bright == 0 && contrast == 0 && !gray && !invert) return;

        // In-place pixel pass — small images, fine. For large images we'd build an
        // SKColorFilter chain; this stays simple and avoids surprises.
        using var pixmap = bmp.PeekPixels();
        if (pixmap is null) return;
        unsafe
        {
            byte* p = (byte*)pixmap.GetPixels().ToPointer();
            int stride = pixmap.RowBytes;
            float c = (contrast / 100f) + 1f; // -100..+100 → 0..2
            float bAdd = bright * 2.55f;       // -100..+100 → -255..+255
            for (int y = 0; y < bmp.Height; y++)
            {
                byte* row = p + y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    byte* px = row + x * 4;
                    float B = px[0], G = px[1], R = px[2];
                    if (gray)
                    {
                        float lum = 0.299f * R + 0.587f * G + 0.114f * B;
                        R = G = B = lum;
                    }
                    if (invert) { R = 255 - R; G = 255 - G; B = 255 - B; }
                    R = Clamp((R - 128) * c + 128 + bAdd);
                    G = Clamp((G - 128) * c + 128 + bAdd);
                    B = Clamp((B - 128) * c + 128 + bAdd);
                    px[0] = (byte)B; px[1] = (byte)G; px[2] = (byte)R;
                }
            }
        }
    }

    private static float Clamp(float v) => v < 0 ? 0 : v > 255 ? 255 : v;

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = ToImagePoint(e.GetPosition(Canvas));
        _dragStart = pos;
        _dragging = true;
        _draftShape = CreateDraftShape(pos);
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _draftShape is null) return;
        var pos = ToImagePoint(e.GetPosition(Canvas));
        UpdateDraft(_draftShape, _dragStart, pos);
        Canvas.InvalidateVisual();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (_draftShape is null) return;
        var pos = ToImagePoint(e.GetPosition(Canvas));
        UpdateDraft(_draftShape, _dragStart, pos);
        if (IsValidShape(_draftShape))
        {
            _commands.Do(_doc, new AddShapeCommand(_draftShape));
            if (_activeTool == EditorTool.Step) _stepCounter++;
            if (_recentColors.Count == 0 || _recentColors[0] != _activeColor) SetActiveColor(_activeColor);
        }
        _draftShape = null;
        Canvas.InvalidateVisual();
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Mouse-wheel adjusts stroke thickness when not holding Ctrl
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            double delta = e.Delta > 0 ? 1 : -1;
            StrokeSlider.Value = Math.Max(StrokeSlider.Minimum, Math.Min(StrokeSlider.Maximum, StrokeSlider.Value + delta));
            e.Handled = true;
        }
    }

    private SKPoint ToImagePoint(System.Windows.Point p) => new((float)p.X, (float)p.Y);

    private Shape? CreateDraftShape(SKPoint p)
    {
        bool dashed = DashedCheck.IsChecked == true;
        bool bidir = BidirCheck.IsChecked == true;
        bool filled = FilledCheck.IsChecked == true;
        bool pixelate = PixelateCheck.IsChecked == true;
        return _activeTool switch
        {
            EditorTool.Rectangle => new RectangleShape   { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Filled = filled },
            EditorTool.Ellipse   => new EllipseShape     { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Filled = filled },
            EditorTool.Line      => new LineShape        { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Dashed = dashed },
            EditorTool.Arrow     => new ArrowShape       { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Dashed = dashed, Bidirectional = bidir },
            EditorTool.Freehand  => new FreehandShape    { Points = new() { p }, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness },
            EditorTool.Text      => new TextShape        { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, Text = PromptForText() ?? "" },
            EditorTool.Highlight => new HighlightShape   { X = p.X, Y = p.Y, StrokeColorArgb = 0xFFFFD43B },
            EditorTool.Blur      => new BlurShape        { X = p.X, Y = p.Y, BlurRadius = Math.Max(8, _strokeThickness * 4), Pixelate = pixelate },
            EditorTool.Redact    => new RedactShape      { X = p.X, Y = p.Y },
            EditorTool.Step      => new StepShape        { X = p.X, Y = p.Y, Label = _stepCounter.ToString(), StrokeColorArgb = _activeColor, Radius = Math.Max(14, _strokeThickness * 5) },
            EditorTool.Crop      => null, // crop-on-release handled separately
            _ => null,
        };
    }

    private void UpdateDraft(Shape shape, SKPoint a, SKPoint b)
    {
        switch (shape)
        {
            case RectangleShape r:
                r.X = Math.Min(a.X, b.X); r.Y = Math.Min(a.Y, b.Y);
                r.Width = Math.Abs(b.X - a.X); r.Height = Math.Abs(b.Y - a.Y);
                break;
            case EllipseShape e:
                e.X = Math.Min(a.X, b.X); e.Y = Math.Min(a.Y, b.Y);
                e.Width = Math.Abs(b.X - a.X); e.Height = Math.Abs(b.Y - a.Y);
                break;
            case LineShape l:    l.X2 = b.X; l.Y2 = b.Y; break;
            case ArrowShape ar:  ar.X2 = b.X; ar.Y2 = b.Y; break;
            case FreehandShape f:
                if (f.Points.Count == 0 || (f.Points[^1] - b).Length > 0.5f) f.Points.Add(b);
                break;
            case HighlightShape h:
                h.X = Math.Min(a.X, b.X); h.Y = Math.Min(a.Y, b.Y);
                h.Width = Math.Abs(b.X - a.X); h.Height = Math.Abs(b.Y - a.Y);
                break;
            case BlurShape bs:
                bs.X = Math.Min(a.X, b.X); bs.Y = Math.Min(a.Y, b.Y);
                bs.Width = Math.Abs(b.X - a.X); bs.Height = Math.Abs(b.Y - a.Y);
                break;
            case RedactShape r2:
                r2.X = Math.Min(a.X, b.X); r2.Y = Math.Min(a.Y, b.Y);
                r2.Width = Math.Abs(b.X - a.X); r2.Height = Math.Abs(b.Y - a.Y);
                break;
        }
    }

    private static bool IsValidShape(Shape s) => s switch
    {
        RectangleShape r   => r.Width >= 4 && r.Height >= 4,
        EllipseShape e     => e.Width >= 4 && e.Height >= 4,
        LineShape l        => Math.Abs(l.X2 - l.X1) + Math.Abs(l.Y2 - l.Y1) >= 4,
        ArrowShape a       => Math.Abs(a.X2 - a.X1) + Math.Abs(a.Y2 - a.Y1) >= 4,
        FreehandShape f    => f.Points.Count >= 2,
        TextShape t        => !string.IsNullOrWhiteSpace(t.Text),
        HighlightShape h   => h.Width >= 4 && h.Height >= 4,
        BlurShape b        => b.Width >= 4 && b.Height >= 4,
        RedactShape r      => r.Width >= 4 && r.Height >= 4,
        StepShape          => true,
        _                  => false,
    };

    private string? PromptForText()
    {
        var dlg = new TextInputDialog { Owner = this };
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }

    // ---- Toolbar handlers -----------------------------------------------------

    private void OnUndoClicked(object sender, RoutedEventArgs e) { _commands.Undo(_doc); Canvas.InvalidateVisual(); }
    private void OnRedoClicked(object sender, RoutedEventArgs e) { _commands.Redo(_doc); Canvas.InvalidateVisual(); }
    private void OnStrokeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _strokeThickness = (float)e.NewValue;
    private void OnAdjustmentChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Canvas.InvalidateVisual();
    private void OnAdjustmentClicked(object sender, RoutedEventArgs e) => Canvas.InvalidateVisual();
    private void OnFrameClicked(object sender, RoutedEventArgs e) => Canvas.InvalidateVisual();

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Snapture project (*.snapture)|*.snapture|Image (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var w = new EditorWindow(dlg.FileName);
            w.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnSaveProjectClicked(object sender, RoutedEventArgs e)
    {
        if (_projectPath is null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Snapture project (*.snapture)|*.snapture",
                FileName = $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.snapture"
            };
            if (dlg.ShowDialog(this) != true) return;
            _projectPath = dlg.FileName;
        }
        try
        {
            SnapFileFormat.Save(_projectPath, _doc);
            StatusText.Text = $"Project saved: {Path.GetFileName(_projectPath)}";
            PathText.Text = _projectPath;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save project:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnExportPngClicked(object sender, RoutedEventArgs e)
    {
        if (_exportPath is null)
        {
            OnExportAsClicked(sender, e);
            return;
        }
        ExportTo(_exportPath);
    }

    private void OnExportAsClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp|WebP (*.webp)|*.webp",
            FileName = $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dlg.ShowDialog(this) != true) return;
        ExportTo(dlg.FileName);
        _exportPath = dlg.FileName;
    }

    private void ExportTo(string path)
    {
        try
        {
            using var flat = RenderForExport();
            using var image = SKImage.FromBitmap(flat);
            var fmt = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                ".bmp" => SKEncodedImageFormat.Bmp,
                ".webp" => SKEncodedImageFormat.Webp,
                _ => SKEncodedImageFormat.Png
            };
            int quality = fmt == SKEncodedImageFormat.Jpeg ? 92 : (fmt == SKEncodedImageFormat.Webp ? 88 : 100);
            using var data = image.Encode(fmt, quality);
            using var fs = File.Create(path);
            data.SaveTo(fs);
            StatusText.Text = $"Exported {Path.GetFileName(path)}";
            PathText.Text = path;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private SKBitmap RenderForExport()
    {
        // Render document at full size with current adjustments + frame applied.
        using var doc = _doc.RenderToBitmap();
        ApplyAdjustments(doc);

        bool gradient = GradientCheck.IsChecked == true;
        bool shadow = ShadowCheck.IsChecked == true;
        bool rounded = RoundedCheck.IsChecked == true;
        bool codeChrome = CodeChromeCheck.IsChecked == true;

        int chromeBar = codeChrome ? 36 : 0;
        int padding = (gradient || shadow || codeChrome) ? 80 : 0;
        int outW = _doc.Width + padding * 2;
        int outH = _doc.Height + chromeBar + padding * 2;
        var info = new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Premul);
        var output = new SKBitmap(info);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);

        if (gradient)
        {
            using var bg = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(
                    new SKPoint(0, 0), new SKPoint(outW, outH),
                    new[] { new SKColor(50, 30, 90), new SKColor(170, 80, 200) },
                    null, SKShaderTileMode.Clamp)
            };
            canvas.DrawRect(0, 0, outW, outH, bg);
        }

        var contentRect = new SKRect(padding, padding + chromeBar, padding + _doc.Width, padding + chromeBar + _doc.Height);
        var fullFrameRect = new SKRect(padding, padding, padding + _doc.Width, padding + chromeBar + _doc.Height);

        if (shadow)
        {
            using var shadowPaint = new SKPaint
            {
                ImageFilter = SKImageFilter.CreateDropShadowOnly(0, 8, 16, 16, new SKColor(0, 0, 0, 180))
            };
            canvas.DrawRect(fullFrameRect, shadowPaint);
        }

        using var clipPath = new SKPath();
        if (rounded || codeChrome) clipPath.AddRoundRect(new SKRoundRect(fullFrameRect, 14));
        else clipPath.AddRect(fullFrameRect);
        canvas.Save();
        canvas.ClipPath(clipPath, antialias: true);

        if (codeChrome)
        {
            // Carbon-style title bar: dark gray background + traffic-light dots.
            var titleRect = new SKRect(fullFrameRect.Left, fullFrameRect.Top, fullFrameRect.Right, fullFrameRect.Top + chromeBar);
            using var bar = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(40, 42, 54) };
            canvas.DrawRect(titleRect, bar);
            float r = 7, gap = 8;
            float cx = titleRect.Left + 18, cy = titleRect.Top + chromeBar / 2f;
            using var redDot    = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 95, 86),  IsAntialias = true };
            using var yellowDot = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(255, 189, 46), IsAntialias = true };
            using var greenDot  = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(39, 201, 63),  IsAntialias = true };
            canvas.DrawCircle(cx,                cy, r, redDot);
            canvas.DrawCircle(cx + (r * 2 + gap), cy, r, yellowDot);
            canvas.DrawCircle(cx + (r * 2 + gap) * 2, cy, r, greenDot);
        }

        canvas.DrawBitmap(doc, contentRect);
        canvas.Restore();
        return output;
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var flat = RenderForExport();
            var bs = SkiaToBitmapSource(flat);
            Clipboard.SetImage(bs);
            StatusText.Text = "Copied to clipboard.";
        }
        catch { StatusText.Text = "Clipboard busy — try again."; }
    }

    private void OnPinClicked(object sender, RoutedEventArgs e)
    {
        using var flat = RenderForExport();
        var bs = SkiaToBitmapSource(flat);
        new PinWindow(bs).Show();
    }

    private void OnShareLanClicked(object sender, RoutedEventArgs e)
    {
        if (App.Host is null) return;
        if (!App.Host.LanShare.IsRunning && !App.Host.TryStartLanShare())
        {
            StatusText.Text = "LAN share is off — open Settings → LAN share to configure it.";
            return;
        }
        try
        {
            // Flatten current document with adjustments + frame to a temp PNG, then register.
            using var flat = RenderForExport();
            using var image = SkiaSharp.SKImage.FromBitmap(flat);
            using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Snapture", "share");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"share_{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            using (var fs = System.IO.File.Create(path)) data.SaveTo(fs);

            var ttl = TimeSpan.FromMinutes(App.Host.Settings.Current.LanShareTtlMinutes);
            var url = App.Host.LanShare.Register(path, ttl);
            try { Clipboard.SetText(url); } catch { }
            StatusText.Text = $"LAN URL copied: {url} (expires in {ttl.TotalMinutes:F0}m)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Share failed: {ex.Message}";
        }
    }

    private async void OnAutoRedactClicked(object sender, RoutedEventArgs e)
    {
        AutoRedactButton.IsEnabled = false;
        StatusText.Text = "Scanning for secrets…";
        try
        {
            using var flat = _doc.RenderToBitmap();
            var findings = await AutoRedactor.ScanAsync(flat);
            if (findings.Count == 0)
            {
                StatusText.Text = "No secrets detected.";
                return;
            }
            int added = AutoRedactor.ApplyToDocument(_doc, findings);
            // Stuff the redactions into a single command so undo reverts the whole batch.
            // (Each shape was added directly; record the redactions for explicit undo.)
            // Simpler: rebuild as Add commands so existing undo works.
            // Pop them out then push as a batch.
            var added_shapes = _doc.Shapes.TakeLast(added).ToList();
            for (int i = 0; i < added; i++) _doc.Shapes.RemoveAt(_doc.Shapes.Count - 1);
            foreach (var s in added_shapes) _commands.Do(_doc, new AddShapeCommand(s));
            Canvas.InvalidateVisual();
            StatusText.Text = $"Added {added} redactions: " + string.Join(", ",
                findings.Select(f => f.RuleId).Distinct().Take(6));
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Auto-redact failed: {ex.Message}";
        }
        finally { AutoRedactButton.IsEnabled = true; }
    }

    // ---- BitmapSource <-> SKBitmap converters --------------------------------

    private static SKBitmap BitmapSourceToSKBitmap(BitmapSource bs)
    {
        var formatted = new FormatConvertedBitmap(bs, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        int stride = formatted.PixelWidth * 4;
        var pixels = new byte[stride * formatted.PixelHeight];
        formatted.CopyPixels(pixels, stride, 0);
        var skinfo = new SKImageInfo(formatted.PixelWidth, formatted.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var sk = new SKBitmap(skinfo);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, sk.GetPixels(), pixels.Length);
        return sk;
    }

    private static BitmapSource SkiaToBitmapSource(SKBitmap bmp)
    {
        var info = bmp.Info;
        var bs = BitmapSource.Create(info.Width, info.Height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null,
            bmp.GetPixels(), info.RowBytes * info.Height, info.RowBytes);
        bs.Freeze();
        return bs;
    }
}

/// <summary>Inline modal text input — used by the Text tool.</summary>
public sealed class TextInputDialog : Window
{
    public string InputText { get; private set; } = "";
    private readonly TextBox _box;

    public TextInputDialog()
    {
        Title = "Add text";
        Width = 360;
        Height = 160;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)Application.Current.Resources["Mantle"];
        Foreground = (Brush)Application.Current.Resources["Text"];
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = "Enter text:", Foreground = (Brush)Application.Current.Resources["Subtext"] };
        Grid.SetRow(label, 0);
        _box = new TextBox { Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetRow(_box, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => { InputText = _box.Text; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(label);
        grid.Children.Add(_box);
        grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) => _box.Focus();
    }
}
