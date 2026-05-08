using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _tray;
    private readonly CaptureOrchestrator _orchestrator;

    public TrayIconHost(CaptureOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
        _tray = new TaskbarIcon
        {
            ToolTipText = "Snapture",
            IconSource = LoadTrayIconSource(),
            ContextMenu = BuildMenu()
        };
        _tray.TrayLeftMouseDown += async (_, _) => await SafeRun(_orchestrator.CaptureRegionAsync);
    }

    private static ImageSource LoadTrayIconSource()
    {
        // Fall back to a generated icon if the embedded ICO isn't available.
        try
        {
            var uri = new Uri("pack://application:,,,/icon.ico", UriKind.Absolute);
            return new BitmapImage(uri);
        }
        catch
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRoundedRectangle(Brushes.MediumPurple, null, new Rect(0, 0, 32, 32), 6, 6);
                var ft = new FormattedText("S", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 22, Brushes.Black, 1.0);
                dc.DrawText(ft, new Point(8, 2));
            }
            var rtb = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
    }

    private ContextMenu BuildMenu()
    {
        var m = new ContextMenu();

        var region = new MenuItem { Header = "Capture _Region…  (PrintScreen)" };
        region.Click += async (_, _) => await SafeRun(_orchestrator.CaptureRegionAsync);
        m.Items.Add(region);

        var window = new MenuItem { Header = "Capture _Window  (Alt+PrintScreen)" };
        window.Click += async (_, _) => await SafeRun(_orchestrator.CaptureForegroundWindowAsync);
        m.Items.Add(window);

        var fs = new MenuItem { Header = "Capture _Fullscreen  (Ctrl+PrintScreen)" };
        fs.Click += async (_, _) => await SafeRun(_orchestrator.CaptureFullscreenAsync);
        m.Items.Add(fs);

        var monitorParent = new MenuItem { Header = "Capture _Monitor" };
        try
        {
            var monitors = MonitorEnumerator.Enumerate();
            foreach (var mon in monitors)
            {
                var mi = new MenuItem { Header = $"{mon.DeviceName} — {mon.Bounds.Width}×{mon.Bounds.Height}{(mon.IsPrimary ? "  (primary)" : "")}" };
                var captured = mon;
                mi.Click += async (_, _) => await SafeRun(() => _orchestrator.CaptureMonitorAsync(captured));
                monitorParent.Items.Add(mi);
            }
        }
        catch { /* enumeration failure is non-fatal */ }
        m.Items.Add(monitorParent);

        m.Items.Add(new Separator());

        var openFolder = new MenuItem { Header = "Open _Output Folder" };
        openFolder.Click += (_, _) =>
        {
            try
            {
                var dir = App.Host?.Settings.Current.OutputFolder ?? "";
                if (System.IO.Directory.Exists(dir))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch { }
        };
        m.Items.Add(openFolder);

        var about = new MenuItem { Header = "_About Snapture" };
        about.Click += (_, _) =>
        {
            MessageBox.Show(
                "Snapture v0.1.0\n\nAll-in-one screenshot utility for Windows.\nMIT License — github.com/SysAdminDoc/Snapture",
                "About Snapture", MessageBoxButton.OK, MessageBoxImage.Information);
        };
        m.Items.Add(about);

        m.Items.Add(new Separator());
        var quit = new MenuItem { Header = "_Quit" };
        quit.Click += (_, _) => Application.Current.Shutdown();
        m.Items.Add(quit);

        return m;
    }

    public void ShowToast(string title, string message)
    {
        try { _tray.ShowBalloonTip(title, message, BalloonIcon.Info); }
        catch { /* non-fatal */ }
    }

    private static async Task SafeRun(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex)
        {
            MessageBox.Show($"Capture failed:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Dispose() => _tray.Dispose();
}
