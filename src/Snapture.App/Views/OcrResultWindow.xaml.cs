using System.Windows;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class OcrResultWindow : Window
{
    private readonly string _recognizedText;

    public OcrResultWindow(string text, string? language = null)
    {
        InitializeComponent();
        _recognizedText = text ?? string.Empty;
        ResultBox.Text = _recognizedText;
        if (!string.IsNullOrEmpty(language))
            LangText.Text = $"Language: {language}";

        int chars = _recognizedText.Length;
        int words = _recognizedText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        StatusText.Text = $"{words} words · {chars} characters";

        if (chars == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
            StatusText.Text = "No text recognized.";
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_recognizedText); StatusText.Text = "Copied to clipboard."; }
        catch { StatusText.Text = "Clipboard busy — try again."; }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
