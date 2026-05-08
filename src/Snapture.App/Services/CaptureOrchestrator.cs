using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Snapture.App.Views;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class CaptureOrchestrator
{
    private readonly SettingsService _settings;
    private ICaptureEngine _engine;

    public CaptureOrchestrator(SettingsService settings, ICaptureEngine engine)
    {
        _settings = settings;
        _engine = engine;
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

    public async Task CaptureWithDelayAsync(Func<Task> capture, int delaySeconds)
    {
        if (delaySeconds > 0)
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds)).ConfigureAwait(true);
        await capture().ConfigureAwait(true);
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
        // Save to disk
        string? savedPath = null;
        try
        {
            Directory.CreateDirectory(_settings.Current.OutputFolder);
            savedPath = BuildOutputPath(_settings.Current);
            using var fs = File.Create(savedPath);
            var fmt = _settings.Current.OutputFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase)
                ? ImageFormat.Jpeg : ImageFormat.Png;
            result.Bitmap.Save(fs, fmt);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not save capture:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (_settings.Current.CopyToClipboard)
        {
            try
            {
                var bs = ToBitmapSource(result.Bitmap);
                Clipboard.SetImage(bs);
            }
            catch { /* clipboard contention is not fatal */ }
        }

        if (_settings.Current.OpenEditorAfterCapture)
        {
            var bs = ToBitmapSource(result.Bitmap);
            var editor = new EditorWindow(bs, savedPath, result);
            editor.Show();
            editor.Activate();
        }

        await Task.CompletedTask;
    }

    private static string BuildOutputPath(SnaptureSettings s)
    {
        var now = DateTime.Now;
        string baseName = System.Text.RegularExpressions.Regex.Replace(
            s.FilenamePattern,
            @"\{([^}]+)\}",
            m => now.ToString(m.Groups[1].Value));
        string ext = s.OutputFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
        string path = Path.Combine(s.OutputFolder, baseName + ext);
        int n = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(s.OutputFolder, $"{baseName}_{n++}{ext}");
        }
        return path;
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
