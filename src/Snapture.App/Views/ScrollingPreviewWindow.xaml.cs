using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Snapture.App.Views;

/// <summary>Non-modal viewport preview shown while a scrolling capture is stitched.</summary>
public partial class ScrollingPreviewWindow : Window
{
    public ScrollingPreviewWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 24;
        Top = workArea.Top + 24;
    }

    public void UpdateStatus(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateStatus(status));
            return;
        }

        if (!IsVisible) return;
        StatusText.Text = status;
    }

    public void UpdateFrame(Bitmap frame, int frameNumber, double scrollPercent)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => UpdateFrame(frame, frameNumber, scrollPercent));
            return;
        }

        if (!IsVisible) return;
        PreviewImage.Source = ToBitmapSource(frame);
        EmptyText.Visibility = Visibility.Collapsed;
        FrameText.Text = $"Frame {frameNumber} · {Math.Clamp(scrollPercent, 0, 100):0}%";
        ProgressBar.Value = Math.Clamp(scrollPercent, 0, 100);
    }

    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();
        source.Freeze();
        return source;
    }
}
