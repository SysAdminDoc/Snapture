using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class TrayIconHost : IDisposable
{
    private readonly TaskbarIcon _tray;
    private readonly CaptureOrchestrator _orchestrator;
    private readonly VideoRingBufferService _ringBuffer = new();

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

        var picker = new MenuItem { Header = "_Capture picker…" };
        picker.Click += async (_, _) => await SafeRun(_orchestrator.CaptureViaPickerAsync);
        m.Items.Add(picker);

        m.Items.Add(new Separator());

        var region = new MenuItem { Header = "_Region…", InputGestureText = "PrintScreen" };
        region.Click += async (_, _) => await SafeRun(_orchestrator.CaptureRegionAsync);
        m.Items.Add(region);

        var lastRegion = new MenuItem { Header = "_Last region", InputGestureText = "Shift+PrintScreen" };
        lastRegion.Click += async (_, _) => await SafeRun(_orchestrator.CaptureLastRegionAsync);
        m.Items.Add(lastRegion);

        var pickWin = new MenuItem { Header = "_Window picker…" };
        pickWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureWindowPickerAsync);
        m.Items.Add(pickWin);

        var smartPick = new MenuItem { Header = "Smart _element…" };
        smartPick.Click += async (_, _) => await SafeRun(_orchestrator.CaptureSmartElementAsync);
        m.Items.Add(smartPick);

        var window = new MenuItem { Header = "Foreground _window", InputGestureText = "Alt+PrintScreen" };
        window.Click += async (_, _) => await SafeRun(_orchestrator.CaptureForegroundWindowAsync);
        m.Items.Add(window);

        var fs = new MenuItem { Header = "_Fullscreen", InputGestureText = "Ctrl+PrintScreen" };
        fs.Click += async (_, _) => await SafeRun(_orchestrator.CaptureFullscreenAsync);
        m.Items.Add(fs);

        var monUnderCursor = new MenuItem { Header = "Monitor under _cursor" };
        monUnderCursor.Click += async (_, _) => await SafeRun(_orchestrator.CaptureMonitorUnderCursorAsync);
        m.Items.Add(monUnderCursor);

        var scrollingWin = new MenuItem { Header = "_Scrolling window (alpha)" };
        scrollingWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureScrollingForegroundAsync);
        m.Items.Add(scrollingWin);
        var horizontalScrollingWin = new MenuItem { Header = "Horizontal scrolling window" };
        horizontalScrollingWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureHorizontalScrollingForegroundAsync);
        m.Items.Add(horizontalScrollingWin);
        var omnidirectionalScrollingWin = new MenuItem { Header = "Omnidirectional scrolling window" };
        omnidirectionalScrollingWin.Click += async (_, _) => await SafeRun(_orchestrator.CaptureOmnidirectionalScrollingForegroundAsync);
        m.Items.Add(omnidirectionalScrollingWin);

        var monitorParent = new MenuItem { Header = "_Monitor" };
        try
        {
            var monitors = MonitorEnumerator.Enumerate();
            foreach (var mon in monitors)
            {
                var mi = new MenuItem { Header = $"{mon.DeviceName} - {mon.Bounds.Width}x{mon.Bounds.Height}{(mon.IsPrimary ? " (primary)" : "")}" };
                var captured = mon;
                mi.Click += async (_, _) => await SafeRun(() => _orchestrator.CaptureMonitorAsync(captured));
                monitorParent.Items.Add(mi);
            }
        }
        catch { /* enumeration failure is non-fatal */ }
        m.Items.Add(monitorParent);

        // Self-timer submenu
        var timer = new MenuItem { Header = "Region with _delay" };
        foreach (var seconds in new[] { 1, 3, 5, 10 })
        {
            var item = new MenuItem { Header = $"After {seconds}s" };
            int s = seconds;
            item.Click += async (_, _) => await SafeRun(() => _orchestrator.CaptureWithDelayAsync(_orchestrator.CaptureRegionAsync, s));
            timer.Items.Add(item);
        }
        m.Items.Add(timer);

        var presetMenu = new MenuItem { Header = "Capture _preset" };
        foreach (var preset in CapturePresetService.Presets)
        {
            var item = new MenuItem
            {
                Header = preset.Label,
                IsCheckable = true,
                Tag = preset.Key,
                ToolTip = preset.Description
            };
            item.IsChecked = string.Equals(
                App.Host?.Settings.Current.ActiveCapturePreset,
                preset.Key,
                StringComparison.OrdinalIgnoreCase);
            item.Click += (_, _) =>
            {
                if (App.Host is null || preset.Key == CapturePresetService.CustomKey)
                    return;

                if (!CapturePresetService.Apply(preset.Key, App.Host.Settings.Current))
                    return;

                App.Host.Settings.Save();
                App.Host.ApplyEngineSettings();
                if (App.Host.Settings.Current.LanShareEnabled && !App.Host.LanShare.IsRunning)
                    App.Host.TryStartLanShare();
                else if (!App.Host.Settings.Current.LanShareEnabled && App.Host.LanShare.IsRunning)
                    App.Host.LanShare.Stop();

                foreach (var menuItem in presetMenu.Items.OfType<MenuItem>())
                {
                    menuItem.IsChecked = string.Equals(
                        (string?)menuItem.Tag,
                        preset.Key,
                        StringComparison.OrdinalIgnoreCase);
                }
                ShowToast("Capture preset applied", preset.Label);
            };
            presetMenu.Items.Add(item);
        }
        m.Items.Add(presetMenu);

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
        var captureText = new MenuItem { Header = "Capture text to clipboard" };
        captureText.Click += async (_, _) => await SafeRun(_orchestrator.CaptureTextAsync);
        tools.Items.Add(captureText);

        var shellIntegration = new MenuItem { Header = "Image shell integration" };
        var installShell = new MenuItem { Header = "Install for this user" };
        installShell.Click += (_, _) =>
        {
            try
            {
                ShellIntegrationService.Install();
                ShowToast("Shell integration installed", "Right-click an image to open, resize, or convert it with Snapture.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not install shell integration:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        shellIntegration.Items.Add(installShell);

        var removeShell = new MenuItem { Header = "Remove for this user" };
        removeShell.Click += (_, _) =>
        {
            try
            {
                ShellIntegrationService.Uninstall();
                ShowToast("Shell integration removed", "Snapture image verbs are no longer in Explorer.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not remove shell integration:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        shellIntegration.Items.Add(removeShell);
        tools.Items.Add(shellIntegration);

        var urlIntegration = new MenuItem { Header = "URL scheme integration" };
        var installUrl = new MenuItem { Header = "Install snapture:// for this user" };
        installUrl.Click += (_, _) =>
        {
            try
            {
                UrlSchemeIntegrationService.Install();
                ShowToast("URL scheme installed", "snapture:// capture links are now handled by Snapture.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not install the URL scheme:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        urlIntegration.Items.Add(installUrl);

        var removeUrl = new MenuItem { Header = "Remove snapture:// for this user" };
        removeUrl.Click += (_, _) =>
        {
            try
            {
                UrlSchemeIntegrationService.Uninstall();
                ShowToast("URL scheme removed", "Snapture no longer handles snapture:// links.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not remove the URL scheme:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        urlIntegration.Items.Add(removeUrl);
        tools.Items.Add(urlIntegration);

        var ocrFromFile = new MenuItem { Header = "OCR from file…" };
        ocrFromFile.Click += async (_, _) =>
        {
            var path = await StoragePickerService.PickOpenFileAsync(
                owner: null,
                "Image (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
                new[] { ".png", ".jpg", ".jpeg", ".bmp" },
                title: "Choose an image for OCR");
            if (path is null) return;
            try
            {
                var bi = SafeImageInput.LoadBitmapImage(path);
                var result = await OcrService.RecognizeAsync(bi);
                string text = result?.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                    try { System.Windows.Clipboard.SetText(text); } catch { }
                new Views.OcrResultWindow(text, engine: result?.Engine).Show();
            }
            catch (Exception ex) { MessageBox.Show($"OCR failed:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        tools.Items.Add(ocrFromFile);
        var recordGif = new MenuItem { Header = "Record GIF" };
        var recGifWindow = new MenuItem { Header = "Foreground window" };
        recGifWindow.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var rec = new GifRecorder(App.Host.Engine);
                new Views.GifRecordingWindow(rec, Views.GifRecordingWindow.Mode.ForegroundWindow).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start GIF recorder:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        var recGifFull = new MenuItem { Header = "All monitors" };
        recGifFull.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var rec = new GifRecorder(App.Host.Engine);
                new Views.GifRecordingWindow(rec, Views.GifRecordingWindow.Mode.VirtualScreen).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start GIF recorder:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        recordGif.Items.Add(recGifWindow);
        recordGif.Items.Add(recGifFull);
        tools.Items.Add(recordGif);

        var editGif = new MenuItem { Header = "Edit GIF…" };
        editGif.Click += async (_, _) =>
        {
            var path = await StoragePickerService.PickOpenFileAsync(
                owner: null,
                "Animated GIF (*.gif)|*.gif",
                new[] { ".gif" },
                title: "Choose an animated GIF");
            if (path is null)
                return;

            try
            {
                using var editor = GifFrameEditor.LoadGif(path);
                new Views.GifFrameEditorWindow(editor).ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open GIF:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        tools.Items.Add(editGif);

        var recordVideo = new MenuItem { Header = "Record MP4 video" };
        var recVidWindow = new MenuItem { Header = "Foreground window" };
        recVidWindow.Click += (_, _) =>
        {
            try
            {
                var hwnd = Native2.GetForegroundWindow();
                if (hwnd == 0)
                {
                    MessageBox.Show("No foreground window is available to record.", "Snapture",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                var q = RecordingPresets.GetQuality(App.Host?.Settings.Current.RecordingQuality ?? RecordingPresets.DefaultQuality);
                var (ow, oh) = RecordingPresets.ResolveOutputSize(
                    App.Host?.Settings.Current.RecordingResolution ?? RecordingPresets.NativeResolution, 0, 0);
                var rec = new VideoRecorder(
                    autoTightenEnabled: App.Host?.Settings.Current.RecordingAutoTighten ?? false,
                    toneMapOperator: HdrToneMapOperators.Parse(App.Host?.Settings.Current.HdrToneMapOperator),
                    hdrColorCorrection: App.Host?.Settings.Current.HdrColorCorrection ?? true);
                new Views.VideoRecordingWindow(rec, Views.VideoRecordingWindow.Mode.ForegroundWindow, hwnd,
                    q.Fps, q.BitrateMbps, ow, oh).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start video recorder:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        recordVideo.Items.Add(recVidWindow);
        var recVidMonitor = new MenuItem { Header = "Primary monitor" };
        recVidMonitor.Click += (_, _) =>
        {
            try
            {
                var mon = MonitorEnumerator.Enumerate().FirstOrDefault(m => m.IsPrimary)
                    ?? MonitorEnumerator.Enumerate().First();
                var q = RecordingPresets.GetQuality(App.Host?.Settings.Current.RecordingQuality ?? RecordingPresets.DefaultQuality);
                var (ow, oh) = RecordingPresets.ResolveOutputSize(
                    App.Host?.Settings.Current.RecordingResolution ?? RecordingPresets.NativeResolution, 0, 0);
                var rec = new VideoRecorder(
                    autoTightenEnabled: App.Host?.Settings.Current.RecordingAutoTighten ?? false,
                    toneMapOperator: HdrToneMapOperators.Parse(App.Host?.Settings.Current.HdrToneMapOperator),
                    hdrColorCorrection: App.Host?.Settings.Current.HdrColorCorrection ?? true);
                new Views.VideoRecordingWindow(rec, Views.VideoRecordingWindow.Mode.Monitor, mon.Handle,
                    q.Fps, q.BitrateMbps, ow, oh).Show();
            }
            catch (Exception ex) { MessageBox.Show($"Could not start video recorder:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        recordVideo.Items.Add(recVidMonitor);

        recordVideo.Items.Add(new Separator());

        var qualityMenu = new MenuItem { Header = "Quality" };
        foreach (var preset in RecordingPresets.Qualities)
        {
            var qi = new MenuItem { Header = preset.Label, IsCheckable = true, Tag = preset.Key };
            qi.IsChecked = preset.Key == (App.Host?.Settings.Current.RecordingQuality ?? RecordingPresets.DefaultQuality);
            qi.Click += (_, _) =>
            {
                if (App.Host is null) return;
                App.Host.Settings.Current.RecordingQuality = preset.Key;
                App.Host.Settings.Save();
                foreach (var it in qualityMenu.Items.OfType<MenuItem>())
                    it.IsChecked = (string)it.Tag! == preset.Key;
            };
            qualityMenu.Items.Add(qi);
        }
        recordVideo.Items.Add(qualityMenu);

        var resolutionMenu = new MenuItem { Header = "Output resolution" };
        foreach (var preset in RecordingPresets.Resolutions)
        {
            var ri = new MenuItem { Header = preset.Label, IsCheckable = true, Tag = preset.Key };
            ri.IsChecked = preset.Key == (App.Host?.Settings.Current.RecordingResolution ?? RecordingPresets.NativeResolution);
            ri.Click += (_, _) =>
            {
                if (App.Host is null) return;
                App.Host.Settings.Current.RecordingResolution = preset.Key;
                App.Host.Settings.Save();
                foreach (var it in resolutionMenu.Items.OfType<MenuItem>())
                    it.IsChecked = (string)it.Tag! == preset.Key;
            };
            resolutionMenu.Items.Add(ri);
        }
        recordVideo.Items.Add(resolutionMenu);

        var autoTighten = new MenuItem
        {
            Header = "Auto-tighten app chrome",
            IsCheckable = true,
            IsChecked = App.Host?.Settings.Current.RecordingAutoTighten ?? false,
            ToolTip = "Use UI Automation to crop edge-mounted tabs, docks, and taskbars when the crop is safe."
        };
        autoTighten.Click += (_, _) =>
        {
            if (App.Host is null) return;
            App.Host.Settings.Current.RecordingAutoTighten = autoTighten.IsChecked;
            App.Host.Settings.Save();
        };
        recordVideo.Items.Add(autoTighten);

        var editVideo = new MenuItem { Header = "Trim or split a video…" };
        editVideo.Click += async (_, _) =>
        {
            var path = await StoragePickerService.PickOpenFileAsync(
                owner: null,
                "MP4 video (*.mp4)|*.mp4|All files (*.*)|*.*",
                new[] { ".mp4" },
                title: "Choose a recording to trim or split");
            if (path is not null)
                new Views.VideoTrimWindow(path).Show();
        };
        recordVideo.Items.Add(editVideo);

        recordVideo.Items.Add(new Separator());
        var ringMenu = new MenuItem { Header = "Ring buffer" };
        var ringStartWindow = new MenuItem { Header = "Start for foreground window" };
        ringStartWindow.Click += (_, _) =>
        {
            try
            {
                var hwnd = Native2.GetForegroundWindow();
                if (hwnd == 0)
                {
                    ShowToast("Ring buffer unavailable", "No foreground window is available.");
                    return;
                }

                var q = RecordingPresets.GetQuality(App.Host?.Settings.Current.RecordingQuality ?? RecordingPresets.DefaultQuality);
                var (ow, oh) = RecordingPresets.ResolveOutputSize(
                    App.Host?.Settings.Current.RecordingResolution ?? RecordingPresets.NativeResolution, 0, 0);
                _ringBuffer.StartWindow(
                    hwnd, q.Fps, q.BitrateMbps, ow, oh,
                    App.Host?.Settings.Current.RecordingAutoTighten ?? false,
                    HdrToneMapOperators.Parse(App.Host?.Settings.Current.HdrToneMapOperator),
                    App.Host?.Settings.Current.HdrColorCorrection ?? true);
                ShowToast("Ring buffer recording", "The last 30, 60, or 90 seconds are ready to save from the tray.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start ring buffer:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        ringMenu.Items.Add(ringStartWindow);

        var ringStartMonitor = new MenuItem { Header = "Start for primary monitor" };
        ringStartMonitor.Click += (_, _) =>
        {
            try
            {
                var monitor = MonitorEnumerator.Enumerate().FirstOrDefault(item => item.IsPrimary)
                    ?? MonitorEnumerator.Enumerate().First();
                var q = RecordingPresets.GetQuality(App.Host?.Settings.Current.RecordingQuality ?? RecordingPresets.DefaultQuality);
                var (ow, oh) = RecordingPresets.ResolveOutputSize(
                    App.Host?.Settings.Current.RecordingResolution ?? RecordingPresets.NativeResolution, 0, 0);
                _ringBuffer.StartMonitor(
                    monitor.Handle, q.Fps, q.BitrateMbps, ow, oh,
                    App.Host?.Settings.Current.RecordingAutoTighten ?? false,
                    HdrToneMapOperators.Parse(App.Host?.Settings.Current.HdrToneMapOperator),
                    App.Host?.Settings.Current.HdrColorCorrection ?? true);
                ShowToast("Ring buffer recording", "The last 30, 60, or 90 seconds are ready to save from the tray.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not start ring buffer:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        ringMenu.Items.Add(ringStartMonitor);

        var ringSave30 = new MenuItem { Header = "Save last 30 seconds…" };
        var ringSave60 = new MenuItem { Header = "Save last 60 seconds…" };
        var ringSave90 = new MenuItem { Header = "Save last 90 seconds…" };
        async Task SaveRingAsync(TimeSpan duration)
        {
            if (!_ringBuffer.IsRunning)
                return;

            var path = await StoragePickerService.PickSaveFileAsync(
                owner: null,
                "MP4 video (*.mp4)|*.mp4",
                $"Snapture_Ring_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4",
                ".mp4",
                new[]
                {
                    new StoragePickerService.FileTypeChoice("MP4 video", new[] { ".mp4" })
                },
                title: "Save ring-buffer recording");
            if (path is null)
                return;

            try
            {
                await _ringBuffer.SaveRecentAsync(duration, path);
                ShowToast("Ring buffer saved", $"Saved the last {duration.TotalSeconds:0} seconds.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save ring buffer:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        ringSave30.Click += async (_, _) => await SaveRingAsync(TimeSpan.FromSeconds(30));
        ringSave60.Click += async (_, _) => await SaveRingAsync(TimeSpan.FromSeconds(60));
        ringSave90.Click += async (_, _) => await SaveRingAsync(TimeSpan.FromSeconds(90));
        ringMenu.Items.Add(ringSave30);
        ringMenu.Items.Add(ringSave60);
        ringMenu.Items.Add(ringSave90);

        var ringStop = new MenuItem { Header = "Stop ring buffer" };
        ringStop.Click += (_, _) =>
        {
            _ringBuffer.Stop();
            ShowToast("Ring buffer stopped", "The temporary rolling recording was discarded.");
        };
        ringMenu.Items.Add(ringStop);
        _ringBuffer.StateChanged += () =>
        {
            if (Application.Current.Dispatcher.CheckAccess())
                SyncRingMenu();
            else
                _ = Application.Current.Dispatcher.BeginInvoke(SyncRingMenu);
        };
        void SyncRingMenu()
        {
            bool running = _ringBuffer.IsRunning;
            ringStartWindow.IsEnabled = !running;
            ringStartMonitor.IsEnabled = !running;
            ringSave30.IsEnabled = running;
            ringSave60.IsEnabled = running;
            ringSave90.IsEnabled = running;
            ringStop.IsEnabled = running;
            ringMenu.ToolTip = running
                ? $"{_ringBuffer.Status} · {_ringBuffer.BufferedDuration:mm\\:ss} buffered"
                : _ringBuffer.Status;
        }
        SyncRingMenu();
        ringMenu.Items.Add(new Separator());
        recordVideo.Items.Add(ringMenu);

        tools.Items.Add(recordVideo);

        var batchProcess = new MenuItem { Header = "Batch process images…" };
        batchProcess.Click += (_, _) => new Views.BatchProcessWindow().ShowDialog();
        tools.Items.Add(batchProcess);

        var imageCombiner = new MenuItem { Header = "Combine images…" };
        imageCombiner.Click += (_, _) => new Views.ImageCombinerWindow().ShowDialog();
        tools.Items.Add(imageCombiner);

        var beforeAfterGif = new MenuItem { Header = "Before/after GIF…" };
        beforeAfterGif.Click += (_, _) => new Views.BeforeAfterGifWindow().ShowDialog();
        tools.Items.Add(beforeAfterGif);

        var codeAwareExport = new MenuItem { Header = "Code-aware export…" };
        codeAwareExport.Click += async (_, _) =>
        {
            var input = await StoragePickerService.PickOpenFileAsync(
                owner: null,
                "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff",
                new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff" },
                title: "Choose a code screenshot");
            if (input is null)
                return;
            var output = await StoragePickerService.PickSaveFileAsync(
                owner: null,
                "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap image (*.bmp)|*.bmp|WebP image (*.webp)|*.webp",
                "code-snapture.png",
                ".png",
                new[]
                {
                    new StoragePickerService.FileTypeChoice("PNG image", new[] { ".png" }),
                    new StoragePickerService.FileTypeChoice("JPEG image", new[] { ".jpg" }),
                    new StoragePickerService.FileTypeChoice("Bitmap image", new[] { ".bmp" }),
                    new StoragePickerService.FileTypeChoice("WebP image", new[] { ".webp" })
                },
                title: "Save code-aware export");
            if (output is null)
                return;
            try
            {
                var result = await CodeAwareCaptureService.CreateFromImageAsync(input, output);
                ShowToast("Code-aware export", $"Exported {result.Analysis.CodeLineCount} code lines.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create code-aware export:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        tools.Items.Add(codeAwareExport);

        var stepCapture = new MenuItem { Header = "Step capture…" };
        stepCapture.Click += (_, _) =>
        {
            try { new Views.StepCaptureWindow().Show(); }
            catch (Exception ex) { MessageBox.Show($"Could not open Step Capture:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        tools.Items.Add(stepCapture);

        var pinLatest = new MenuItem
        {
            Header = "Pin latest as Markdown",
            InputGestureText = "Ctrl+Alt+V"
        };
        pinLatest.Click += async (_, _) =>
        {
            try { await (App.Host?.PinLatestCaptureAsMarkdownAsync() ?? Task.CompletedTask); }
            catch (Exception ex) { ShowToast("Markdown pin failed", ex.Message); }
        };
        tools.Items.Add(pinLatest);

        var pluginsItem = new MenuItem { Header = "Plugins…" };
        pluginsItem.Click += (_, _) =>
        {
            try { new Views.PluginsWindow().Show(); }
            catch (Exception ex) { MessageBox.Show($"Could not open Plugins:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning); }
        };
        tools.Items.Add(pluginsItem);

        var externalCommands = new MenuItem { Header = "External commands" };
        var configureExternal = new MenuItem { Header = "Configure…" };
        configureExternal.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var dialog = new Views.ExternalCommandConfigurationWindow(App.Host.Settings.Current.ExternalCommands);
                if (dialog.ShowDialog() == true)
                {
                    App.Host.Settings.Current.ExternalCommands = dialog.Profiles.ToList();
                    App.Host.Settings.Save();
                    ShowToast("External commands saved", $"{dialog.Profiles.Count} profile(s) available in the editor.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not configure external commands:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        externalCommands.Items.Add(configureExternal);
        var runLatest = new MenuItem { Header = "Run on latest capture" };
        runLatest.Click += async (_, _) => await RunLatestExternalCommandAsync();
        externalCommands.Items.Add(runLatest);
        tools.Items.Add(externalCommands);

        var uploaders = new MenuItem { Header = "Declarative uploaders" };
        var configureUploaders = new MenuItem { Header = "Import .sxcu / JSON…" };
        configureUploaders.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var dialog = new Views.DeclarativeUploaderConfigurationWindow(App.Host.Settings.Current.DeclarativeUploaders);
                if (dialog.ShowDialog() == true)
                {
                    App.Host.Settings.Current.DeclarativeUploaders = dialog.Profiles.ToList();
                    App.Host.Settings.Save();
                    ShowToast("Declarative uploaders saved", $"{dialog.Profiles.Count} profile(s) available in the editor.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not configure declarative uploaders:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        uploaders.Items.Add(configureUploaders);
        var uploadLatest = new MenuItem { Header = "Upload latest capture" };
        uploadLatest.Click += async (_, _) => await UploadLatestCaptureAsync();
        uploaders.Items.Add(uploadLatest);
        tools.Items.Add(uploaders);

        var selfHosted = new MenuItem { Header = "Self-hosted destinations" };
        var configureSelfHosted = new MenuItem { Header = "Configure Nextcloud / Immich…" };
        configureSelfHosted.Click += (_, _) =>
        {
            try
            {
                if (App.Host is null) return;
                var dialog = new Views.SelfHostedDestinationsWindow(App.Host.Settings.Current);
                if (dialog.ShowDialog() == true)
                {
                    App.Host.Settings.Current.Nextcloud = dialog.Nextcloud;
                    App.Host.Settings.Current.Immich = dialog.Immich;
                    SaveSelfHostedCredentials(dialog);
                    App.Host.Settings.Save();
                    ShowToast("Self-hosted destinations saved", "Nextcloud and Immich remain opt-in.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not configure self-hosted destinations:\n{ex.Message}", "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        selfHosted.Items.Add(configureSelfHosted);
        var uploadSelfHosted = new MenuItem { Header = "Upload latest capture" };
        uploadSelfHosted.Click += async (_, _) => await UploadLatestSelfHostedAsync();
        selfHosted.Items.Add(uploadSelfHosted);
        tools.Items.Add(selfHosted);

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

        var openFolder = new MenuItem { Header = "Open _output folder" };
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
        var engineMenu = new MenuItem { Header = "Capture _engine" };
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

        var cursorToggle = new MenuItem
        {
            Header = "Include _cursor",
            IsCheckable = true,
            IsChecked = App.Host?.Settings.Current.IncludeCursor ?? true
        };
        cursorToggle.Click += (_, _) =>
        {
            if (App.Host is null) return;
            App.Host.Settings.Current.IncludeCursor = cursorToggle.IsChecked;
            App.Host.Settings.Save();
            App.Host.ApplyEngineSettings();
        };
        m.Items.Add(cursorToggle);

        // Restore PrintScreen if hijacked by Win11 24H2.
        if (PrintScreenHijackDetector.IsHijacked())
        {
            var reclaim = new MenuItem { Header = "Restore _PrintScreen shortcut" };
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

        var diagDump = new MenuItem { Header = "Create _diagnostic dump…" };
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

        var checkUpdate = new MenuItem { Header = "Check for _updates…" };
        checkUpdate.Click += async (_, _) =>
        {
            try
            {
                checkUpdate.IsEnabled = false;
                checkUpdate.Header = "Checking…";
                var result = await UpdateChecker.CheckAsync();
                checkUpdate.IsEnabled = true;
                checkUpdate.Header = "Check for _updates…";
                if (result.Error is not null)
                {
                    ShowToast("Update check failed", result.Error);
                }
                else if (result.Available)
                {
                    if (result.VelopackEnabled)
                    {
                        var downloadAnswer = MessageBox.Show(
                            $"A newer version is available: v{result.LatestVersion}\nYou are running: v{result.CurrentVersion}\n\nDownload it now?",
                            "Snapture Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                        if (downloadAnswer == MessageBoxResult.Yes)
                        {
                            checkUpdate.Header = "Downloading update…";
                            try
                            {
                                bool downloaded = await UpdateChecker.DownloadPendingAsync(
                                    progress => Application.Current.Dispatcher.BeginInvoke(() =>
                                        checkUpdate.Header = $"Downloading update… {progress}%"));
                                if (downloaded)
                                {
                                    var restartAnswer = MessageBox.Show(
                                        "The update is ready. Restart Snapture now to apply it?",
                                        "Snapture Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                                    if (restartAnswer == MessageBoxResult.Yes)
                                    {
                                        bool applied = UpdateChecker.ApplyPendingAndRestart();
                                        if (applied)
                                        {
                                            App.Host?.Dispose();
                                            return;
                                        }

                                        ShowToast("Update restart failed", "The downloaded update is still staged; try again from the tray menu.");
                                    }
                                }
                                else
                                {
                                    ShowToast("Update download failed", result.Error ?? "The update could not be staged.");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "Velopack.UpdateDownloadFailed");
                                ShowToast("Update download failed", ex.Message);
                            }
                            finally
                            {
                                checkUpdate.IsEnabled = true;
                                checkUpdate.Header = "Check for _updates…";
                            }
                        }
                        return;
                    }

                    var answer = MessageBox.Show(
                        $"A newer version is available: v{result.LatestVersion}\nYou are running: v{result.CurrentVersion}\n\nOpen the release page?",
                        "Snapture Update", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (answer == MessageBoxResult.Yes && result.HtmlUrl is not null)
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(result.HtmlUrl) { UseShellExecute = true });
                }
                else
                {
                    ShowToast("Up to date", $"Snapture v{result.CurrentVersion} is the latest.");
                }
            }
            catch
            {
                checkUpdate.IsEnabled = true;
                checkUpdate.Header = "Check for _updates…";
                ShowToast("Update check failed", "Could not reach GitHub.");
            }
        };
        m.Items.Add(checkUpdate);

        var about = new MenuItem { Header = "_About Snapture" };
        about.Click += (_, _) =>
        {
            var ver = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            MessageBox.Show(
                $"Snapture v{ver}\n\nAll-in-one screenshot utility for Windows.\nEngine: {App.Host?.EngineName.ToUpperInvariant()}\nTheme: {ThemeManager.DisplayName(App.Host?.Settings.Current.ThemeMode)} ({ThemeManager.EffectiveMode})\nRedact rules: {Editor.SecretDetector.RulePackVersion} ({Editor.SecretDetector.Rules.Count} rules, {Editor.SecretDetector.RulePackSource})\nMIT License - github.com/SysAdminDoc/Snapture",
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
        title = LocalizationService.Get(title);
        message = LocalizationService.Get(message);
        try { _tray.ShowBalloonTip(title, message, BalloonIcon.Info); }
        catch { /* non-fatal */ }
    }

    private async Task RunLatestExternalCommandAsync()
    {
        if (App.Host is null) return;
        var profiles = App.Host.Settings.Current.ExternalCommands ?? new List<ExternalCommandProfile>();
        if (profiles.Count == 0)
        {
            ShowToast("No external commands", "Configure a profile in Tools → External commands → Configure.");
            return;
        }
        var latest = App.Host.History.Recent(1).FirstOrDefault();
        if (latest is null || !File.Exists(latest.FilePath))
        {
            ShowToast("No capture available", "Take or save a capture before running an external command.");
            return;
        }

        ExternalCommandProfile? profile = profiles.Count == 1
            ? profiles[0].Clone()
            : ChooseExternalCommand(profiles);
        if (profile is null) return;
        try
        {
            ShowToast("External command", $"Running {profile.Name}…");
            var result = await ExternalCommandService.RunAsync(
                profile,
                ExternalCommandRequest.FromFile(latest.FilePath, latest.Source, latest.Width, latest.Height, latest.CapturedAtUtc));
            ShowToast(
                result.ExitCode == 0 ? "External command complete" : "External command failed",
                result.ExitCode == 0
                    ? $"{profile.Name} finished in {result.Duration.TotalSeconds:F1}s."
                    : $"{profile.Name} exited with code {result.ExitCode}.");
        }
        catch (Exception ex)
        {
            ShowToast("External command failed", ex.Message);
        }
    }

    private static ExternalCommandProfile? ChooseExternalCommand(IReadOnlyList<ExternalCommandProfile> profiles)
    {
        var picker = new Views.ExternalCommandPickerWindow(profiles);
        return picker.ShowDialog() == true ? picker.SelectedProfile : null;
    }

    private async Task UploadLatestCaptureAsync()
    {
        if (App.Host is null) return;
        var profiles = App.Host.Settings.Current.DeclarativeUploaders ?? new List<DeclarativeUploaderProfile>();
        if (profiles.Count == 0)
        {
            ShowToast("No uploaders imported", "Import a ShareX .sxcu or compatible JSON profile first.");
            return;
        }
        var latest = App.Host.History.Recent(1).FirstOrDefault();
        if (latest is null || !File.Exists(latest.FilePath))
        {
            ShowToast("No capture available", "Take or save a capture before uploading.");
            return;
        }
        DeclarativeUploaderProfile? profile = profiles.Count == 1
            ? profiles[0].Clone()
            : ChooseUploader(profiles);
        if (profile is null) return;
        try
        {
            ShowToast("Uploader", $"Uploading to {profile.Name}…");
            var result = await DeclarativeUploaderService.UploadAsync(
                profile,
                new DeclarativeUploaderRequest(
                    await SafeImageInput.ReadAllBytesAsync(latest.FilePath),
                    Path.GetFileName(latest.FilePath),
                    latest.Source,
                    latest.Width,
                    latest.Height,
                    latest.CapturedAtUtc));
            ShowToast(
                result.Succeeded ? "Upload complete" : "Upload failed",
                result.Succeeded ? result.Url ?? $"{profile.Name} completed." : result.ErrorMessage ?? $"HTTP {result.StatusCode}");
        }
        catch (Exception ex)
        {
            ShowToast("Upload failed", ex.Message);
        }
    }

    private static DeclarativeUploaderProfile? ChooseUploader(IReadOnlyList<DeclarativeUploaderProfile> profiles)
    {
        var picker = new Views.DeclarativeUploaderPickerWindow(profiles);
        return picker.ShowDialog() == true ? picker.SelectedProfile : null;
    }

    private static void SaveSelfHostedCredentials(Views.SelfHostedDestinationsWindow dialog)
    {
        if (dialog.RemoveNextcloudCredential)
            SelfHostedDestinationService.RemoveCredential(SelfHostedDestinationKind.Nextcloud);
        if (dialog.NextcloudCredential is { Length: > 0 })
            SelfHostedDestinationService.SetCredential(SelfHostedDestinationKind.Nextcloud, dialog.NextcloudCredential);
        if (dialog.RemoveImmichCredential)
            SelfHostedDestinationService.RemoveCredential(SelfHostedDestinationKind.Immich);
        if (dialog.ImmichCredential is { Length: > 0 })
            SelfHostedDestinationService.SetCredential(SelfHostedDestinationKind.Immich, dialog.ImmichCredential);
    }

    private async Task UploadLatestSelfHostedAsync()
    {
        if (App.Host is null) return;
        var enabled = SelfHostedDestinationService.EnabledDestinations(App.Host.Settings.Current);
        if (enabled.Count == 0)
        {
            ShowToast("No self-hosted destinations", "Enable Nextcloud or Immich in Tools → Self-hosted destinations.");
            return;
        }
        var latest = App.Host.History.Recent(1).FirstOrDefault();
        if (latest is null || !File.Exists(latest.FilePath))
        {
            ShowToast("No capture available", "Take or save a capture before uploading.");
            return;
        }
        SelfHostedDestinationKind destination;
        if (enabled.Count == 1)
            destination = enabled[0];
        else
        {
            var picker = new Views.SelfHostedDestinationPickerWindow(enabled);
            if (picker.ShowDialog() != true || picker.SelectedDestination is not { } selected)
                return;
            destination = selected;
        }
        string? credential = SelfHostedDestinationService.GetCredential(destination);
        if (string.IsNullOrWhiteSpace(credential))
        {
            ShowToast("Credential missing", $"Configure the {destination} credential before uploading.");
            return;
        }
        try
        {
            ShowToast("Self-hosted upload", $"Uploading to {destination}…");
            var request = new SelfHostedUploadRequest(
                await SafeImageInput.ReadAllBytesAsync(latest.FilePath),
                Path.GetFileName(latest.FilePath),
                latest.Source,
                latest.Width,
                latest.Height,
                latest.CapturedAtUtc);
            var result = destination == SelfHostedDestinationKind.Nextcloud
                ? await SelfHostedDestinationService.UploadNextcloudAsync(App.Host.Settings.Current.Nextcloud, credential, request)
                : await SelfHostedDestinationService.UploadImmichAsync(App.Host.Settings.Current.Immich, credential, request);
            ShowToast(
                result.Succeeded ? "Upload complete" : "Upload failed",
                result.Succeeded ? $"Uploaded to {destination}." : result.ErrorMessage ?? $"HTTP {result.StatusCode}");
        }
        catch (Exception ex)
        {
            ShowToast("Upload failed", ex.Message);
        }
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

    public void Dispose()
    {
        _ringBuffer.Dispose();
        _tray.Dispose();
    }
}
