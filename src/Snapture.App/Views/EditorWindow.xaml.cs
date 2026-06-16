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
using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class EditorWindow : Window
{
    public enum EditorTool
    {
        Select, Rectangle, Ellipse, Line, Arrow, Freehand, Text, Highlight, Blur, Redact, Step, Crop, Eyedropper, Spotlight, Ruler
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
        (EditorTool.Eyedropper,"Eyedropper (I)",      Key.I, "💧"),
        (EditorTool.Spotlight, "Spotlight (P)",       Key.P, "◐"),
        (EditorTool.Ruler,     "Ruler (M)",           Key.M, "📏"),
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

    // Selection model — tracks shapes the user has clicked in Select mode
    private readonly HashSet<Shape> _selectedShapes = new();

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
        KeyDown += OnKeyDown;
        Closed += OnEditorClosed;
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
        KeyDown += OnKeyDown;
        Closed += OnEditorClosed;
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
        KeyDown += OnKeyDown;
        Closed += OnEditorClosed;
        _autosave = new AutosaveService(_doc);
        Canvas.InvalidateVisual();
    }

    private void OnEditorClosed(object? sender, EventArgs e)
    {
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
                Content = glyph,
                ToolTip = tip,
                Width = 44,
                Height = 36,
                Margin = new Thickness(0, 0, 0, 4),
                Tag = tool,
                FontSize = 18
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, tip);
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
            b.SetResourceReference(Control.BackgroundProperty, active ? "AppAccent" : "AppSurfaceRaised");
            b.SetResourceReference(Control.ForegroundProperty, active ? "AppAccentForeground" : "AppForeground");
            b.SetResourceReference(Control.BorderBrushProperty, active ? "AppAccent" : "AppBorderStrong");
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
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            swatch.SetResourceReference(Border.BorderBrushProperty, "AppBorderStrong");
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
                var w = new EditorWindow(file);
                w.Show();
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
        var pos = ToImagePoint(e.GetPosition(Canvas));

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
            EditorTool.Arrow     => new ArrowShape       { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness, Dashed = dashed, Bidirectional = bidir, Reversed = ReversedCheck.IsChecked == true },
            EditorTool.Freehand  => new FreehandShape    { Points = new() { p }, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness },
            EditorTool.Text      => new TextShape        { X = p.X, Y = p.Y, StrokeColorArgb = _activeColor, Text = PromptForText() ?? "" },
            EditorTool.Highlight => new HighlightShape   { X = p.X, Y = p.Y, StrokeColorArgb = 0xFFFFD43B },
            EditorTool.Blur      => new BlurShape        { X = p.X, Y = p.Y, BlurRadius = Math.Max(8, _strokeThickness * 4), Pixelate = pixelate },
            EditorTool.Redact    => new RedactShape      { X = p.X, Y = p.Y },
            EditorTool.Step      => new StepShape        { X = p.X, Y = p.Y, Label = _stepCounter.ToString(), StrokeColorArgb = _activeColor, Radius = Math.Max(14, _strokeThickness * 5) },
            EditorTool.Spotlight => new SpotlightShape   { X = p.X, Y = p.Y },
            EditorTool.Ruler     => new RulerShape       { X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, StrokeColorArgb = _activeColor, StrokeThickness = _strokeThickness },
            EditorTool.Crop      => null,
            _ => null,
        };
        if (shape is not null) shape.DropShadow = shadow;
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
        HighlightShape h   => h.Width >= 4 && h.Height >= 4,
        BlurShape b        => b.Width >= 4 && b.Height >= 4,
        RedactShape r      => r.Width >= 4 && r.Height >= 4,
        StepShape          => true,
        SpotlightShape sp  => sp.Width >= 4 && sp.Height >= 4,
        RulerShape ru      => Math.Abs(ru.X2 - ru.X1) + Math.Abs(ru.Y2 - ru.Y1) >= 4,
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
    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        byte alpha = (byte)Math.Clamp((int)e.NewValue, 0, 255);
        _activeColor = (_activeColor & 0x00FFFFFF) | ((uint)alpha << 24);
    }
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
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp|WebP (*.webp)|*.webp|SVG (*.svg)|*.svg",
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
            StatusText.Text = $"Exported {Path.GetFileName(path)}";
            PathText.Text = path;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not export SVG:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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

    private async void OnRetakeClicked(object sender, RoutedEventArgs e)
    {
        if (_captureResult is null || App.Host is null) return;

        bool preserveAnnotations = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (preserveAnnotations)
        {
            await RefreshCapturePreservingAnnotations();
            return;
        }

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
        FontFamily = (FontFamily)Application.Current.Resources["UiFont"];
        SetResourceReference(BackgroundProperty, "AppSurface");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var label = new TextBlock { Text = "Enter text:" };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
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
