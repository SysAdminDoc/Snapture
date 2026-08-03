using System.Globalization;
using System.IO;
using System.Windows;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class VideoTrimWindow : Window
{
    private readonly string _inputPath;
    private TimeSpan _duration;

    public VideoTrimWindow(string inputPath)
    {
        InitializeComponent();
        _inputPath = Path.GetFullPath(inputPath);
        SourceText.Text = Path.GetFileName(_inputPath);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _duration = await VideoSegmentService.GetDurationAsync(_inputPath);
            EndTextBox.Text = FormatSeconds(_duration);
            SourceText.Text = $"{Path.GetFileName(_inputPath)} · {_duration.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)} seconds";
            StatusText.Text = "The source stays untouched; each action writes a new MP4.";
        }
        catch (Exception ex)
        {
            SetStatus($"Could not read the recording: {ex.Message}", error: true);
            SetBusy(true);
        }
    }

    private async void OnTrimClicked(object sender, RoutedEventArgs e)
    {
        if (!TryReadRange(out var start, out var end))
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MP4 video (*.mp4)|*.mp4",
            FileName = $"{Path.GetFileNameWithoutExtension(_inputPath)}_trim.mp4",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            SetBusy(true);
            SetStatus("Rendering the trimmed copy…");
            await VideoSegmentService.TrimAsync(_inputPath, dialog.FileName, start, end);
            SetStatus($"Saved {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Trim failed: {ex.Message}", error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnSplitClicked(object sender, RoutedEventArgs e)
    {
        var cutPoints = ParseMarkers();
        if (cutPoints is null)
            return;

        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for the split files",
            InitialDirectory = Path.GetDirectoryName(_inputPath)
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            SetBusy(true);
            SetStatus("Rendering split files…");
            var outputs = await VideoSegmentService.SplitAsync(_inputPath, dialog.FolderName, cutPoints);
            SetStatus($"Saved {outputs.Count} segment(s) to {dialog.FolderName}.");
        }
        catch (Exception ex)
        {
            SetStatus($"Split failed: {ex.Message}", error: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool TryReadRange(out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;
        if (!TryParseSeconds(StartTextBox.Text, out start)
            || !TryParseSeconds(EndTextBox.Text, out end))
        {
            SetStatus("Enter start and end as non-negative seconds.", error: true);
            return false;
        }

        try
        {
            _ = VideoSegmentService.NormalizeRange(_duration, start, end);
            return true;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            SetStatus(ex.Message, error: true);
            return false;
        }
    }

    private IReadOnlyList<TimeSpan>? ParseMarkers()
    {
        var values = MarkersTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (values.Length == 0)
        {
            SetStatus("Enter at least one split marker.", error: true);
            return null;
        }

        List<TimeSpan> markers = new(values.Length);
        foreach (string value in values)
        {
            if (!TryParseSeconds(value, out var marker) || marker <= TimeSpan.Zero || marker >= _duration)
            {
                SetStatus($"Split marker '{value}' must be inside the recording duration.", error: true);
                return null;
            }
            markers.Add(marker);
        }

        return markers;
    }

    private static bool TryParseSeconds(string text, out TimeSpan value)
    {
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds)
            && double.IsFinite(seconds) && seconds >= 0)
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        value = default;
        return false;
    }

    private static string FormatSeconds(TimeSpan value)
        => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    private void SetBusy(bool busy)
    {
        TrimButton.IsEnabled = !busy;
        SplitButton.IsEnabled = !busy;
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource(error ? "AppWarning" : "AppMutedForeground");
    }
}
