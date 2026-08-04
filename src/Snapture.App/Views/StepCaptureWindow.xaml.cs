using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class StepCaptureWindow : Window
{
    private StepCaptureSession? _session;
    private readonly Dictionary<int, TextBox> _captionBoxes = new();

    public StepCaptureWindow()
    {
        InitializeComponent();
        FooterText.Text = "Click Start, then click your way through the workflow you want to document. " +
                          "Snapture grabs the foreground window after every left-click.";
    }

    private void OnStartStopClicked(object sender, RoutedEventArgs e)
    {
        if (_session is { IsRunning: true })
        {
            _session.Stop();
            StartStopButton.Content = "Start recording";
            StatusText.Text = $"Stopped — {_session.Frames.Count} steps captured.";
            FooterText.Text = $"Session folder: {_session.SessionFolder}";
            UpdateEmptyState(_session.Frames.Count == 0, "No steps captured",
                "Start recording again and click through the workflow you want to document.");
            return;
        }

        var engine = App.Host?.Engine;
        if (engine is null)
        {
            StatusText.Text = "Capture engine unavailable.";
            return;
        }

        _session?.Dispose();
        _session = new StepCaptureSession(engine);
        _session.FrameAdded += OnFrameAdded;
        _session.Start();
        if (!_session.IsRunning)
        {
            StatusText.Text = "Could not install the click hook.";
            return;
        }

        StepsList.Children.Clear();
        _captionBoxes.Clear();
        UpdateEmptyState(true, "Recording is active",
            "Click through the workflow. Each left-click captures the foreground window after a short settle delay.");
        StartStopButton.Content = "Stop recording";
        StatusText.Text = "Recording — click anywhere to capture a step.";
        FooterText.Text = $"Session folder: {_session.SessionFolder}";
    }

    private void OnFrameAdded(StepCaptureFrame frame)
    {
        // Marshal back onto the UI thread.
        Dispatcher.BeginInvoke((Action)(() => AppendStep(frame)));
    }

    private void AppendStep(StepCaptureFrame frame)
    {
        EmptyState.Visibility = Visibility.Collapsed;
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };
        card.SetResourceReference(Border.BackgroundProperty, "AppSurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "AppBorder");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var imageFrame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 0, 14, 0),
            Padding = new Thickness(4)
        };
        imageFrame.SetResourceReference(Border.BackgroundProperty, "AppCanvas");
        imageFrame.SetResourceReference(Border.BorderBrushProperty, "AppBorder");
        var img = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 220,
            Source = LoadThumbnail(frame.FilePath)
        };
        imageFrame.Child = img;
        Grid.SetColumn(imageFrame, 0);
        grid.Children.Add(imageFrame);

        var stack = new StackPanel();
        var stepTitle = new TextBlock
        {
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Text = $"Step {frame.Number}"
        };
        stepTitle.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        stack.Children.Add(stepTitle);
        if (!string.IsNullOrWhiteSpace(frame.WindowTitle) || !string.IsNullOrWhiteSpace(frame.ProcessName))
        {
            var sourceText = new TextBlock
            {
                FontSize = 12,
                Text = $"{frame.ProcessName ?? "?"} · {frame.WindowTitle ?? ""}",
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 2, 0, 0)
            };
            sourceText.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
            stack.Children.Add(sourceText);
        }
        var captionBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 200,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Caption rendered above the screenshot in the exported Markdown."
        };
        captionBox.SetResourceReference(Control.BackgroundProperty, "AppCanvas");
        captionBox.SetResourceReference(Control.ForegroundProperty, "AppForeground");
        captionBox.SetResourceReference(Control.BorderBrushProperty, "AppBorder");
        _captionBoxes[frame.Number] = captionBox;
        stack.Children.Add(captionBox);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        card.Child = grid;
        StepsList.Children.Add(card);
        StatusText.Text = $"{(_session?.Frames.Count ?? 0)} steps recorded · click to add more, or Stop";
    }

    private void UpdateEmptyState(bool visible, string title, string body)
    {
        EmptyState.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitleText.Text = title;
        EmptyBodyText.Text = body;
    }

    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 360;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.Frames.Count == 0)
        {
            StatusText.Text = "Nothing to export — record some steps first.";
            return;
        }
        var dlg = new SaveFileDialog
        {
            Filter = "Markdown bundle (*.md)|*.md",
            FileName = "steps.md",
            InitialDirectory = _session.SessionFolder,
            Title = "Choose output location for the steps.md file"
        };
        if (dlg.ShowDialog(this) != true) return;

        var outDir = Path.GetDirectoryName(dlg.FileName)!;
        var entries = _session.Frames
            .Select(f => new StepCaptureExporter.StepEntry(
                f.Number, f.FilePath,
                _captionBoxes.TryGetValue(f.Number, out var box) ? box.Text : ""))
            .ToList();

        try
        {
            var path = StepCaptureExporter.ExportMarkdown(outDir, TitleBox.Text, entries);
            StatusText.Text = $"Exported: {path}";
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private void OnExportDocxClicked(object sender, RoutedEventArgs e) =>
        ExportOfficeDocument(pptx: false);

    private void OnExportPptxClicked(object sender, RoutedEventArgs e) =>
        ExportOfficeDocument(pptx: true);

    private void ExportOfficeDocument(bool pptx)
    {
        if (_session is null || _session.Frames.Count == 0)
        {
            StatusText.Text = "Nothing to export — record some steps first.";
            return;
        }

        var extension = pptx ? ".pptx" : ".docx";
        var label = pptx ? "PowerPoint" : "Word";
        var dlg = new SaveFileDialog
        {
            Filter = pptx
                ? "PowerPoint presentation (*.pptx)|*.pptx"
                : "Word document (*.docx)|*.docx",
            DefaultExt = extension,
            AddExtension = true,
            FileName = $"{SafeFileName(TitleBox.Text)}{extension}",
            InitialDirectory = _session.SessionFolder,
            Title = $"Choose output location for the {label} document"
        };
        if (dlg.ShowDialog(this) != true) return;

        var entries = BuildExportEntries();
        try
        {
            var path = pptx
                ? StepCaptureOfficeExporter.ExportPptx(dlg.FileName, TitleBox.Text, entries)
                : StepCaptureOfficeExporter.ExportDocx(dlg.FileName, TitleBox.Text, entries);
            StatusText.Text = $"Exported {label}: {path}";
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
            catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"{label} export failed: {ex.Message}";
        }
    }

    private List<StepCaptureExporter.StepEntry> BuildExportEntries() =>
        _session!.Frames
            .Select(f => new StepCaptureExporter.StepEntry(
                f.Number,
                f.FilePath,
                _captionBoxes.TryGetValue(f.Number, out var box) ? box.Text : string.Empty))
            .ToList();

    private static string SafeFileName(string? value)
    {
        var candidate = string.IsNullOrWhiteSpace(value) ? "steps" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            candidate = candidate.Replace(invalid, '_');
        return candidate.Length > 80 ? candidate[..80].TrimEnd() : candidate;
    }

    protected override void OnClosed(EventArgs e)
    {
        _session?.Dispose();
        base.OnClosed(e);
    }
}
