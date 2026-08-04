using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class QrCodeWindow : Window
{
    private readonly string _url;

    public QrCodeWindow(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("A LAN-share URL is required.", nameof(url));

        InitializeComponent();
        _url = url;
        UrlText.Text = url;
        QrImage.Source = LoadPng(QrCodeService.EncodePng(url));
    }

    private static BitmapImage LoadPng(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnCopyUrlClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_url);
            UrlText.Text = $"{_url}\nCopied to clipboard.";
        }
        catch
        {
            UrlText.Text = $"{_url}\nClipboard busy — try again.";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
