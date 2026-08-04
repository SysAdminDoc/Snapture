using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Snapture.App.Editor;
using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class EditorWindow : Window
{
    public enum EditorTool
    {
        Select, Rectangle, Ellipse, Line, Arrow, Freehand, Text, SpeechBalloon, Highlight, LineStateMarker, Blur, Redact, Step, Crop, Eyedropper, Spotlight, Ruler
    }

    private static readonly (EditorTool tool, string label, Key hotkey, string glyph)[] ToolButtons =
    {
        (EditorTool.Select,    "Select / move",       Key.V, "↘"),
        (EditorTool.Rectangle, "Rectangle",           Key.R, "▭"),
        (EditorTool.Ellipse,   "Ellipse",             Key.E, "◯"),
        (EditorTool.Line,      "Line",                Key.L, "／"),
        (EditorTool.Arrow,     "Arrow",               Key.A, "➜"),
        (EditorTool.Freehand,  "Freehand pen",        Key.F, "✎"),
        (EditorTool.Text,      "Text",                Key.T, "T"),
        (EditorTool.SpeechBalloon, "Speech balloon", Key.Q, "☁"),
        (EditorTool.Highlight, "Highlight",           Key.H, "▣"),
        (EditorTool.LineStateMarker, "Code line marker", Key.G, "±"),
        (EditorTool.Blur,      "Blur / pixelate",     Key.B, "▦"),
        (EditorTool.Redact,    "Redact secrets",      Key.X, "■"),
        (EditorTool.Step,      "Step counter",        Key.N, "①"),
        (EditorTool.Crop,      "Crop",                Key.C, "✂"),
        (EditorTool.Eyedropper,"Eyedropper",          Key.I, "◎"),
        (EditorTool.Spotlight, "Spotlight",           Key.P, "◐"),
        (EditorTool.Ruler,     "Ruler",               Key.M, "⌁"),
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
    private float _sloppiness;
    private ArrowStyle _arrowStyle = ArrowStyle.Classic;
    private float _arrowCurve;
    private TextOrientation _textOrientation = TextOrientation.Horizontal;
    private float _balloonCornerRadius = 16f;
    private AnnotationCategory _annotationCategory = AnnotationCategory.None;
    private LineState _lineState = LineState.Added;
    private readonly List<uint> _recentColors = new();
    private int _stepCounter = 1;
    private SKRect? _cropSelection;
    private bool _optionsPanelVisible = true;
    private const double OptionsPanelWidth = 280;

    // In-progress shape (during drag)
    private Shape? _draftShape;
    private SKPoint _dragStart;
    private bool _dragging;

    // Selection model — tracks shapes the user has clicked in Select mode
    private readonly HashSet<Shape> _selectedShapes = new();
    private readonly HashSet<Shape> _autoRedactionShapes = new();
    private Popup? _colorWheelPopup;

    // Transform handles: resize selected shape via corner/edge drag
    private enum HandlePosition { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left, Move }
    private HandlePosition _activeHandle = HandlePosition.None;
    private SKRect _handleOriginalBounds;
    private Shape? _handleShape;
    private Shape? _handleShapeSnapshot;
    private const float HandleRadius = 5f;

    // Autosave: periodic crash-recovery draft
    private AutosaveService? _autosave;

    // Retake: remembers the capture source so the user can redo it
    private CaptureResult? _captureResult;

    // The editor remains a stateful document window even when its visual content
    // is hosted inside the shared tab shell.
    private EditorTabHostWindow? _tabHost;
    private UIElement? _documentRoot;

    internal event EventHandler? DocumentTitleChanged;

    internal string DocumentTitle =>
        _projectPath is { Length: > 0 } projectPath ? Path.GetFileName(projectPath) :
        _exportPath is { Length: > 0 } exportPath ? Path.GetFileName(exportPath) :
        "Untitled capture";

    public EditorWindow(BitmapSource image, string? savedPath, CaptureResult capture)
    {
        InitializeComponent();
        _doc = new AnnotationDocument(BitmapSourceToSKBitmap(image));
        _exportPath = savedPath;
        _captureResult = capture;
        BuildToolButtons();
        BuildColorPalette();
        UpdateRecentColors();
        UpdateSavedSwatches();
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        StatusText.Text = capture.Source is { } src ? $"Captured: {src}" : "Ready";
        PathText.Text = savedPath ?? "(unsaved)";
        RegisterWindowHandlers();
        _autosave = new AutosaveService(_doc);
        RetakeButton.Visibility = Visibility.Visible;
        RetakeSep.Visibility = Visibility.Visible;
        Canvas.InvalidateVisual();
    }

    public EditorWindow(string projectOrImagePath) : this(LoadFromDisk(projectOrImagePath, out var doc), doc, projectOrImagePath)
    {
    }

    /// <summary>
    /// Opens an editor from a recovered autosave document. The autosave file
    /// is adopted so it will be cleaned up on normal close.
    /// </summary>
    internal EditorWindow(AnnotationDocument recoveredDoc, string autosavePath)
    {
        InitializeComponent();
        _doc = recoveredDoc;
        BuildToolButtons();
        BuildColorPalette();
        UpdateRecentColors();
        UpdateSavedSwatches();
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        StatusText.Text = "Recovered from autosave";
        PathText.Text = "(recovered — unsaved)";
        RegisterWindowHandlers();
        // Delete the original autosave; a new autosave service will create
        // a fresh file on its own timer.
        AutosaveService.Discard(autosavePath);
        _autosave = new AutosaveService(_doc);
        Canvas.InvalidateVisual();
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
        UpdateSavedSwatches();
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        StatusText.Text = $"Loaded {Path.GetFileName(path)}";
        PathText.Text = path;
        RegisterWindowHandlers();
        _autosave = new AutosaveService(_doc);
        Canvas.InvalidateVisual();
    }

    private void RegisterWindowHandlers()
    {
        KeyDown += OnKeyDown;
        Closed += OnEditorClosed;
        if (Content is UIElement root)
        {
            _documentRoot = root;
            root.KeyDown += OnKeyDown;
        }
    }

    internal UIElement DetachContentForTabHost()
    {
        if (Content is not UIElement root)
            throw new InvalidOperationException("The editor visual tree is already detached.");

        Content = null;
        return root;
    }

    internal void AttachToTabHost(EditorTabHostWindow host) => _tabHost = host;

    internal void DisposeForTabHost()
    {
        OnEditorClosed(this, EventArgs.Empty);
        _tabHost = null;
    }

    private Window DialogOwner => _tabHost is { } host ? host : this;

    private void NotifyDocumentTitleChanged() => DocumentTitleChanged?.Invoke(this, EventArgs.Empty);

    private void OnEditorClosed(object? sender, EventArgs e)
    {
        if (_documentRoot is not null)
        {
            _documentRoot.KeyDown -= OnKeyDown;
            _documentRoot = null;
        }
        _colorWheelPopup?.IsOpen = false;
        _colorWheelPopup = null;
        // Clean close: delete the autosave file so no recovery prompt appears next launch.
        _autosave?.DeleteAutosave();
        _autosave?.Dispose();
        _autosave = null;
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
                Content = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe UI Symbol, Segoe UI"),
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = tip,
                Width = 44,
                Height = 38,
                Margin = new Thickness(0, 0, 0, 6),
                Tag = tool,
                Padding = new Thickness(0)
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, tip);
            System.Windows.Automation.AutomationProperties.SetHelpText(btn, "Annotation tool");
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
            b.SetResourceReference(Control.BackgroundProperty, active ? "AppSelection" : "AppSurfaceRaised");
            b.SetResourceReference(Control.ForegroundProperty, active ? "AppAccent" : "AppForeground");
            b.SetResourceReference(Control.BorderBrushProperty, active ? "AppAccent" : "AppBorderStrong");
            b.BorderThickness = new Thickness(1);
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Canvas.Cursor = tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
        ArrowOptionsPanel.Visibility = tool == EditorTool.Arrow ? Visibility.Visible : Visibility.Collapsed;
        TextOptionsPanel.Visibility = tool == EditorTool.Text ? Visibility.Visible : Visibility.Collapsed;
        BalloonOptionsPanel.Visibility = tool == EditorTool.SpeechBalloon ? Visibility.Visible : Visibility.Collapsed;
        CropOptionsPanel.Visibility = tool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        LineMarkerOptionsPanel.Visibility = tool == EditorTool.LineStateMarker ? Visibility.Visible : Visibility.Collapsed;
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
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "AppBorderStrong");
            swatch.ToolTip = $"Use color #{argb:X8}";
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Use color #{argb:X8}");
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
            swatch.SetResourceReference(Border.BorderBrushProperty, "AppBorderStrong");
            swatch.BorderThickness = new Thickness(1);
            swatch.ToolTip = $"Use recent color #{argb:X8}";
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Use recent color #{argb:X8}");
            swatch.MouseLeftButtonDown += (_, _) => SetActiveColor(captured);
            RecentColors.Children.Add(swatch);
        }
    }

    private void OnSaveSwatchClicked(object sender, RoutedEventArgs e)
    {
        if (App.Host is null) return;
        var swatches = App.Host.Settings.Current.SavedColorSwatches;
        if (!swatches.Contains(_activeColor))
        {
            swatches.Add(_activeColor);
            App.Host.Settings.Save();
        }
        UpdateSavedSwatches();
    }

    private void UpdateSavedSwatches()
    {
        SavedSwatches.Children.Clear();
        var swatches = App.Host?.Settings.Current.SavedColorSwatches;
        if (swatches is null) return;
        foreach (var argb in swatches)
        {
            uint captured = argb;
            var swatch = new Border
            {
                Width = 22, Height = 22,
                Margin = new Thickness(2),
                Background = new SolidColorBrush(ToWpfColor(argb)),
                CornerRadius = new CornerRadius(3),
                Cursor = Cursors.Hand
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "AppBorderStrong");
            swatch.BorderThickness = new Thickness(1);
            swatch.ToolTip = $"Use saved color #{argb:X8}";
            System.Windows.Automation.AutomationProperties.SetName(swatch, $"Use saved color #{argb:X8}");
            swatch.MouseLeftButtonDown += (_, _) => SetActiveColor(captured);
            SavedSwatches.Children.Add(swatch);
        }
    }

    private static Color ToWpfColor(uint argb) =>
        Color.FromArgb((byte)((argb >> 24) & 0xFF), (byte)((argb >> 16) & 0xFF), (byte)((argb >> 8) & 0xFF), (byte)(argb & 0xFF));

    // ---- Hotkeys --------------------------------------------------------------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (e.Key == Key.Z) { _selectedShapes.Clear(); _commands.Undo(_doc); Canvas.InvalidateVisual(); e.Handled = true; return; }
            if (e.Key == Key.Y) { _selectedShapes.Clear(); _commands.Redo(_doc); Canvas.InvalidateVisual(); e.Handled = true; return; }
            if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            { SelectAllOfType(); e.Handled = true; return; }
            if (e.Key == Key.A) { SelectAll(); e.Handled = true; return; }
            if (e.Key == Key.S) { OnSaveProjectClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.E) { OnExportPngClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.O) { OnOpenClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.C) { OnCopyClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
            if (e.Key == Key.D) { DuplicateSelectedShapes(); e.Handled = true; return; }
            if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            { OnPasteDiagramClicked(this, new RoutedEventArgs()); e.Handled = true; return; }
        }
        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None && CanToggleOptionsPanel())
        {
            ToggleOptionsPanel();
            e.Handled = true;
            return;
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
        if (e.Key == Key.Escape && _cropSelection is not null)
        {
            _cropSelection = null;
            _dragging = false;
            StatusText.Text = "Crop cancelled";
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _selectedShapes.Count > 0)
        {
            _selectedShapes.Clear();
            StatusText.Text = $"Tool: {_activeTool}";
            Canvas.InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete)
        {
            if (_selectedShapes.Count > 0)
            {
                // Delete all selected shapes as one undoable operation.
                var removeCmds = _selectedShapes
                    .Select(s => (AnnotationCommand)new RemoveShapeCommand(s))
                    .ToList();
                if (removeCmds.Count == 1)
                    _commands.Do(_doc, removeCmds[0]);
                else
                    _commands.Do(_doc, new CompositeCommand(removeCmds));
                _selectedShapes.Clear();
                Canvas.InvalidateVisual();
            }
            else if (_doc.Shapes.Count > 0)
            {
                // Fallback: delete last shape.
                var last = _doc.Shapes[^1];
                _commands.Do(_doc, new RemoveShapeCommand(last));
                Canvas.InvalidateVisual();
            }
        }
    }

    private void SelectAll()
    {
        _selectedShapes.Clear();
        foreach (var s in _doc.Shapes) _selectedShapes.Add(s);
        int count = _selectedShapes.Count;
        StatusText.Text = count == 0 ? "No shapes" : $"{count} shapes selected";
        Canvas.InvalidateVisual();
    }

    private void SelectAllOfType()
    {
        if (_selectedShapes.Count == 0) { SelectAll(); return; }
        var type = _selectedShapes.First().GetType();
        _selectedShapes.Clear();
        foreach (var s in _doc.Shapes)
            if (s.GetType() == type) _selectedShapes.Add(s);
        StatusText.Text = $"{_selectedShapes.Count} {type.Name.Replace("Shape", "")}(s) selected";
        Canvas.InvalidateVisual();
    }

    private void DuplicateSelectedShapes()
    {
        // Determine which shapes to duplicate: selected shapes, or fall back to the last shape.
        var targets = _selectedShapes.Count > 0
            ? _doc.Shapes.Where(s => _selectedShapes.Contains(s)).ToList()
            : _doc.Shapes.Count > 0
                ? new List<Shape> { _doc.Shapes[^1] }
                : new List<Shape>();

        if (targets.Count == 0) return;

        var clones = new List<Shape>();
        var addCmds = new List<AnnotationCommand>();
        foreach (var original in targets)
        {
            var clone = original.Clone();
            clone.Offset(10, 10);
            clones.Add(clone);
            addCmds.Add(new AddShapeCommand(clone));
        }

        if (addCmds.Count == 1)
            _commands.Do(_doc, addCmds[0]);
        else
            _commands.Do(_doc, new CompositeCommand(addCmds));

        // Move selection to the new clones
        _selectedShapes.Clear();
        foreach (var c in clones) _selectedShapes.Add(c);

        int count = clones.Count;
        StatusText.Text = count == 1 ? "Duplicated 1 shape" : $"Duplicated {count} shapes";
        Canvas.InvalidateVisual();
    }

    // ---- Drag-and-drop -------------------------------------------------------

    private static readonly HashSet<string> AcceptedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", SnapFileFormat.Extension
    };

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Any(f => AcceptedImageExtensions.Contains(Path.GetExtension(f))))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        foreach (var file in files)
        {
            if (!AcceptedImageExtensions.Contains(Path.GetExtension(file)))
                continue;
            try
            {
                EditorTabHostWindow.Open(new EditorWindow(file));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open dropped file:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (_cropSelection is { } cropSelection)
        {
            var crop = CropMath.NormalizeAndSnap(cropSelection, _doc.Width, _doc.Height, snapToEdges: false);
            using var shade = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(17, 17, 27, 110) };
            canvas.DrawRect(new SKRect(0, 0, _doc.Width, crop.Top), shade);
            canvas.DrawRect(new SKRect(0, crop.Bottom, _doc.Width, _doc.Height), shade);
            canvas.DrawRect(new SKRect(0, crop.Top, crop.Left, crop.Bottom), shade);
            canvas.DrawRect(new SKRect(crop.Right, crop.Top, _doc.Width, crop.Bottom), shade);

            using var outline = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(203, 166, 247),
                StrokeWidth = 2,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 8f, 5f }, 0)
            };
            canvas.DrawRect(crop, outline);
        }

        // Draw selection handles around selected shapes
        if (_selectedShapes.Count > 0)
        {
            using var selPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(59, 130, 246),
                StrokeWidth = 1.5f,
                IsAntialias = true,
                PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0)
            };
            using var handleFill = new SKPaint { Style = SKPaintStyle.Fill, Color = SKColors.White, IsAntialias = true };
            using var handleStroke = new SKPaint { Style = SKPaintStyle.Stroke, Color = new SKColor(59, 130, 246), StrokeWidth = 1.5f, IsAntialias = true };
            foreach (var shape in _selectedShapes)
            {
                var bounds = shape.GetBounds();
                bounds.Inflate(4, 4);
                canvas.DrawRect(bounds, selPaint);

                if (_selectedShapes.Count == 1)
                {
                    foreach (var pt in GetHandlePoints(shape.GetBounds()))
                    {
                        canvas.DrawCircle(pt, HandleRadius, handleFill);
                        canvas.DrawCircle(pt, HandleRadius, handleStroke);
                    }
                }
            }
        }
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
        Canvas.Focus();
        var pos = ToImagePoint(e.GetPosition(Canvas));

        if (_activeTool == EditorTool.Crop)
        {
            _selectedShapes.Clear();
            _draftShape = null;
            _activeHandle = HandlePosition.None;
            _cropSelection = new SKRect(pos.X, pos.Y, pos.X, pos.Y);
            _dragStart = pos;
            _dragging = true;
            Canvas.InvalidateVisual();
            return;
        }

        if (_activeTool == EditorTool.Eyedropper)
        {
            int px = Math.Clamp((int)pos.X, 0, _doc.Width - 1);
            int py = Math.Clamp((int)pos.Y, 0, _doc.Height - 1);
            var pixel = _doc.Background.GetPixel(px, py);
            uint argb = (uint)((pixel.Alpha << 24) | (pixel.Red << 16) | (pixel.Green << 8) | pixel.Blue);
            SetActiveColor(argb);
            StatusText.Text = $"Picked: #{argb:X8}";
            return;
        }

        if (_activeTool == EditorTool.Select)
        {
            // Check transform handles first (single selection only)
            var handle = HitTestHandles(pos);
            if (handle != HandlePosition.None)
            {
                _activeHandle = handle;
                _handleShape = _selectedShapes.First();
                _handleShapeSnapshot = _handleShape.Clone();
                _handleOriginalBounds = _handleShape.GetBounds();
                _dragStart = pos;
                _dragging = true;
                Canvas.InvalidateVisual();
                return;
            }

            // Hit-test shapes in reverse (top-most first)
            Shape? hit = null;
            for (int i = _doc.Shapes.Count - 1; i >= 0; i--)
            {
                if (_doc.Shapes[i].HitTest(pos))
                {
                    hit = _doc.Shapes[i];
                    break;
                }
            }

            if (hit is not null)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    if (!_selectedShapes.Remove(hit))
                        _selectedShapes.Add(hit);
                }
                else if (!_selectedShapes.Contains(hit))
                {
                    _selectedShapes.Clear();
                    _selectedShapes.Add(hit);
                }
                // Start move drag
                _activeHandle = HandlePosition.Move;
                _handleShape = hit;
                _handleShapeSnapshot = hit.Clone();
                _handleOriginalBounds = hit.GetBounds();
                _dragStart = pos;
                _dragging = true;
                int count = _selectedShapes.Count;
                StatusText.Text = count == 1 ? "1 shape selected" : $"{count} shapes selected";
            }
            else if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _selectedShapes.Clear();
                StatusText.Text = "Tool: Select";
            }
            Canvas.InvalidateVisual();
            return;
        }

        _dragStart = pos;
        _dragging = true;
        _draftShape = CreateDraftShape(pos);
    }

    private void OnCanvasRightMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _dragging = false;
        _draftShape = null;
        _activeHandle = HandlePosition.None;
        _handleShape = null;
        _handleShapeSnapshot = null;

        var position = ToImagePoint(e.GetPosition(Canvas));
        var hit = FindShapeAt(position);
        Shape[] targets = Array.Empty<Shape>();
        if (hit is not null)
        {
            targets = _selectedShapes.Contains(hit) && _selectedShapes.Count > 1
                ? _doc.Shapes.Where(_selectedShapes.Contains).ToArray()
                : new[] { hit! };
        }
        ShowColorWheel(position, targets);
    }

    private Shape? FindShapeAt(SKPoint position)
    {
        for (int i = _doc.Shapes.Count - 1; i >= 0; i--)
        {
            if (_doc.Shapes[i].HitTest(position))
                return _doc.Shapes[i];
        }
        return null;
    }

    private void ShowColorWheel(SKPoint imagePosition, IReadOnlyList<Shape> targets)
    {
        _colorWheelPopup?.IsOpen = false;

        uint initialColor = targets.Count > 0 ? targets[0].StrokeColorArgb : _activeColor;
        var wheel = new ColorWheelControl { SelectedColorArgb = initialColor };
        var panel = new Border
        {
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromArgb(248, 28, 31, 38)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(230, 81, 89, 110)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Child = wheel
        };
        panel.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 18,
            ShadowDepth = 5,
            Opacity = 0.55,
            Color = Colors.Black
        };

        var popup = new Popup
        {
            Child = panel,
            PlacementTarget = Canvas,
            Placement = PlacementMode.RelativePoint,
            StaysOpen = false,
            AllowsTransparency = true,
            Focusable = false,
            HorizontalOffset = Math.Clamp(imagePosition.X - 130, 0, Math.Max(0, Canvas.ActualWidth - 260)),
            VerticalOffset = Math.Clamp(imagePosition.Y - 130, 0, Math.Max(0, Canvas.ActualHeight - 260))
        };
        wheel.ColorSelected += (_, color) =>
        {
            SetActiveColor(color);
            if (targets.Count > 0)
            {
                _commands.Do(_doc, new SetShapeColorCommand(targets, color));
                StatusText.Text = targets.Count == 1
                    ? $"Recolored {targets[0].GetType().Name.Replace("Shape", "")}: #{color:X8}"
                    : $"Recolored {targets.Count} shapes: #{color:X8}";
                Canvas.InvalidateVisual();
            }
            else
            {
                StatusText.Text = $"Color: #{color:X8} — ready to draw";
            }
            popup.IsOpen = false;
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_colorWheelPopup, popup))
                _colorWheelPopup = null;
        };
        _colorWheelPopup = popup;
        popup.IsOpen = true;
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var pos = ToImagePoint(e.GetPosition(Canvas));

        if (_activeHandle != HandlePosition.None && _handleShape is not null && _handleShapeSnapshot is not null)
        {
            // Restore shape to snapshot state, then apply the handle drag
            var snapshotBounds = _handleShapeSnapshot.GetBounds();
            if (_activeHandle == HandlePosition.Move)
            {
                float dx = pos.X - _dragStart.X;
                float dy = pos.Y - _dragStart.Y;
                var restored = _handleShapeSnapshot.Clone();
                restored.Offset(dx, dy);
                _handleShape.ResizeTo(restored.GetBounds());
                // Also move other selected shapes
                foreach (var s in _selectedShapes)
                {
                    if (s == _handleShape) continue;
                    // Not ideal for multi-select move, but functional
                }
            }
            else
            {
                var newBounds = ApplyHandleDrag(_activeHandle, _handleOriginalBounds, _dragStart, pos);
                _handleShape.ResizeTo(newBounds);
            }
            Canvas.InvalidateVisual();
            return;
        }

        if (_activeTool == EditorTool.Crop && _cropSelection is not null)
        {
            _cropSelection = new SKRect(_dragStart.X, _dragStart.Y, pos.X, pos.Y);
            Canvas.InvalidateVisual();
            return;
        }

        if (_draftShape is null) return;
        UpdateDraft(_draftShape, _dragStart, pos);
        Canvas.InvalidateVisual();
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;

        if (_activeHandle != HandlePosition.None)
        {
            _activeHandle = HandlePosition.None;
            _handleShape = null;
            _handleShapeSnapshot = null;
            Canvas.InvalidateVisual();
            return;
        }

        if (_activeTool == EditorTool.Crop)
        {
            var selection = _cropSelection;
            _cropSelection = null;
            if (selection is { } cropSelection)
                ApplyCrop(cropSelection);
            Canvas.InvalidateVisual();
            return;
        }

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

    private static SKPoint[] GetHandlePoints(SKRect bounds)
    {
        bounds.Inflate(4, 4);
        float mx = bounds.MidX, my = bounds.MidY;
        return new[]
        {
            new SKPoint(bounds.Left, bounds.Top),     // TopLeft
            new SKPoint(mx, bounds.Top),              // Top
            new SKPoint(bounds.Right, bounds.Top),    // TopRight
            new SKPoint(bounds.Right, my),            // Right
            new SKPoint(bounds.Right, bounds.Bottom), // BottomRight
            new SKPoint(mx, bounds.Bottom),           // Bottom
            new SKPoint(bounds.Left, bounds.Bottom),  // BottomLeft
            new SKPoint(bounds.Left, my),             // Left
        };
    }

    private HandlePosition HitTestHandles(SKPoint pos)
    {
        if (_selectedShapes.Count != 1) return HandlePosition.None;
        var shape = _selectedShapes.First();
        var pts = GetHandlePoints(shape.GetBounds());
        HandlePosition[] positions = { HandlePosition.TopLeft, HandlePosition.Top, HandlePosition.TopRight,
            HandlePosition.Right, HandlePosition.BottomRight, HandlePosition.Bottom,
            HandlePosition.BottomLeft, HandlePosition.Left };
        for (int i = 0; i < pts.Length; i++)
        {
            if ((pts[i] - pos).Length <= HandleRadius + 3)
                return positions[i];
        }
        return HandlePosition.None;
    }

    private static SKRect ApplyHandleDrag(HandlePosition handle, SKRect original, SKPoint dragStart, SKPoint dragCurrent)
    {
        float dx = dragCurrent.X - dragStart.X;
        float dy = dragCurrent.Y - dragStart.Y;
        float l = original.Left, t = original.Top, r = original.Right, b = original.Bottom;
        switch (handle)
        {
            case HandlePosition.TopLeft:     l += dx; t += dy; break;
            case HandlePosition.Top:         t += dy; break;
            case HandlePosition.TopRight:    r += dx; t += dy; break;
            case HandlePosition.Right:       r += dx; break;
            case HandlePosition.BottomRight: r += dx; b += dy; break;
            case HandlePosition.Bottom:      b += dy; break;
            case HandlePosition.BottomLeft:  l += dx; b += dy; break;
            case HandlePosition.Left:        l += dx; break;
            case HandlePosition.Move:        l += dx; t += dy; r += dx; b += dy; break;
        }
        if (l > r) (l, r) = (r, l);
        if (t > b) (t, b) = (b, t);
        return new SKRect(l, t, r, b);
    }

    private SKPoint ToImagePoint(System.Windows.Point p) => new((float)p.X, (float)p.Y);

    private Shape? CreateDraftShape(SKPoint p)
    {
        bool dashed = DashedCheck.IsChecked == true;
        bool bidir = BidirCheck.IsChecked == true;
        bool filled = FilledCheck.IsChecked == true;
        bool pixelate = PixelateCheck.IsChecked == true;
        bool shadow = DropShadowCheck.IsChecked == true;
        var shape = _activeTool switch
        {
            EditorTool.Rectangle => (Shape)new RectangleShape { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Filled = filled },
            EditorTool.Ellipse   => new EllipseShape     { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Filled = filled },
            EditorTool.Line      => new LineShape        { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Dashed = dashed },
            EditorTool.Arrow     => new ArrowShape       { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Dashed = dashed, Bidirectional = bidir, Reversed = ReversedCheck.IsChecked == true, Style = _arrowStyle, Curve = _arrowCurve },
            EditorTool.Freehand  => new FreehandShape    { Points = new() { p }, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness },
            EditorTool.Text      => new TextShape        { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, Text = PromptForText() ?? "", Orientation = _textOrientation },
            EditorTool.SpeechBalloon => new SpeechBalloonShape { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, FillColorArgb = (_activeColor & 0x00FFFFFF) | 0x33000000, CornerRadius = _balloonCornerRadius },
            EditorTool.Highlight => new HighlightShape   { X = p.X, Y = p.Y, StrokeColorArgb = 0xFFFFD43B },
            EditorTool.LineStateMarker => new LineStateMarkerShape { X = p.X, Y = p.Y, State = _lineState, StrokeColorArgb = _activeColor },
            EditorTool.Blur      => new BlurShape        { X = p.X, Y = p.Y, BlurRadius = Math.Max(8, _strokeThickness * 4), Pixelate = pixelate },
            EditorTool.Redact    => new RedactShape      { X = p.X, Y = p.Y },
            EditorTool.Step      => new StepShape        { X = p.X, Y = p.Y, Label = _stepCounter.ToString(), StrokeColorArgb = _activeColor, Radius = Math.Max(14, _strokeThickness * 5) },
            EditorTool.Spotlight => new SpotlightShape   { X = p.X, Y = p.Y },
            EditorTool.Ruler     => new RulerShape       { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness },
            EditorTool.Crop      => null,
            _ => null,
        };
        if (shape is not null)
        {
            shape.DropShadow = shadow;
            shape.Sloppiness = _sloppiness;
            shape.Category = _annotationCategory;
        }
        return shape;
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
            case RulerShape ru:  ru.X2 = b.X; ru.Y2 = b.Y; break;
            case FreehandShape f:
                if (f.Points.Count == 0 || (f.Points[^1] - b).Length > 0.5f) f.Points.Add(b);
                break;
            case SpeechBalloonShape sb:
                sb.X = Math.Min(a.X, b.X); sb.Y = Math.Min(a.Y, b.Y);
                sb.Width = Math.Abs(b.X - a.X); sb.Height = Math.Abs(b.Y - a.Y);
                break;
            case HighlightShape h:
                h.X = Math.Min(a.X, b.X); h.Y = Math.Min(a.Y, b.Y);
                h.Width = Math.Abs(b.X - a.X); h.Height = Math.Abs(b.Y - a.Y);
                break;
            case LineStateMarkerShape lm:
                lm.X = Math.Min(a.X, b.X); lm.Y = Math.Min(a.Y, b.Y);
                lm.Width = Math.Abs(b.X - a.X); lm.Height = Math.Abs(b.Y - a.Y);
                break;
            case BlurShape bs:
                bs.X = Math.Min(a.X, b.X); bs.Y = Math.Min(a.Y, b.Y);
                bs.Width = Math.Abs(b.X - a.X); bs.Height = Math.Abs(b.Y - a.Y);
                break;
            case RedactShape r2:
                r2.X = Math.Min(a.X, b.X); r2.Y = Math.Min(a.Y, b.Y);
                r2.Width = Math.Abs(b.X - a.X); r2.Height = Math.Abs(b.Y - a.Y);
                break;
            case SpotlightShape sp:
                sp.X = Math.Min(a.X, b.X); sp.Y = Math.Min(a.Y, b.Y);
                sp.Width = Math.Abs(b.X - a.X); sp.Height = Math.Abs(b.Y - a.Y);
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
        SpeechBalloonShape sb => sb.Width >= 4 && sb.Height >= 4,
        HighlightShape h   => h.Width >= 4 && h.Height >= 4,
        LineStateMarkerShape lm => lm.Width >= 4 && lm.Height >= 4,
        BlurShape b        => b.Width >= 4 && b.Height >= 4,
        RedactShape r      => r.Width >= 4 && r.Height >= 4,
        StepShape          => true,
        SpotlightShape sp  => sp.Width >= 4 && sp.Height >= 4,
        RulerShape ru      => Math.Abs(ru.X2 - ru.X1) + Math.Abs(ru.Y2 - ru.Y1) >= 4,
        _                  => false,
    };

    private string? PromptForText()
    {
        var dlg = new TextInputDialog { Owner = DialogOwner };
        return dlg.ShowDialog() == true ? dlg.InputText : null;
    }

    // ---- Toolbar handlers -----------------------------------------------------

    private void OnUndoClicked(object sender, RoutedEventArgs e) { _commands.Undo(_doc); Canvas.InvalidateVisual(); }
    private void OnRedoClicked(object sender, RoutedEventArgs e) { _commands.Redo(_doc); Canvas.InvalidateVisual(); }
    private void OnOptionsClicked(object sender, RoutedEventArgs e) => ToggleOptionsPanel();

    private void ToggleOptionsPanel()
    {
        _optionsPanelVisible = !_optionsPanelVisible;
        OptionsPanelBorder.Visibility = _optionsPanelVisible ? Visibility.Visible : Visibility.Collapsed;
        OptionsColumn.Width = _optionsPanelVisible ? new GridLength(OptionsPanelWidth) : new GridLength(0);
        StatusText.Text = _optionsPanelVisible
            ? $"Tool: {_activeTool} · options shown"
            : "Options hidden · press Space to show";
    }

    private static bool CanToggleOptionsPanel() => Keyboard.FocusedElement is not
        (ButtonBase or TextBoxBase or PasswordBox or ComboBox or Slider or ScrollBar);

    private void OnStrokeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => _strokeThickness = (float)e.NewValue;
    private void OnSloppinessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _sloppiness = (float)Math.Clamp(e.NewValue / 100.0, 0, 1);
        if (SloppinessValue is not null)
            SloppinessValue.Text = $"{e.NewValue:0}%";
        if (_draftShape is not null)
            _draftShape.Sloppiness = _sloppiness;
        Canvas.InvalidateVisual();
    }

    private void ApplyCrop(SKRect selection)
    {
        _cropSelection = null;
        _dragging = false;
        var crop = CropMath.NormalizeAndSnap(selection, _doc.Width, _doc.Height, SnapCropCheck.IsChecked == true);
        if (crop.Width < 4 || crop.Height < 4)
        {
            StatusText.Text = "Crop selection is too small.";
            return;
        }

        if (crop.Left == 0 && crop.Top == 0 && crop.Right == _doc.Width && crop.Bottom == _doc.Height)
        {
            StatusText.Text = "Crop already covers the whole image.";
            return;
        }

        _commands.Do(_doc, new CropDocumentCommand(_doc, crop));
        _selectedShapes.Clear();
        Canvas.Width = _doc.Width;
        Canvas.Height = _doc.Height;
        DimensionText.Text = $"{_doc.Width} × {_doc.Height}";
        StatusText.Text = $"Cropped to {_doc.Width} × {_doc.Height}";
    }

    private void OnArrowStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArrowStyleCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<ArrowStyle>(tag, out var style))
            _arrowStyle = style;
    }
    private void OnArrowCurveChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _arrowCurve = (float)Math.Clamp(e.NewValue / 100.0, -1, 1);
        if (ArrowCurveValue is not null)
            ArrowCurveValue.Text = $"{e.NewValue:0}%";
    }
    private void OnTextOrientationChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TextOrientationCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<TextOrientation>(tag, out var orientation))
            _textOrientation = orientation;
    }
    private void OnBalloonCornerRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _balloonCornerRadius = (float)Math.Clamp(e.NewValue, 0, 64);
        if (BalloonCornerRadiusValue is not null)
            BalloonCornerRadiusValue.Text = $"{e.NewValue:0}px";
    }
    private void OnLineMarkerStateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LineMarkerStateCombo.SelectedItem is ComboBoxItem { Tag: string tag } &&
            Enum.TryParse<LineState>(tag, out var state))
            _lineState = state;
    }
    private void OnAnnotationCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<AnnotationCategory>(tag, out var category))
            return;

        _annotationCategory = category;
        if (_selectedShapes.Count == 0)
            return;

        var targets = _doc.Shapes.Where(_selectedShapes.Contains).ToArray();
        if (targets.Length == 0) return;
        _commands.Do(_doc, new SetShapeCategoryCommand(targets, category));
        StatusText.Text = category == AnnotationCategory.None
            ? $"Removed category from {targets.Length} shape(s)"
            : $"Tagged {targets.Length} shape(s) as {category}";
        Canvas.InvalidateVisual();
    }
    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        byte alpha = (byte)Math.Clamp((int)e.NewValue, 0, 255);
        _activeColor = (_activeColor & 0x00FFFFFF) | ((uint)alpha << 24);
    }
    private void OnAdjustmentChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Canvas.InvalidateVisual();
    private void OnAdjustmentClicked(object sender, RoutedEventArgs e) => Canvas.InvalidateVisual();
    private void OnFrameClicked(object sender, RoutedEventArgs e) => Canvas.InvalidateVisual();

    private async void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        var path = await StoragePickerService.PickOpenFileAsync(
            DialogOwner,
            "Snapture project (*.snapture)|*.snapture|Image (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            new[] { ".snapture", ".png", ".jpg", ".jpeg", ".bmp" },
            title: "Open a Snapture project or image");
        if (path is null) return;
        try
        {
            EditorTabHostWindow.Open(new EditorWindow(path));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnPasteDiagramClicked(object sender, RoutedEventArgs e)
    {
        string clipboardText = "";
        try
        {
            if (Clipboard.ContainsText())
                clipboardText = Clipboard.GetText(TextDataFormat.Text);
        }
        catch
        {
            // Clipboard access can be denied by another process; the input dialog remains available.
        }

        if (TryInsertDiagram(clipboardText)) return;

        var dialog = new DiagramMarkupDialog(clipboardText) { Owner = DialogOwner };
        if (dialog.ShowDialog() == true)
            TryInsertDiagram(dialog.Markup);
    }

    private bool TryInsertDiagram(string markup)
    {
        if (!DiagramMarkupParser.TryParse(markup, out var definition, out var error) || definition is null)
        {
            if (!string.IsNullOrWhiteSpace(markup))
                StatusText.Text = $"Diagram not imported: {error}";
            return false;
        }

        float maxWidth = Math.Max(120, _doc.Width - 40);
        float maxHeight = Math.Max(100, _doc.Height - 40);
        float scale = Math.Min(1, Math.Min(maxWidth / definition.Width, maxHeight / definition.Height));
        float width = definition.Width * scale;
        float height = definition.Height * scale;
        float x = Math.Max(0, (_doc.Width - width) / 2);
        float y = Math.Max(0, (_doc.Height - height) / 2);
        var diagram = DiagramShape.FromDefinition(definition, markup, x, y);
        if (scale < 1)
            diagram.ResizeTo(new SKRect(x, y, x + width, y + height));
        diagram.StrokeThickness = _strokeThickness;
        diagram.Sloppiness = _sloppiness;
        diagram.DropShadow = DropShadowCheck.IsChecked == true;
        diagram.Category = _annotationCategory;
        _commands.Do(_doc, new AddShapeCommand(diagram));
        _selectedShapes.Clear();
        _selectedShapes.Add(diagram);
        StatusText.Text = $"Pasted {definition.Kind} diagram · {definition.Nodes.Count} nodes, {definition.Edges.Count} connections";
        Canvas.InvalidateVisual();
        return true;
    }

    private async void OnSaveProjectClicked(object sender, RoutedEventArgs e)
    {
        if (_projectPath is null)
        {
            _projectPath = await StoragePickerService.PickSaveFileAsync(
                DialogOwner,
                "Snapture project (*.snapture)|*.snapture",
                $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.snapture",
                ".snapture",
                new[]
                {
                    new StoragePickerService.FileTypeChoice(
                        "Snapture project",
                        new[] { ".snapture" })
                },
                title: "Save the Snapture project");
            if (_projectPath is null) return;
        }
        try
        {
            SnapFileFormat.Save(_projectPath, _doc);
            StatusText.Text = $"Project saved: {Path.GetFileName(_projectPath)}";
            PathText.Text = _projectPath;
            NotifyDocumentTitleChanged();
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

    private async void OnExportAsClicked(object sender, RoutedEventArgs e)
    {
        var path = await StoragePickerService.PickSaveFileAsync(
            DialogOwner,
            "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp|WebP (*.webp)|*.webp|SVG (*.svg)|*.svg",
            $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png",
            ".png",
            new[]
            {
                new StoragePickerService.FileTypeChoice("PNG", new[] { ".png" }),
                new StoragePickerService.FileTypeChoice("JPEG", new[] { ".jpg", ".jpeg" }),
                new StoragePickerService.FileTypeChoice("BMP", new[] { ".bmp" }),
                new StoragePickerService.FileTypeChoice("WebP", new[] { ".webp" }),
                new StoragePickerService.FileTypeChoice("SVG", new[] { ".svg" })
            },
            title: "Export capture");
        if (path is null) return;
        ExportTo(path);
        _exportPath = path;
        NotifyDocumentTitleChanged();
    }

    private void ExportTo(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                ExportSvg(path);
                return;
            }
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
            MarkHistoryExport(path);
            StatusText.Text = $"Exported {Path.GetFileName(path)}";
            PathText.Text = path;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportSvg(string path)
    {
        try
        {
            using var stream = File.Create(path);
            using var svgCanvas = SKSvgCanvas.Create(
                new SKRect(0, 0, _doc.Width, _doc.Height), stream);
            // Draw background as embedded PNG
            using var bgImage = SKImage.FromBitmap(_doc.Background);
            svgCanvas.DrawImage(bgImage, 0, 0);
            // Draw all shapes
            foreach (var shape in _doc.Shapes)
                shape.Render(svgCanvas, _doc);
            MarkHistoryExport(path);
            StatusText.Text = $"Exported {Path.GetFileName(path)}";
            PathText.Text = path;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export SVG:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void MarkHistoryExport(string path)
    {
        try
        {
            var history = App.Host?.History;
            if (history is null)
                return;

            bool verified = _autoRedactionShapes.Any(_doc.Shapes.Contains);
            int matches = history.SetVerifiedRedacted(path, verified);
            if (!verified || matches > 0 || !IsRasterExport(path))
                return;

            history.Add(
                path,
                "Editor",
                "Snapture",
                Path.GetFileName(path),
                _doc.Width,
                _doc.Height,
                ocrText: null,
                verifiedRedacted: true);
        }
        catch
        {
            // History metadata must never turn a successful export into a failed save.
        }
    }

    private static bool IsRasterExport(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";

    private SKBitmap RenderForExport()
    {
        // Render document at full size with current adjustments + frame applied.
        using var doc = _doc.RenderToBitmap();
        ApplyAdjustments(doc);

        bool beautifier = BeautifierCheck.IsChecked == true;
        bool gradient = GradientCheck.IsChecked == true || beautifier;
        bool shadow = ShadowCheck.IsChecked == true || beautifier;
        bool rounded = RoundedCheck.IsChecked == true || beautifier;
        bool codeChrome = CodeChromeCheck.IsChecked == true;

        int chromeBar = codeChrome ? 36 : 0;
        int padding = beautifier ? 120 : (gradient || shadow || codeChrome) ? 80 : 0;
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
                PortableMode.LocalDataDirectory, "share");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"share_{DateTime.UtcNow:yyyyMMddHHmmssfff}.png");
            using (var fs = System.IO.File.Create(path)) data.SaveTo(fs);

            var ttl = TimeSpan.FromMinutes(App.Host.Settings.Current.LanShareTtlMinutes);
            var url = App.Host.LanShare.Register(path, ttl);
            try { Clipboard.SetText(url); } catch { }
            StatusText.Text = $"LAN URL copied: {url} (expires in {ttl.TotalMinutes:F0}m)";
            var qr = new QrCodeWindow(url) { Owner = this };
            qr.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Share failed: {ex.Message}";
        }
    }

    private async void OnRetakeClicked(object sender, RoutedEventArgs e)
    {
        if (_captureResult is null || App.Host is null) return;

        bool preserveAnnotations = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (preserveAnnotations)
        {
            await RefreshCapturePreservingAnnotations();
            return;
        }

        if (_tabHost is { } host)
            host.CloseDocument(this);
        else
            Close();
        try
        {
            var orch = App.Host.Orchestrator;
            if (_captureResult.SourceWindow is { } hwnd && hwnd != 0)
                await orch.CaptureWindowPickerAsync();
            else if (_captureResult.Source == "VirtualScreen" || _captureResult.Source == "Fullscreen")
                await orch.CaptureFullscreenAsync();
            else
                await orch.CaptureRegionAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Retake failed:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshCapturePreservingAnnotations()
    {
        if (_captureResult is null || App.Host is null) return;
        try
        {
            StatusText.Text = "Recapturing…";
            CaptureResult? newCapture = null;
            var engine = App.Host.Engine;
            if (_captureResult.SourceWindow is { } hwnd && hwnd != 0)
                newCapture = await engine.CaptureWindowAsync(hwnd);
            else if (_captureResult.Source == "VirtualScreen" || _captureResult.Source == "Fullscreen")
                newCapture = await engine.CaptureVirtualScreenAsync();
            else if (_captureResult.SourceBounds is { Width: > 0, Height: > 0 } bounds)
                newCapture = await engine.CaptureRegionAsync(bounds);

            if (newCapture is null)
            {
                StatusText.Text = "Refresh failed — could not recapture.";
                return;
            }

            nint hbmp = newCapture.Bitmap.GetHbitmap();
            try
            {
                var bs = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hbmp, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                var newBg = BitmapSourceToSKBitmap(bs);
                _doc.ReplaceBackground(newBg);
            }
            finally { DeleteObject(hbmp); }

            _captureResult = newCapture;
            Canvas.InvalidateVisual();
            StatusText.Text = "Refreshed — annotations preserved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Refresh failed: {ex.Message}";
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint hObject);

    private async void OnAutoRedactClicked(object sender, RoutedEventArgs e)
    {
        AutoRedactButton.IsEnabled = false;
        StatusText.Text = "Scanning for secrets…";
        try
        {
            using var flat = _doc.RenderToBitmap();
            var disabled = App.Host?.Settings.Current.DisabledRedactRules is { Count: > 0 } d
                ? new HashSet<string>(d, StringComparer.OrdinalIgnoreCase)
                : null;
            var findings = await AutoRedactor.ScanAsync(flat, disabled);
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
            _autoRedactionShapes.UnionWith(added_shapes);
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

    private async void OnOcrOverlayClicked(object sender, RoutedEventArgs e)
    {
        OcrOverlayButton.IsEnabled = false;
        StatusText.Text = "Reading positioned text…";
        try
        {
            using var flat = _doc.RenderToBitmap();
            var result = await OcrService.RecognizeAsync(flat);
            if (result is null || string.IsNullOrWhiteSpace(result.Text))
            {
                StatusText.Text = "No text recognized.";
                return;
            }

            var overlays = OcrOverlayBuilder.CreateShapes(result, flat);
            if (overlays.Count == 0)
            {
                StatusText.Text = $"{result.Engine} returned text without image positions.";
                return;
            }

            var commands = overlays.Select(shape => (AnnotationCommand)new AddShapeCommand(shape)).ToList();
            _commands.Do(_doc, commands.Count == 1 ? commands[0] : new CompositeCommand(commands));
            _selectedShapes.Clear();
            Canvas.InvalidateVisual();
            StatusText.Text = $"Added {overlays.Count} OCR text overlays ({result.Engine}).";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR overlay failed: {ex.Message}";
        }
        finally
        {
            OcrOverlayButton.IsEnabled = true;
        }
    }

    private async void OnLocalAiClicked(object sender, RoutedEventArgs e)
    {
        LocalAiButton.IsEnabled = false;
        StatusText.Text = "Discovering local models…";
        try
        {
            var providers = await LocalAiProviderService.DiscoverAsync();
            var choices = LocalAiProviderService.GetModelChoices(providers);
            if (choices.Count == 0)
            {
                StatusText.Text = "No local models detected. Start a local runtime, then try again.";
                return;
            }

            var picker = new LocalAiModelPickerWindow(providers) { Owner = DialogOwner };
            if (picker.ShowDialog() != true || picker.SelectedChoice is not { } choice)
            {
                StatusText.Text = "Local AI send canceled.";
                return;
            }

            using var flat = RenderForExport();
            using var image = SKImage.FromBitmap(flat);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            var prompt = picker.Prompt;
            StatusText.Text = $"Sending flattened PNG to {choice.Reference}…";
            var response = await new LocalAiInferenceService().SendImageAsync(
                choice,
                data.ToArray(),
                prompt);

            new LocalAiResultWindow(choice.Reference, response) { Owner = DialogOwner }.ShowDialog();
            StatusText.Text = $"Local AI response received from {choice.Reference}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Local AI failed: {ex.Message}";
        }
        finally
        {
            LocalAiButton.IsEnabled = true;
        }
    }

    private async void OnBarcodeClicked(object sender, RoutedEventArgs e)
    {
        BarcodeButton.IsEnabled = false;
        StatusText.Text = "Scanning for QR codes and barcodes…";
        try
        {
            using var flat = _doc.RenderToBitmap();
            var detections = await Task.Run(() => BarcodeExtractor.Extract(flat));
            StatusText.Text = detections.Count == 0
                ? "No QR codes or barcodes found."
                : $"Found {detections.Count} code{(detections.Count == 1 ? "" : "s")}.";
            var resultWindow = new BarcodeResultWindow(detections) { Owner = DialogOwner };
            resultWindow.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Code scan failed: {ex.Message}";
        }
        finally
        {
            BarcodeButton.IsEnabled = true;
        }
    }

    private async void OnOcrTableClicked(object sender, RoutedEventArgs e)
    {
        OcrTableButton.IsEnabled = false;
        StatusText.Text = "Reconstructing OCR table…";
        try
        {
            using var flat = _doc.RenderToBitmap();
            var result = await OcrService.RecognizeAsync(flat);
            if (result is null || result.Lines.All(line => line.Words.Count == 0))
            {
                StatusText.Text = "No positioned OCR words available for a table.";
                return;
            }

            var table = OcrTableBuilder.Build(result);
            StatusText.Text = table.IsEmpty
                ? "No table geometry found."
                : $"Reconstructed {table.Rows.Count} rows × {table.ColumnCount} columns.";
            new OcrTableResultWindow(table) { Owner = DialogOwner }.Show();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR table failed: {ex.Message}";
        }
        finally
        {
            OcrTableButton.IsEnabled = true;
        }
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
        Title = "Add text annotation";
        Width = 400;
        Height = 188;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock
        {
            Text = "Text annotation",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        var helper = new TextBlock
        {
            Text = "Add the label exactly as it should appear on the capture.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        helper.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        var copy = new StackPanel();
        copy.Children.Add(label);
        copy.Children.Add(helper);
        Grid.SetRow(copy, 0);
        _box = new TextBox { Margin = new Thickness(0, 0, 0, 10), MinHeight = 36 };
        Grid.SetRow(_box, 1);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "Add text", Width = 96, IsDefault = true };
        ok.SetResourceReference(StyleProperty, "AccentButton");
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        ok.Click += (_, _) => { InputText = _box.Text; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(copy);
        grid.Children.Add(_box);
        grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) => _box.Focus();
    }
}

/// <summary>Multiline local input for Mermaid or PlantUML markup.</summary>
public sealed class DiagramMarkupDialog : Window
{
    public string Markup { get; private set; }
    private readonly TextBox _box;

    public DiagramMarkupDialog(string initialMarkup)
    {
        Markup = initialMarkup;
        Title = "Paste diagram markup";
        Width = 620;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "Mermaid or PlantUML", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        Grid.SetRow(title, 0);
        grid.Children.Add(title);

        var helper = new TextBlock
        {
            Text = "Paste a flowchart/graph block or an @startuml block. The diagram is added as an editable vector group.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        helper.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(helper, 1);
        grid.Children.Add(helper);

        _box = new TextBox
        {
            Text = initialMarkup,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Segoe UI"),
            MinHeight = 220
        };
        Grid.SetRow(_box, 2);
        grid.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var import = new Button { Content = "Add diagram", Width = 104, IsDefault = true };
        import.SetResourceReference(StyleProperty, "AccentButton");
        var cancel = new Button { Content = "Cancel", Width = 80, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        import.Click += (_, _) => { Markup = _box.Text; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(import);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);
        grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) => _box.Focus();
    }
}
