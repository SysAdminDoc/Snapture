using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                          "Snapture grabs the foreground window after every left-click and keeps the input track with it.";
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
            "Click through the workflow. Each left-click captures the foreground window after a short settle delay, " +
            "while key chords and cursor clicks are tracked for the step.");
        StartStopButton.Content = "Stop recording";
        StatusText.Text = _session.KeyboardTrackingAvailable
            ? "Recording — key and click tracks active."
            : "Recording — click track active; keyboard track unavailable.";
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
        var inputTrack = StepCaptureInputFormatter.FormatTrack(frame.Keystrokes, frame.Clicks);
        if (inputTrack is not null)
        {
            var trackText = new TextBlock
            {
                FontSize = 12,
                Text = inputTrack,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            };
            trackText.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
            stack.Children.Add(trackText);
        }
        var captionBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxHeight = 200,
            Margin = new Thickness(0, 8, 0, 0),
            ToolTip = "Caption rendered with this step in every exported format."
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
            return SafeImageInput.LoadBitmapImage(path, decodePixelWidth: 360);
        }
        catch { return null; }
    }

    private async void OnExportClicked(object sender, RoutedEventArgs e)
    {
        if (_session is null || _session.Frames.Count == 0)
        {
            StatusText.Text = "Nothing to export — record some steps first.";
            return;
        }
        var selectedPath = await StoragePickerService.PickSaveFileAsync(
            this,
            "Markdown bundle (*.md)|*.md",
            "steps.md",
            ".md",
            new[]
            {
                new StoragePickerService.FileTypeChoice(
                    "Markdown bundle",
                    new[] { ".md" })
            },
            _session.SessionFolder,
            "Choose output location for the steps.md file");
        if (selectedPath is null) return;

        var outDir = Path.GetDirectoryName(selectedPath)!;
        var entries = _session.Frames
            .Select(f => new StepCaptureExporter.StepEntry(
                f.Number, f.FilePath,
                _captionBoxes.TryGetValue(f.Number, out var box) ? box.Text : "",
                f.Keystrokes,
                f.Clicks))
            .ToList();

        try
        {
            var outputPath = StepCaptureExporter.ExportMarkdown(outDir, TitleBox.Text, entries);
            StatusText.Text = $"Exported: {outputPath}";
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{outputPath}\"") { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private async void OnExportDocxClicked(object sender, RoutedEventArgs e) =>
        await ExportOfficeDocument(pptx: false);

    private async void OnExportPptxClicked(object sender, RoutedEventArgs e) =>
        await ExportOfficeDocument(pptx: true);

    private async Task ExportOfficeDocument(bool pptx)
    {
        if (_session is null || _session.Frames.Count == 0)
        {
            StatusText.Text = "Nothing to export — record some steps first.";
            return;
        }

        var extension = pptx ? ".pptx" : ".docx";
        var label = pptx ? "PowerPoint" : "Word";
        var selectedPath = await StoragePickerService.PickSaveFileAsync(
            this,
            pptx
                ? "PowerPoint presentation (*.pptx)|*.pptx"
                : "Word document (*.docx)|*.docx",
            $"{SafeFileName(TitleBox.Text)}{extension}",
            extension,
            new[]
            {
                new StoragePickerService.FileTypeChoice(
                    pptx ? "PowerPoint presentation" : "Word document",
                    new[] { extension })
            },
            _session.SessionFolder,
            $"Choose output location for the {label} document");
        if (selectedPath is null) return;

        var entries = BuildExportEntries();
        try
        {
            var outputPath = pptx
                ? StepCaptureOfficeExporter.ExportPptx(selectedPath, TitleBox.Text, entries)
                : StepCaptureOfficeExporter.ExportDocx(selectedPath, TitleBox.Text, entries);
            StatusText.Text = $"Exported {label}: {outputPath}";
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{outputPath}\"")
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
                _captionBoxes.TryGetValue(f.Number, out var box) ? box.Text : string.Empty,
                f.Keystrokes,
                f.Clicks))
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
