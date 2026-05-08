using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class EditorWindow : Window
{
    private readonly BitmapSource _image;
    private readonly CaptureResult _capture;
    private string? _savedPath;

    public EditorWindow(BitmapSource image, string? savedPath, CaptureResult capture)
    {
        InitializeComponent();
        _image = image;
        _savedPath = savedPath;
        _capture = capture;

        CapturedImage.Source = _image;
        DimensionText.Text = $"{_image.PixelWidth} × {_image.PixelHeight}";
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        StatusText.Text = _savedPath is not null
            ? $"Saved at {_capture.CapturedAtUtc.ToLocalTime():HH:mm:ss}"
            : "Unsaved capture";
        PathText.Text = _savedPath ?? "(not yet on disk)";
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        if (_savedPath is not null)
        {
            StatusText.Text = "Already saved.";
            return;
        }
        OnSaveAsClicked(sender, e);
    }

    private void OnSaveAsClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|BMP image (*.bmp)|*.bmp",
            FileName = $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"
        };
        if (dlg.ShowDialog(this) != true) return;

        BitmapEncoder encoder = Path.GetExtension(dlg.FileName).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".bmp"            => new BmpBitmapEncoder(),
            _                 => new PngBitmapEncoder()
        };
        encoder.Frames.Add(BitmapFrame.Create(_image));
        using var fs = File.Create(dlg.FileName);
        encoder.Save(fs);
        _savedPath = dlg.FileName;
        UpdateStatus();
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetImage(_image);
            StatusText.Text = "Copied to clipboard.";
        }
        catch { StatusText.Text = "Clipboard busy — try again."; }
    }

    private void OnPinClicked(object sender, RoutedEventArgs e)
    {
        var pin = new PinWindow(_image);
        pin.Show();
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        if (_savedPath is null || !File.Exists(_savedPath))
        {
            StatusText.Text = "Save the capture first.";
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_savedPath}\"") { UseShellExecute = true });
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
