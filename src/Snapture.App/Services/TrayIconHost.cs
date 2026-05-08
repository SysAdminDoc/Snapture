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

        var lastRegion = new MenuItem { Header = "Recapture _Last Region  (Shift+PrintScreen)" };
        lastRegion.Click += async (_, _) => await SafeRun(_orchestrator.CaptureLastRegionAsync);
        m.Items.Add(lastRegion);

        var pickWin = new MenuItem { Header = "_Pick Window…" };
        pickWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureWindowPickerAsync);
        m.Items.Add(pickWin);

        var window = new MenuItem { Header = "Capture Foreground _Window  (Alt+PrintScreen)" };
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

        // Self-timer submenu
        var timer = new MenuItem { Header = "Capture with _Delay" };
        foreach (var seconds in new[] { 1, 3, 5, 10 })
        {
            var item = new MenuItem { Header = $"After {seconds}s — Region" };
            int s = seconds;
            item.Click += async (_, _) => await SafeRun(() => _orchestrator.CaptureWithDelayAsync(_orchestrator.CaptureRegionAsync, s));
            timer.Items.Add(item);
        }
        m.Items.Add(timer);

        m.Items.Add(new Separator());

        var settings = new MenuItem { Header = "_Settings…" };
        settings.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var dlg = new Views.SettingsWindow(App.Host.Settings) { Owner = null };
                if (dlg.ShowDialog() == true)
                {
                    // Re-register hotkeys with the new bindings
                    App.Host.RewireHotkeys();
                    ShowToast("Settings saved", "Snapture is using your updated configuration.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open settings:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        m.Items.Add(settings);

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

        // Engine selector
        var engineMenu = new MenuItem { Header = "Capture _Engine" };
        foreach (var (label, key) in new[]
        {
            ("Auto (recommended)", CaptureEngineFactory.Auto),
            ("WinRT (Windows.Graphics.Capture)", CaptureEngineFactory.WinRt),
            ("GDI (legacy)", CaptureEngineFactory.Gdi)
        })
        {
            var emi = new MenuItem { Header = label, IsCheckable = true };
            string captured = key;
            emi.IsChecked = string.Equals(App.Host?.Settings.Current.CaptureEngine, key, StringComparison.OrdinalIgnoreCase);
            emi.Click += (_, _) =>
            {
                App.Host?.SwitchEngine(captured);
                ShowToast("Capture engine", $"Switched to {App.Host?.EngineName.ToUpperInvariant()}.");
                // Refresh checkmarks
                foreach (var it in engineMenu.Items.OfType<MenuItem>())
                {
                    var k = (string)it.Tag!;
                    it.IsChecked = string.Equals(App.Host?.Settings.Current.CaptureEngine, k, StringComparison.OrdinalIgnoreCase);
                }
            };
            emi.Tag = key;
            engineMenu.Items.Add(emi);
        }
        m.Items.Add(engineMenu);

        // Reclaim PrintScreen if hijacked by Win11 24H2
        if (PrintScreenHijackDetector.IsHijacked())
        {
            var reclaim = new MenuItem { Header = "Reclaim _PrintScreen (Windows is hijacking it)" };
            reclaim.Click += (_, _) =>
            {
                if (PrintScreenHijackDetector.Reclaim())
                    ShowToast("PrintScreen reclaimed", "Sign out and back in for the change to take full effect.");
                else
                    ShowToast("Could not reclaim", "Try opening Settings → Accessibility → Keyboard.");
            };
            m.Items.Add(reclaim);
        }

        var about = new MenuItem { Header = "_About Snapture" };
        about.Click += (_, _) =>
        {
            MessageBox.Show(
                $"Snapture v0.2.0\n\nAll-in-one screenshot utility for Windows.\nEngine: {App.Host?.EngineName.ToUpperInvariant()}\nMIT License — github.com/SysAdminDoc/Snapture",
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
        catch (CaptureExcludedException ex)
        {
            MessageBox.Show(ex.Message, "Snapture", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Capture failed:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Dispose() => _tray.Dispose();
}
