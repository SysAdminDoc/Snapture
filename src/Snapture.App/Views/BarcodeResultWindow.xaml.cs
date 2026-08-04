using System.Windows;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class BarcodeResultWindow : Window
{
    private readonly IReadOnlyList<BarcodeDetection> _detections;

    public BarcodeResultWindow(IReadOnlyList<BarcodeDetection> detections)
    {
        InitializeComponent();
        _detections = detections ?? Array.Empty<BarcodeDetection>();
        ResultsList.ItemsSource = _detections.Select(d => new BarcodeRow(
            d.Format,
            d.Text,
            d.BoundingBox.IsEmpty
                ? "Position unavailable"
                : $"Region: {d.BoundingBox.Left:0}, {d.BoundingBox.Top:0} · {d.BoundingBox.Width:0} × {d.BoundingBox.Height:0}"));
        CountText.Text = $"{_detections.Count} detection{(_detections.Count == 1 ? "" : "s")}";
        StatusText.Text = _detections.Count == 0
            ? "Nothing to copy."
            : "Detected locally — no network request was made.";
        if (_detections.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(
                Environment.NewLine,
                _detections.Select(d => $"{d.Format}: {d.Text}")));
            StatusText.Text = "Copied results to clipboard.";
        }
        catch
        {
            StatusText.Text = "Clipboard busy — try again.";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private sealed record BarcodeRow(string Format, string Text, string Bounds);
}
