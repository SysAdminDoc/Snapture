using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using ImageMagick;
using Serilog;
using Snapture.App.Views;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed record CaptureDeliveryResult(
    string? SavedPath,
    string? LanUrl,
    string? MetadataNotice = null);

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

    public Task CaptureRegionAsync() => CaptureRegionAsync(null, null);

    internal async Task CaptureRegionAsync(
        bool? copyToClipboardOverride,
        bool? openEditorOverride)
    {
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
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

            await DeliverCaptureAsync(
                new CaptureResult(crop, sel.Value, DateTime.UtcNow, "Region"),
                copyToClipboardOverride: copyToClipboardOverride,
                openEditorOverride: openEditorOverride).ConfigureAwait(true);
        }
        finally
        {
            virtualCapture.Bitmap.Dispose();
        }
    }

    public Task CaptureLastRegionAsync() => CaptureLastRegionAsync(null, null);

    internal async Task CaptureLastRegionAsync(
        bool? copyToClipboardOverride,
        bool? openEditorOverride)
    {
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
        var rect = _settings.Current.LastRegion;
        if (rect is null)
        {
            // Fall back to the standard region picker if there's no remembered region yet.
            await CaptureRegionAsync(copyToClipboardOverride, openEditorOverride);
            return;
        }
        var bounds = new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        var result = await _engine.CaptureRegionAsync(bounds).ConfigureAwait(true);
        await DeliverCaptureAsync(
            result,
            copyToClipboardOverride: copyToClipboardOverride,
            openEditorOverride: openEditorOverride).ConfigureAwait(true);
    }

    public Task CaptureFullscreenAsync() => CaptureFullscreenAsync(null, null);

    internal async Task CaptureFullscreenAsync(
        bool? copyToClipboardOverride,
        bool? openEditorOverride)
    {
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
        var result = await _engine.CaptureVirtualScreenAsync().ConfigureAwait(true);
        await DeliverCaptureAsync(
            result,
            copyToClipboardOverride: copyToClipboardOverride,
            openEditorOverride: openEditorOverride).ConfigureAwait(true);
    }

    public Task CaptureForegroundWindowAsync() => CaptureForegroundWindowAsync(null, null);

    internal async Task CaptureForegroundWindowAsync(
        bool? copyToClipboardOverride,
        bool? openEditorOverride)
    {
        using var desktopIcons = BeginDesktopIconScope();
        var hwnd = Native2.GetForegroundWindow();
        if (hwnd == 0) return;
        ApplyForegroundProfile(hwnd);
        var result = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
        await DeliverCaptureAsync(
            result,
            copyToClipboardOverride: copyToClipboardOverride,
            openEditorOverride: openEditorOverride).ConfigureAwait(true);
    }

    public async Task CaptureWindowPickerAsync()
    {
        using var desktopIcons = BeginDesktopIconScope();
        // Show the hover-highlight overlay; user picks the window with click.
        var picker = new WindowPickerWindow();
        var hwnd = picker.PickWindowSync();
        if (hwnd == 0) return;
        ApplyForegroundProfile(hwnd);
        var result = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(true);
        await DeliverCaptureAsync(result).ConfigureAwait(true);
    }

    public async Task CaptureMonitorAsync(MonitorInfo m)
    {
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
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
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
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
        using var desktopIcons = BeginDesktopIconScope();
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
        ApplyForegroundProfile();
        using var desktopIcons = BeginDesktopIconScope();
        var picker = new SmartCaptureWindow();
        picker.PickSync();
        var bounds = picker.SelectedBounds;
        if (bounds is null) return;
        var result = await _engine.CaptureRegionAsync(bounds.Value).ConfigureAwait(true);
        await DeliverCaptureAsync(new CaptureResult(
            result.Bitmap, bounds.Value, DateTime.UtcNow,
            $"Smart:{picker.SelectedDescription ?? "element"}", IsHdr: result.IsHdr)).ConfigureAwait(true);
    }

    public Task CaptureScrollingForegroundAsync() => CaptureScrollingForegroundAsync(null, null);

    public Task CaptureHorizontalScrollingForegroundAsync() =>
        CaptureScrollingForegroundAsync(null, null, ScrollCaptureDirection.Horizontal);

    public Task CaptureOmnidirectionalScrollingForegroundAsync() =>
        CaptureScrollingForegroundAsync(null, null, ScrollCaptureDirection.Both);

    internal async Task CaptureScrollingForegroundAsync(
        bool? copyToClipboardOverride,
        bool? openEditorOverride,
        ScrollCaptureDirection direction = ScrollCaptureDirection.Vertical)
    {
        using var desktopIcons = BeginDesktopIconScope();
        var hwnd = Native2.GetForegroundWindow();
        if (hwnd == 0) return;
        ApplyForegroundProfile(hwnd);
        var svc = new ScrollingCaptureService(_engine);
        var preview = new ScrollingPreviewWindow();
        preview.Show();
        try
        {
            var bmp = await svc.CaptureScrollingForegroundAsync(
                hwnd,
                new Progress<string>(preview.UpdateStatus),
                preview.UpdateFrame,
                direction).ConfigureAwait(true);
            if (bmp is null)
            {
                MessageBox.Show(
                    "Snapture could not drive a scrolling capture on the active window.\n\n" +
                    "The window did not expose a usable UIA scroll pattern. Chromium 130+, " +
                    "Word, Excel and PowerPoint are supported when their scroll host is available.",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            preview.UpdateStatus("Stitching complete");
            await DeliverCaptureAsync(
                new CaptureResult(
                    bmp, new Rectangle(0, 0, bmp.Width, bmp.Height),
                    DateTime.UtcNow, direction == ScrollCaptureDirection.Vertical ? "Scrolling" : "Omnidirectional scrolling", hwnd),
                copyToClipboardOverride: copyToClipboardOverride,
                openEditorOverride: openEditorOverride).ConfigureAwait(true);
        }
        finally
        {
            preview.Close();
        }
    }

    public async Task CaptureUriAsync(CaptureUriRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        switch (request.Mode)
        {
            case UriCaptureMode.Region:
                await CaptureRegionAsync(request.CopyToClipboardOverride, request.OpenEditorOverride);
                break;
            case UriCaptureMode.Window:
                await CaptureForegroundWindowAsync(request.CopyToClipboardOverride, request.OpenEditorOverride);
                break;
            case UriCaptureMode.Fullscreen:
                await CaptureFullscreenAsync(request.CopyToClipboardOverride, request.OpenEditorOverride);
                break;
            case UriCaptureMode.Scrolling:
                await CaptureScrollingForegroundAsync(request.CopyToClipboardOverride, request.OpenEditorOverride);
                break;
            case UriCaptureMode.LastRegion:
                await CaptureLastRegionAsync(request.CopyToClipboardOverride, request.OpenEditorOverride);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Mode));
        }
    }

    public async Task OcrRegionAsync()
    {
        using var desktopIcons = BeginDesktopIconScope();
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
                var win = new OcrResultWindow(text, OcrService.AvailableLanguages().FirstOrDefault(), result?.Engine);
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

    private IDisposable? BeginDesktopIconScope() =>
        _settings.Current.HideDesktopIcons
            ? DesktopIconVisibilityService.TryHide()
            : null;

    private void ApplyForegroundProfile() =>
        ApplyForegroundProfile(Native2.GetForegroundWindow());

    private void ApplyForegroundProfile(nint hwnd)
    {
        if (_settings.Current.PerAppCaptureProfiles.Count == 0)
            return;

        if (CaptureAppProfileService.ApplyForWindow(hwnd, _settings.Current))
        {
            _settings.Save();
            Log.Information(
                "Capture.ProfileApplied {WindowClass} {Preset}",
                CaptureAppProfileService.GetWindowClassName(hwnd) ?? "unknown",
                _settings.Current.ActiveCapturePreset);
        }
    }

    public Task<CaptureDeliveryResult> DeliverCaptureForCliAsync(
        CaptureResult result,
        string? outputPath,
        bool? copyToClipboard,
        LanShareServer? lanShare = null,
        ExportMetadataOptions? metadataOptions = null) =>
        DeliverCaptureAsync(
            result,
            outputPath,
            copyToClipboardOverride: copyToClipboard,
            openEditorOverride: false,
            lanShare: lanShare,
            showUi: false,
            metadataOptions: metadataOptions);

    private async Task<CaptureDeliveryResult> DeliverCaptureAsync(
        CaptureResult result,
        string? outputPathOverride = null,
        bool? copyToClipboardOverride = null,
        bool? openEditorOverride = null,
        LanShareServer? lanShare = null,
        bool showUi = true,
        ExportMetadataOptions? metadataOptions = null)
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
                            var processed = await PluginProcessorInvoker.ProcessAsync(
                                proc, pluginCapture, plugin.Host).ConfigureAwait(true);
                            if (!ReferenceEquals(processed, pluginCapture))
                                result = ApplyPluginCaptureBack(processed, result);
                        }
                        catch (Exception ex)
                        {
                            plugin.Host.Log($"Processor failed ({proc.Id}): {ex.Message}");
                        }
                    }
                }
            }
        }
        catch { }

        if (showUi)
        {
            try { new Views.CaptureFlashWindow().Flash(); } catch { }
        }

        if (showUi && _settings.Current.PlayShutterSound)
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
        string? metadataNotice = null;
        try
        {
            string configuredPath = outputPathOverride is null
                ? BuildOutputPath(_settings.Current, result)
                : ResolveExplicitOutputPath(outputPathOverride, _settings.Current);
            Directory.CreateDirectory(
                Path.GetDirectoryName(configuredPath) ?? _settings.Current.OutputFolder);
            var exportOptions = metadataOptions ?? ExportMetadataService.FromSettings(_settings.Current);
            string extension = Path.GetExtension(configuredPath);
            if (!ExportMetadataService.TryGetFormat(configuredPath, out var exportFormat)
                || exportFormat is MagickFormat.Jxl or MagickFormat.Avif)
            {
                exportFormat = extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    ? MagickFormat.Jpeg
                    : MagickFormat.Png;
            }
            string? colorProfilePath = null;
            bool isComposite = ExportMetadataService.IsComposite(result.SourceBounds);
            byte[]? iccProfile = exportOptions.Icc == ExportIccMode.EmbedDisplay
                ? TryGetIccProfile(result.SourceBounds, out colorProfilePath)
                : null;
            if (result.IsHdr)
            {
                string stem = Path.Combine(
                    Path.GetDirectoryName(configuredPath) ?? _settings.Current.OutputFolder,
                    Path.GetFileNameWithoutExtension(configuredPath));
                var variants = HdrSavePolicy.Save(
                    stem,
                    result.Bitmap,
                    _settings.Current.HdrWriteJxr,
                    iccProfile,
                    exportOptions,
                    isComposite);
                savedPath = variants.PngPath;
                if (variants.IccUnavailableForComposite)
                    metadataNotice = "Composite capture: no single display ICC profile was embedded.";
                Log.Information(
                    "Capture.SavedHdr {Source} {Width}x{Height} Variants={Variants} ColorProfile={ColorProfile} Composite={Composite} Provenance={Provenance}",
                    result.Source, result.Bitmap.Width, result.Bitmap.Height,
                    variants.WrittenCount, colorProfilePath ?? "none", isComposite,
                    variants.ProvenancePath ?? "none");
            }
            else
            {
                savedPath = configuredPath;
                byte[] rawPng = PngIccProfileEmbedder.Encode(result.Bitmap, profile: null);
                var metadata = ExportMetadataService.Apply(
                    rawPng,
                    exportFormat,
                    exportOptions,
                    displayIccProfile: iccProfile,
                    isComposite: isComposite);
                File.WriteAllBytes(savedPath, metadata.Bytes);
                if (metadata.IccUnavailableForComposite)
                    metadataNotice = "Composite capture: no single display ICC profile was embedded.";
                string? provenancePath = ExportMetadataService.WriteProvenanceSidecar(
                    savedPath,
                    metadata.Bytes,
                    exportFormat,
                    exportOptions,
                    metadata,
                    sourcePath: null,
                    isComposite,
                    isRedacted: false,
                    result.Bitmap.Width,
                    result.Bitmap.Height);
                Log.Information("Capture.Saved {Source} {Width}x{Height} Format={Format} ColorProfile={ColorProfile} Composite={Composite} Provenance={Provenance}",
                    result.Source, result.Bitmap.Width, result.Bitmap.Height,
                    exportFormat, colorProfilePath ?? "none", isComposite,
                    provenancePath ?? "none");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Capture.Save.Failed");
            if (showUi)
            {
                MessageBox.Show($"Could not save capture:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        string? lanUrl = null;
        if (lanShare is not null && savedPath is not null)
        {
            try
            {
                lanUrl = lanShare.Register(
                    savedPath,
                    TimeSpan.FromMinutes(_settings.Current.LanShareTtlMinutes));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Capture.LanShareRegistrationFailed");
            }
        }

        bool shouldCopy = copyToClipboardOverride
            ?? (_settings.Current.CopyToClipboard || _settings.Current.QuickMode);
        if (shouldCopy)
        {
            bool copiedAsMarkdown = false;
            if (_settings.Current.ClipboardCopyMode.Equals(
                    ClipboardIntegrationService.MarkdownMode,
                    StringComparison.OrdinalIgnoreCase)
                && savedPath is not null)
            {
                var markdownResult = ClipboardIntegrationService.TryCopyCaptureAsMarkdown(
                    savedPath,
                    _settings.Current.MarkdownVaultFolder,
                    _settings.Current.MarkdownAttachmentFolder,
                    ClipboardIntegrationService.GetForegroundTarget());
                copiedAsMarkdown = markdownResult.Succeeded;
                if (!copiedAsMarkdown)
                    Log.Warning("Clipboard.MarkdownCopyFailed {Error}", markdownResult.Error ?? "unknown");
            }

            if (!copiedAsMarkdown)
            {
                try
                {
                    var bs = ToBitmapSource(result.Bitmap);
                    Clipboard.SetImage(bs);
                }
                catch { /* clipboard contention is not fatal */ }
            }
        }

        bool openEditor = openEditorOverride ?? _settings.Current.OpenEditorAfterCapture;
        if (openEditor && !_settings.Current.QuickMode)
        {
            var bs = ToBitmapSource(result.Bitmap);
            var editor = new EditorWindow(bs, savedPath, result);
            EditorTabHostWindow.Open(editor);
        }

        await Task.CompletedTask;
        return new CaptureDeliveryResult(savedPath, lanUrl, metadataNotice);
    }

    private static string ResolveExplicitOutputPath(string requested, SnaptureSettings settings)
    {
        string path = Path.GetFullPath(requested);
        if (string.IsNullOrEmpty(Path.GetExtension(path)))
        {
            path += settings.OutputFormat.Equals("JPG", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
        }
        return path;
    }

    private static byte[]? TryGetIccProfile(Rectangle bounds, out string? profilePath)
        => ExportMetadataService.TryGetDisplayIccProfile(bounds, out profilePath);

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
