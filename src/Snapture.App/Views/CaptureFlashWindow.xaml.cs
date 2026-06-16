using System.Windows;
using System.Windows.Media.Animation;
using Snapture.Capture;

namespace Snapture.App.Views;

/// <summary>
/// Brief white flash over the capture area as visual feedback.
/// Fills the virtual screen, fades from 40% to 0% over 200ms, then closes.
/// </summary>
public partial class CaptureFlashWindow : Window
{
    public CaptureFlashWindow()
    {
        InitializeComponent();
        var virt = MonitorEnumerator.GetVirtualScreen();
        Left = virt.X;
        Top = virt.Y;
        Width = virt.Width;
        Height = virt.Height;
    }

    public void Flash()
    {
        Show();
        var anim = new DoubleAnimation(0.4, 0.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}
