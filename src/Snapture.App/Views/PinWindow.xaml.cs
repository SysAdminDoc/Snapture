using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace Snapture.App.Views;

public partial class PinWindow : Window
{
    private double _scale = 1.0;
    private double _opacity = 1.0;

    public PinWindow(BitmapSource image)
    {
        InitializeComponent();
        PinnedImage.Source = image;
        PinnedImage.Width = image.PixelWidth;
        PinnedImage.Height = image.PixelHeight;

        MouseLeftButtonDown += (_, _) => DragMove();
        MouseRightButtonDown += (_, _) => Close();
        PreviewMouseWheel += OnWheel;
        ContextMenu = BuildMenu();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            _opacity = Math.Clamp(_opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.2, 1.0);
            Opacity = _opacity;
        }
        else
        {
            _scale = Math.Clamp(_scale + (e.Delta > 0 ? 0.1 : -0.1), 0.25, 4.0);
            PinnedImage.Width = ((BitmapSource)PinnedImage.Source).PixelWidth * _scale;
            PinnedImage.Height = ((BitmapSource)PinnedImage.Source).PixelHeight * _scale;
        }
        e.Handled = true;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "Copy" };
        copy.Click += (_, _) => Clipboard.SetImage((BitmapSource)PinnedImage.Source);
        menu.Items.Add(copy);
        var resetScale = new MenuItem { Header = "Reset 100%" };
        resetScale.Click += (_, _) => { _scale = 1.0; var b = (BitmapSource)PinnedImage.Source; PinnedImage.Width = b.PixelWidth; PinnedImage.Height = b.PixelHeight; };
        menu.Items.Add(resetScale);
        var resetOpacity = new MenuItem { Header = "Reset Opacity" };
        resetOpacity.Click += (_, _) => { _opacity = 1.0; Opacity = 1.0; };
        menu.Items.Add(resetOpacity);
        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) => Close();
        menu.Items.Add(close);
        return menu;
    }
}
