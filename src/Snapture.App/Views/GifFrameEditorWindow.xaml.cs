using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class GifFrameEditorWindow : Window
{
    private readonly GifFrameEditor _editor;
    private readonly ObservableCollection<FramePreview> _previews = new();
    private bool _refreshing;
    private int _selectedIndex = -1;

    internal GifFrameEditorWindow(GifFrameEditor editor)
    {
        InitializeComponent();
        _editor = editor;
        FrameList.ItemsSource = _previews;
        RefreshFrames(0);
    }

    private void RefreshFrames(int requestedIndex)
    {
        _refreshing = true;
        _previews.Clear();
        for (int index = 0; index < _editor.Count; index++)
        {
            var info = _editor.GetInfo(index);
            using var bitmap = _editor.CloneFrame(index);
            _previews.Add(new FramePreview(
                $"Frame {index + 1}",
                ToBitmapSource(bitmap),
                $"{info.DelayMs} ms",
                info.IsDithered ? "Dithered" : string.Empty));
        }

        SummaryText.Text = $"{_editor.Count} frame{(_editor.Count == 1 ? string.Empty : "s")} · Changes are kept until you save";
        _selectedIndex = _editor.Count == 0 ? -1 : Math.Clamp(requestedIndex, 0, _editor.Count - 1);
        FrameList.SelectedIndex = _selectedIndex;
        _refreshing = false;
        UpdateSelection();
    }

    private void OnFrameSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing)
            UpdateSelection();
    }

    private void UpdateSelection()
    {
        _selectedIndex = FrameList.SelectedIndex;
        bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _editor.Count;
        DeleteButton.IsEnabled = hasSelection && _editor.Count > 1;
        DuplicateButton.IsEnabled = hasSelection;
        DitherButton.IsEnabled = hasSelection;
        DelayTextBox.IsEnabled = hasSelection;
        LosslessButton.IsEnabled = _editor.CanSaveLosslessly;

        if (!hasSelection)
        {
            PreviewTitle.Text = "Select a frame";
            PreviewDetails.Text = string.Empty;
            PreviewImage.Source = null;
            DelayTextBox.Text = string.Empty;
            return;
        }

        var info = _editor.GetInfo(_selectedIndex);
        using var bitmap = _editor.CloneFrame(_selectedIndex);
        PreviewTitle.Text = $"Frame {_selectedIndex + 1} of {_editor.Count}";
        PreviewDetails.Text = $"{bitmap.Width} × {bitmap.Height} px · {(info.IsDithered ? "dithered" : "original color")}";
        PreviewImage.Source = ToBitmapSource(bitmap);
        DelayTextBox.Text = info.DelayMs.ToString(CultureInfo.InvariantCulture);
    }

    private void OnApplyDelayClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndex(out int index))
            return;

        if (!int.TryParse(DelayTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int delayMs))
        {
            SetStatus("Enter a whole number of milliseconds.");
            return;
        }

        _editor.SetDelay(index, delayMs);
        RefreshFrames(index);
        SetStatus($"Frame {index + 1} delay set to {_editor.GetInfo(index).DelayMs} ms.");
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndex(out int index))
            return;

        try
        {
            _editor.Delete(index);
            RefreshFrames(Math.Min(index, _editor.Count - 1));
            SetStatus("Frame deleted.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void OnDuplicateClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndex(out int index))
            return;

        _editor.Duplicate(index);
        RefreshFrames(index + 1);
        SetStatus($"Frame {index + 1} duplicated.");
    }

    private void OnDitherClicked(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedIndex(out int index))
            return;

        DitherButton.IsEnabled = false;
        try
        {
            _editor.ApplyDither(index);
            RefreshFrames(index);
            SetStatus($"Dither applied to frame {index + 1}.");
        }
        finally
        {
            DitherButton.IsEnabled = true;
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var selectedPath = await StoragePickerService.PickSaveFileAsync(
            this,
            "Animated GIF (*.gif)|*.gif|Animated PNG (*.apng)|*.apng|Animated AVIF (*.avif)|*.avif",
            $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.gif",
            ".gif",
            new[]
            {
                new StoragePickerService.FileTypeChoice("Animated GIF", new[] { ".gif" }),
                new StoragePickerService.FileTypeChoice("Animated PNG", new[] { ".apng" }),
                new StoragePickerService.FileTypeChoice("Animated AVIF", new[] { ".avif" })
            },
            title: "Save animated image");
        if (selectedPath is null)
            return;

        try
        {
            var format = Path.GetExtension(selectedPath).ToLowerInvariant() switch
            {
                ".apng" => AnimatedImageFormat.Apng,
                ".avif" => AnimatedImageFormat.Avif,
                _ => AnimatedImageFormat.Gif
            };
            if (!GifEncoder.IsFormatSupported(format))
            {
                MessageBox.Show(
                    $"{GifEncoder.GetDisplayName(format)} output is unavailable in this installation.",
                    "Snapture",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string outputPath = Path.ChangeExtension(
                selectedPath,
                GifEncoder.GetExtension(format).TrimStart('.'));
            _editor.SaveAs(outputPath, format);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "explorer.exe", $"/select,\"{outputPath}\"") { UseShellExecute = true });
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not encode animated image:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnSaveLosslessClicked(object sender, RoutedEventArgs e)
    {
        var path = await StoragePickerService.PickSaveFileAsync(
            this,
            "Lossless GIF clip (*.gif)|*.gif",
            $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_clip.gif",
            ".gif",
            new[]
            {
                new StoragePickerService.FileTypeChoice(
                    "Lossless GIF clip",
                    new[] { ".gif" })
            },
            title: "Save lossless GIF clip");
        if (path is null)
            return;

        try
        {
            _editor.SaveLossless(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save lossless GIF clip:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool TryGetSelectedIndex(out int index)
    {
        index = FrameList.SelectedIndex;
        return index >= 0 && index < _editor.Count;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private sealed class FramePreview
    {
        public FramePreview(string caption, BitmapSource preview, string delayLabel, string ditherLabel)
        {
            Caption = caption;
            Preview = preview;
            DelayLabel = delayLabel;
            DitherLabel = ditherLabel;
        }

        public string Caption { get; }
        public BitmapSource Preview { get; }
        public string DelayLabel { get; }
        public string DitherLabel { get; }
    }
}
