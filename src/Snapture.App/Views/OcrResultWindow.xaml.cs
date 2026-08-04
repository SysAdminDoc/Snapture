using System.Windows;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class OcrResultWindow : Window
{
    private readonly string _recognizedText;

    public OcrResultWindow(string text, string? language = null, OcrEngineKind? engine = null)
    {
        InitializeComponent();
        _recognizedText = text ?? string.Empty;
        ResultBox.Text = _recognizedText;
        var engineLabel = engine switch
        {
            OcrEngineKind.WindowsAiTextRecognizer => "Windows AI",
            OcrEngineKind.WindowsMediaOcr => "Windows OCR",
            _ => null
        };
        LangText.Text = (language, engineLabel) switch
        {
            ({ Length: > 0 }, { Length: > 0 }) => $"Language: {language} · Engine: {engineLabel}",
            ({ Length: > 0 }, _) => $"Language: {language}",
            (_, { Length: > 0 }) => $"Engine: {engineLabel}",
            _ => ""
        };

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
