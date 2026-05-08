using System.Windows;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class OcrResultWindow : Window
{
    public OcrResultWindow(string text, string? language = null)
    {
        InitializeComponent();
        ResultBox.Text = text ?? string.Empty;
        if (!string.IsNullOrEmpty(language))
            LangText.Text = $"Language: {language}";

        int chars = ResultBox.Text.Length;
        int words = ResultBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        StatusText.Text = $"{words} words · {chars} characters";

        if (chars == 0)
        {
            ResultBox.Text = "(No text detected. If the language pack you need isn't installed, " +
                             "open Settings → Time & Language → Language to add it.)";
            StatusText.Text = "Empty result — see settings deeplink in the message.";
        }
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ResultBox.Text); StatusText.Text = "Copied to clipboard."; }
        catch { StatusText.Text = "Clipboard busy — try again."; }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
