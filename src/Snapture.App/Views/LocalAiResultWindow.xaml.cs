using System.Windows;

namespace Snapture.App.Views;

public partial class LocalAiResultWindow : Window
{
    public LocalAiResultWindow(string modelReference, string response)
    {
        InitializeComponent();
        ModelText.Text = modelReference;
        ResponseBox.Text = response;
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(ResponseBox.Text);
            StatusText.Text = "Response copied.";
        }
        catch
        {
            StatusText.Text = "Clipboard unavailable.";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
