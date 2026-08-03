using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Serilog;
using Snapture.App.Views;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class CaptureOrchestrator
{
    private readonly SettingsService _settings;
    private readonly CaptureHistoryService? _history;
    private ICaptureEngine _engine;

    public CaptureOrchestrator(SettingsService settings, ICaptureEngine engine, CaptureHistoryService? history = null)
    {
        _settings = settings;
        _engine = engine;
        _history = history;
    }

    public void ReplaceEngine(ICaptureEngine engine) => _engine = engine;

    public async Task CaptureRegionAsync()
    {
        var virtualBounds = MonitorEnumerator.GetVirtualScreen();
        var virtualCapture = await _engine.CaptureRegionAsync(virtualBounds).ConfigureAwait(true);
        try
        {
            var overlay = new RegionOverlayWindow(virtualCapture.Bitmap, virtualBounds);
            overlay.ShowDialog();
            var sel = overlay.GetSelectedRegionAsRectangle();
            if (sel is null) return;

            var crop = CropFromVirtual(virtualCapture.Bitmap, virtualBounds, sel.Value);

            // Persist last region for Shift+PrintScreen recapture
            _settings.Current.LastRegion = new CaptureRect(sel.Value.X, sel.Value.Y, sel.Value.Width, sel.Value.Height);
            _settings.Save();

            await DeliverCaptureAsync(new CaptureResult(crop, sel.Value, DateTime.UtcNow, "Region")).ConfigureAwait(true);
        }
        finally
        {
            virtualCapture.Bitmap.Dispose();
        }
    }

    public async Task CaptureLastRegionAsync()
    {
        var rect = _settings.Current.LastRegion;
        if (rect is null)
        {
            // Fall back to the standard region picker if there's no remembered region yet.
            await CaptureRegionAsync();
            return;
        }
        var bounds = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        var result = await _engine.CaptureRegionAsync(bounds).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureFullscreenAsync()
    {
        var result = await _engine.CaptureVirtualScreenAsync().ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureForegroundWindowAsync()
    {
        var hwnd = Native2.GetForegroundWindow();
        if (hwnd == 0) return;
        var result = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureWindowPickerAsync()
    {
        // Show the hover-highlight overlay; user picks the window with click.
        var picker = new WindowPickerWindow();
        var hwnd = picker.PickWindowSync();
        if (hwnd == 0) return;
        var result = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureMonitorAsync(MonitorInfo m)
    {
        var result = await _engine.CaptureMonitorAsync(m).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureViaPickerAsync()
    {
        var mode = Views.CapturePickerWindow.PickMode();
        if (mode is null) return;
        switch (mode.Value)
        {
            case Views.CapturePickerWindow.CaptureMode.Region: await CaptureRegionAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.Window: await CaptureForegroundWindowAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.Fullscreen: await CaptureFullscreenAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.LastRegion: await CaptureLastRegionAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.MonitorUnderCursor: await CaptureMonitorUnderCursorAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.ScrollingWindow: await CaptureScrollingForegroundAsync(); break;
            case Views.CapturePickerWindow.CaptureMode.SmartElement: await CaptureSmartElementAsync(); break;
        }
    }

    public async Task CaptureMonitorUnderCursorAsync()
    {
        GetCursorPos(out var pt);
        var mon = MonitorEnumerator.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
        if (mon is null) return;
        var result = await _engine.CaptureMonitorAsync(mon).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    public async Task CaptureTextAsync()
    {
        var virtualBounds = MonitorEnumerator.GetVirtualScreen();
        var virtualCapture = await _engine.CaptureRegionAsync(virtualBounds).ConfigureAwait(true);
        try
        {
            var overlay = new Views.RegionOverlayWindow(virtualCapture.Bitmap, virtualBounds);
            overlay.ShowDialog();
            var sel = overlay.GetSelectedRegionAsRectangle();
            if (sel is null) return;

            var crop = CropFromVirtual(virtualCapture.Bitmap, virtualBounds, sel.Value);
            try
            {
                var bs = ToBitmapSource(crop);
                var result = await OcrService.RecognizeAsync(bs).ConfigureAwait(true);
                string text = result?.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(text))
                {
                    try { Clipboard.SetText(text); } catch { }
                    Log.Information("CaptureText.Completed {Length}", text.Length);
                }
            }
            finally { crop.Dispose(); }
        }
        finally { virtualCapture.Bitmap.Dispose(); }
    }

    public async Task CaptureWithDelayAsync(Func<Task> capture, int delaySeconds)
    {
        if (delaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(true);
        await capture().ConfigureAwait(true);
    }

    public async Task CaptureSmartElementAsync()
    {
        var picker = new SmartCaptureWindow();
        picker.PickSync();
        var bounds = picker.SelectedBounds;
        if (bounds is null) return;
        var result = await _engine.CaptureRegionAsync(bounds.Value).ConfigureAwait(true);
        await DeliverCaptureAsync(new CaptureResult(
            result.Bitmap, bounds.Value, DateTime.UtcNow,
            $"Smart:{picker.SelectedDescription ?? "element"}", IsHdr: result.IsHdr)).ConfigureAwait(true);
    }

    public async Task CaptureScrollingForegroundAsync()
    {
        var hwnd = Native2.GetForegroundWindow();
        if (hwnd == 0) return;
        var svc = new ScrollingCaptureService(_engine);
        var bmp = await svc.CaptureScrollingForegroundAsync(hwnd, new Progress<string>(_ => { })).ConfigureAwait(true);
        if (bmp is null)
        {
            MessageBox.Show(
                "Snapture could not drive a scrolling capture on the active window.\n\n" +
                "This means the window does not expose a UIA scroll pattern. Most browsers, " +
                "Word, Excel and PowerPoint route scroll through their own custom hosts; " +
                "image-stitching fallback ships in v0.4.",
                "Snapture", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await DeliverCaptureAsync(new CaptureResult(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
            DateTime.UtcNow, "Scrolling", hwnd)).ConfigureAwait(true);
    }

    public async Task OcrRegionAsync()
    {
        var virtualBounds = MonitorEnumerator.GetVirtualScreen();
        var virtualCapture = await _engine.CaptureRegionAsync(virtualBounds).ConfigureAwait(true);
        try
        {
            var overlay = new RegionOverlayWindow(virtualCapture.Bitmap, virtualBounds);
            overlay.ShowDialog();
            var sel = overlay.GetSelectedRegionAsRectangle();
            if (sel is null) return;

            var crop = CropFromVirtual(virtualCapture.Bitmap, virtualBounds, sel.Value);
            try
            {
                var bs = ToBitmapSource(crop);
                var result = await OcrService.RecognizeAsync(bs).ConfigureAwait(true);
                string text = result?.Text ?? string.Empty;
                try { if (!string.IsNullOrEmpty(text)) Clipboard.SetText(text); } catch { }
                var win = new OcrResultWindow(text, OcrService.AvailableLanguages().FirstOrDefault());
                win.Show();
                win.Activate();
            }
            finally { crop.Dispose(); }
        }
        finally
        {
            virtualCapture.Bitmap.Dispose();
        }
    }

    private static Bitmap CropFromVirtual(Bitmap virtualBmp, Rectangle virtualBounds, Rectangle target)
    {
        int x = target.X - virtualBounds.X;
        int y = target.Y - virtualBounds.Y;
        x = Math.Max(0, x); y = Math.Max(0, y);
        int w = Math.Min(target.Width, virtualBmp.Width - x);
        int h = Math.Min(target.Height, virtualBmp.Height - y);
        var src = new Rectangle(x, y, w, h);
        var crop = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(crop);
        g.DrawImage(virtualBmp, new Rectangle(0, 0, w, h), src, GraphicsUnit.Pixel);
        return crop;
    }

    private async Task DeliverCaptureAsync(CaptureResult result)
    {
        // Run plugin capture-processors first so any resize/redact lands in the saved file
        // and the history index. Failures are non-fatal.
        try
        {
            var host = App.Host;
            if (host is not null)
            {
                foreach (var plugin in host.Plugins.All)
                {
                    foreach (var proc in plugin.CaptureProcessors)
                    {
                        if (!proc.RunsByDefault) continue;
                        try
                        {
                            var pluginCapture = ToPluginCapture(result, path: null);
                            var processed = await proc.ProcessAsync(pluginCapture, host.PluginHost).ConfigureAwait(true);
                            if (!ReferenceEquals(processed, pluginCapture))
                                result = ApplyPluginCaptureBack(processed, result);
                        }
                        catch (Exception ex)
                        {
                            host.PluginHost.Log($"Processor failed ({proc.Id}): {ex.Message}");
                        }
                    }
                }
            }
        }
        catch { }

        try { new Views.CaptureFlashWindow().Flash(); } catch { }

        if (_settings.Current.PlayShutterSound)
        {
            try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
        }

        // Auto-border
        if (_settings.Current.AutoBorderOnCapture)
        {
            try
            {
                using var g = Graphics.FromImage(result.Bitmap);
                uint argb = _settings.Current.AutoBorderColor;
                var color = Color.FromArgb((int)argb);
                using var pen = new Pen(color, 1f);
                g.DrawRectangle(pen, 0, 0, result.Bitmap.Width - 1, result.Bitmap.Height - 1);
            }
            catch { }
        }

        // Save to disk
        string? savedPath = null;
        try
        {
            Directory.CreateDirectory(_settings.Current.OutputFolder);
            string configuredPath = BuildOutputPath(_settings.Current, result);
            if (result.IsHdr)
            {
                string stem = Path.Combine(
                    Path.GetDirectoryName(configuredPath) ?? _settings.Current.OutputFolder,
                    Path.GetFileNameWithoutExtension(configuredPath));
                var variants = HdrSavePolicy.Save(stem, result.Bitmap, _settings.Current.HdrWriteJxr);
                savedPath = variants.PngPath;
                Log.Information("Capture.SavedHdr {Source} {Width}x{Height} Variants={Variants}",
                    result.Source, result.Bitmap.Width, result.Bitmap.Height, variants.WrittenCount);
            }
            else
            {
                savedPath = configuredPath;
                using var fs = File.Create(savedPath);
                var fmt = _settings.Current.OutputFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase)
                    ? ImageFormat.Jpeg : ImageFormat.Png;
                result.Bitmap.Save(fs, fmt);
                Log.Information("Capture.Saved {Source} {Width}x{Height}",
                    result.Source, result.Bitmap.Width, result.Bitmap.Height);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Capture.Save.Failed");
            MessageBox.Show($"Could not save capture:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Index in history (best-effort — never block the user)
        try
        {
            if (_history is not null && savedPath is not null)
            {
                var (proc, title) = result.SourceWindow is { } hwnd
                    ? CaptureHistoryService.DescribeForeground(hwnd)
                    : (null, null);
                _history.Add(savedPath, result.Source, proc, title,
                    result.Bitmap.Width, result.Bitmap.Height, ocrText: null);
            }
        }
        catch { /* history failures must never block delivery */ }

        if (_settings.Current.CopyToClipboard || _settings.Current.QuickMode)
        {
            try
            {
                var bs = ToBitmapSource(result.Bitmap);
                Clipboard.SetImage(bs);
            }
            catch { /* clipboard contention is not fatal */ }
        }

        if (_settings.Current.OpenEditorAfterCapture && !_settings.Current.QuickMode)
        {
            var bs = ToBitmapSource(result.Bitmap);
            var editor = new EditorWindow(bs, savedPath, result);
            editor.Show();
            editor.Activate();
        }

        await Task.CompletedTask;
    }

    private static string BuildOutputPath(SnaptureSettings s, CaptureResult? capture = null)
    {
        var now = DateTime.Now;

        string? processName = null;
        string? windowTitle = null;
        if (capture?.SourceWindow is { } hwnd && hwnd != 0)
        {
            var (proc, title) = CaptureHistoryService.DescribeForeground(hwnd);
            processName = proc;
            windowTitle = title;
        }

        string baseName = System.Text.RegularExpressions.Regex.Replace(
            s.FilenamePattern,
            @"\{([^}]+)\}",
            m =>
            {
                var token = m.Groups[1].Value;
                return token switch
                {
                    "ProcessName" => SanitizeFilename(processName ?? "unknown"),
                    "WindowTitle" => SanitizeFilename(windowTitle ?? "untitled"),
                    "Width" => capture?.Bitmap.Width.ToString() ?? "0",
                    "Height" => capture?.Bitmap.Height.ToString() ?? "0",
                    "MonitorIndex" => ResolveMonitorIndex(capture),
                    "MonitorDpi" => ResolveMonitorDpi(capture),
                    "HDR" => capture?.IsHdr == true ? "Y" : "N",
                    _ => now.ToString(token)
                };
            });
        string ext = s.OutputFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
        string path = Path.Combine(s.OutputFolder, baseName + ext);
        int n = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(s.OutputFolder, $"{baseName}_{n++}{ext}");
        }
        return path;
    }

    private static string ResolveMonitorIndex(CaptureResult? capture)
    {
        if (capture is null) return "0";
        var monitors = MonitorEnumerator.Enumerate();
        var pt = new System.Drawing.Point(
            capture.SourceBounds.X + capture.SourceBounds.Width / 2,
            capture.SourceBounds.Y + capture.SourceBounds.Height / 2);
        for (int i = 0; i < monitors.Count; i++)
            if (monitors[i].Bounds.Contains(pt)) return (i + 1).ToString();
        return "0";
    }

    private static string ResolveMonitorDpi(CaptureResult? capture)
    {
        if (capture is null) return "96";
        var pt = new System.Drawing.Point(
            capture.SourceBounds.X + capture.SourceBounds.Width / 2,
            capture.SourceBounds.Y + capture.SourceBounds.Height / 2);
        var mon = MonitorEnumerator.FromPoint(pt);
        return mon?.DpiX.ToString() ?? "96";
    }

    private static string SanitizeFilename(string raw)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new System.Text.StringBuilder(raw.Length);
        foreach (char c in raw)
            clean.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        var result = clean.ToString().Trim();
        return result.Length > 60 ? result[..60] : result;
    }

    private static Snapture.Plugin.PluginCapture ToPluginCapture(CaptureResult r, string? path)
    {
        var bmp = r.Bitmap;
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new Snapture.Plugin.PluginCapture(
                pixels, bmp.Width, bmp.Height, data.Stride,
                r.Source, r.CapturedAtUtc, path);
        }
        finally { bmp.UnlockBits(data); }
    }

    private static CaptureResult ApplyPluginCaptureBack(Snapture.Plugin.PluginCapture src, CaptureResult dst)
    {
        // Same dimensions: copy pixels in place (cheap).
        if (src.Width == dst.Bitmap.Width && src.Height == dst.Bitmap.Height)
        {
            var rect = new Rectangle(0, 0, dst.Bitmap.Width, dst.Bitmap.Height);
            var data = dst.Bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int copyLen = Math.Min(src.PixelsBgra.Length, data.Stride * dst.Bitmap.Height);
                System.Runtime.InteropServices.Marshal.Copy(src.PixelsBgra, 0, data.Scan0, copyLen);
            }
            finally { dst.Bitmap.UnlockBits(data); }
            return dst;
        }

        // Resized: build a new Bitmap and CaptureResult, dispose the old one. The plugin
        // contract validated in PluginCapture: src.Stride may differ from src.Width*4 if the
        // plugin packed rows tightly, so honour the reported stride.
        var resized = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        var lock2 = resized.LockBits(new Rectangle(0, 0, src.Width, src.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int srcRow = src.Stride;
            int dstRow = lock2.Stride;
            int copyBytes = Math.Min(srcRow, dstRow);
            for (int y = 0; y < src.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(
                    src.PixelsBgra, y * srcRow,
                    lock2.Scan0 + y * dstRow,
                    copyBytes);
            }
        }
        finally { resized.UnlockBits(lock2); }

        var newBounds = new Rectangle(0, 0, src.Width, src.Height);
        dst.Bitmap.Dispose();
        return new CaptureResult(resized, newBounds, dst.CapturedAtUtc, dst.Source, dst.SourceWindow, dst.IsHdr);
    }

    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.StreamSource = ms;
        bi.EndInit();
        bi.Freeze();
        return bi;
    }
}

internal static class Native2
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
}
