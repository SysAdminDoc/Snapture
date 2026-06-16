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
                var accent = Application.Current.TryFindResource("AppAccent") as Brush ?? Brushes.MediumPurple;
                var foreground = Application.Current.TryFindResource("AppAccentForeground") as Brush ?? Brushes.Black;
                dc.DrawRoundedRectangle(accent, null, new Rect(0, 0, 32, 32), 6, 6);
                var ft = new FormattedText("S", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 22, foreground, 1.0);
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

        var picker = new MenuItem { Header = "Capture _Mode Picker…" };
        picker.Click += async (_, _) => await SafeRun(_orchestrator.CaptureViaPickerAsync);
        m.Items.Add(picker);

        m.Items.Add(new Separator());

        var region = new MenuItem { Header = "Capture _Region…  (PrintScreen)" };
        region.Click += async (_, _) => await SafeRun(_orchestrator.CaptureRegionAsync);
        m.Items.Add(region);

        var lastRegion = new MenuItem { Header = "Recapture _Last Region  (Shift+PrintScreen)" };
        lastRegion.Click += async (_, _) => await SafeRun(_orchestrator.CaptureLastRegionAsync);
        m.Items.Add(lastRegion);

        var pickWin = new MenuItem { Header = "_Pick Window…" };
        pickWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureWindowPickerAsync);
        m.Items.Add(pickWin);

        var smartPick = new MenuItem { Header = "Smart _Element Capture…" };
        smartPick.Click += async (_, _) => await SafeRun(_orchestrator.CaptureSmartElementAsync);
        m.Items.Add(smartPick);

        var window = new MenuItem { Header = "Capture Foreground _Window  (Alt+PrintScreen)" };
        window.Click += async (_, _) => await SafeRun(_orchestrator.CaptureForegroundWindowAsync);
        m.Items.Add(window);

        var fs = new MenuItem { Header = "Capture _Fullscreen  (Ctrl+PrintScreen)" };
        fs.Click += async (_, _) => await SafeRun(_orchestrator.CaptureFullscreenAsync);
        m.Items.Add(fs);

        var monUnderCursor = new MenuItem { Header = "Capture Monitor Under _Cursor" };
        monUnderCursor.Click += async (_, _) => await SafeRun(_orchestrator.CaptureMonitorUnderCursorAsync);
        m.Items.Add(monUnderCursor);

        var scrollingWin = new MenuItem { Header = "Capture _Scrolling Window  (alpha)" };
        scrollingWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureScrollingForegroundAsync);
        m.Items.Add(scrollingWin);

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

        var themeMenu = new MenuItem { Header = "_Theme" };
        foreach (var (label, key) in new[]
        {
            ("System", ThemeManager.SystemMode),
            ("Light", ThemeManager.LightMode),
            ("Dark", ThemeManager.DarkMode)
        })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, Tag = key };
            item.IsChecked = ThemeManager.NormalizeMode(App.Host?.Settings.Current.ThemeMode) == key;
            item.Click += (_, _) =>
            {
                if (App.Host is null) return;
                App.Host.Settings.Current.ThemeMode = key;
                App.Host.Settings.Save();
                ThemeManager.Apply(key);
                foreach (var it in themeMenu.Items.OfType<MenuItem>())
                {
                    var mode = (string)it.Tag!;
                    it.IsChecked = ThemeManager.NormalizeMode(App.Host.Settings.Current.ThemeMode) == mode;
                }
            };
            themeMenu.Items.Add(item);
        }
        m.Items.Add(themeMenu);

        var tools = new MenuItem { Header = "_Tools" };
        var colorPicker = new MenuItem { Header = "Color picker" };
        colorPicker.Click += (_, _) => new Views.ColorPickerWindow().Show();
        tools.Items.Add(colorPicker);
        var pixelRuler = new MenuItem { Header = "Pixel ruler" };
        pixelRuler.Click += (_, _) => new Views.PixelRulerWindow().Show();
        tools.Items.Add(pixelRuler);
        var ocr = new MenuItem { Header = "OCR region…" };
        ocr.Click += async (_, _) => await SafeRun(_orchestrator.OcrRegionAsync);
        tools.Items.Add(ocr);
        var captureText = new MenuItem { Header = "Capture Text → clipboard" };
        captureText.Click += async (_, _) => await SafeRun(_orchestrator.CaptureTextAsync);
        tools.Items.Add(captureText);
        var recordGif = new MenuItem { Header = "Record GIF" };
        var recGifWindow = new MenuItem { Header = "…of foreground window" };
        recGifWindow.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var rec = new GifRecorder(App.Host.Engine);
                new Views.GifRecordingWindow(rec, Views.GifRecordingWindow.Mode.ForegroundWindow).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start GIF recorder: {ex.Message}", "Snapture"); }
        };
        var recGifFull = new MenuItem { Header = "…of all monitors" };
        recGifFull.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var rec = new GifRecorder(App.Host.Engine);
                new Views.GifRecordingWindow(rec, Views.GifRecordingWindow.Mode.VirtualScreen).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start GIF recorder: {ex.Message}", "Snapture"); }
        };
        recordGif.Items.Add(recGifWindow);
        recordGif.Items.Add(recGifFull);
        tools.Items.Add(recordGif);

        var recordVideo = new MenuItem { Header = "Record Video (MP4: AV1 / HEVC / H.264)" };
        var recVidWindow = new MenuItem { Header = "…of foreground window" };
        recVidWindow.Click += (_, _) =>
        {
            try
            {
                var hwnd = Native2.GetForegroundWindow();
                if (hwnd == 0) { MessageBox.Show("No foreground window.", "Snapture"); return; }
                var rec = new VideoRecorder();
                new Views.VideoRecordingWindow(rec, Views.VideoRecordingWindow.Mode.ForegroundWindow, hwnd).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start video recorder: {ex.Message}", "Snapture"); }
        };
        recordVideo.Items.Add(recVidWindow);
        var recVidMonitor = new MenuItem { Header = "…of primary monitor" };
        recVidMonitor.Click += (_, _) =>
        {
            try
            {
                var mon = MonitorEnumerator.Enumerate().FirstOrDefault(m => m.IsPrimary)
                    ?? MonitorEnumerator.Enumerate().First();
                var rec = new VideoRecorder();
                new Views.VideoRecordingWindow(rec, Views.VideoRecordingWindow.Mode.Monitor, mon.Handle).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start video recorder: {ex.Message}", "Snapture"); }
        };
        recordVideo.Items.Add(recVidMonitor);
        tools.Items.Add(recordVideo);

        var stepCapture = new MenuItem { Header = "Step Capture…" };
        stepCapture.Click += (_, _) =>
        {
            try { new Views.StepCaptureWindow().Show(); }
            catch (Exception ex) { MessageBox.Show($"Could not open Step Capture: {ex.Message}", "Snapture"); }
        };
        tools.Items.Add(stepCapture);

        var pluginsItem = new MenuItem { Header = "Plugins…" };
        pluginsItem.Click += (_, _) =>
        {
            try { new Views.PluginsWindow().Show(); }
            catch (Exception ex) { MessageBox.Show($"Plugins window failed: {ex.Message}", "Snapture"); }
        };
        tools.Items.Add(pluginsItem);

        var history = new MenuItem { Header = "Capture history…" };
        history.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var w = new Views.HistoryWindow(App.Host.History);
                w.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open history:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        tools.Items.Add(history);
        m.Items.Add(tools);

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
                {
                    PrintScreenHijackDetector.OpenSettingsPage();
                    ShowToast("PrintScreen reclaimed",
                        "The registry toggle is set. The Settings page is open so you can verify. Sign out and back in for the change to take full effect.");
                }
                else
                    ShowToast("Could not reclaim", "Try opening Settings → Accessibility → Keyboard.");
            };
            m.Items.Add(reclaim);
        }

        var diagDump = new MenuItem { Header = "Create _Diagnostic Dump…" };
        diagDump.Click += (_, _) =>
        {
            try
            {
                var path = DiagnosticDump.Create();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                ShowToast("Diagnostic dump created", $"Saved to Desktop: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create dump:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        m.Items.Add(diagDump);

        var about = new MenuItem { Header = "_About Snapture" };
        about.Click += (_, _) =>
        {
            var ver = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            MessageBox.Show(
                $"Snapture v{ver}\n\nAll-in-one screenshot utility for Windows.\nEngine: {App.Host?.EngineName.ToUpperInvariant()}\nTheme: {ThemeManager.DisplayName(App.Host?.Settings.Current.ThemeMode)} ({ThemeManager.EffectiveMode})\nMIT License — github.com/SysAdminDoc/Snapture",
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
