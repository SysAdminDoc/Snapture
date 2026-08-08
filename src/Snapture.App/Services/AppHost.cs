using System.IO;
using System.Windows;
using Serilog;
using SkiaSharp;
using Snapture.App.Editor;
using Snapture.App.Views;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class AppHost : IDisposable
{
    public SettingsService Settings { get; } = new();
    public ICaptureEngine Engine { get; private set; }
    public string EngineName { get; private set; }
    public CaptureOrchestrator Orchestrator { get; }
    public HotkeyService Hotkeys { get; } = new();
    public CaptureHistoryService History { get; }
    public WatchFolderService WatchFolder { get; }
    public LanShareServer LanShare { get; } = new();
    public McpServer Mcp { get; }
    public PluginLoader Plugins { get; }
    public PluginHostBridge PluginHost { get; }
    private TrayIconHost? _tray;

    public AppHost()
    {
        Native.SetProcessDPIAware();
        AppIdentity.SetAumid();
        Settings.Load();
        OcrService.ConfigureRapidOcr(Settings.Current.RapidOcrUseDirectMl);
        OcrService.ConfigureOneOcr(Settings.Current.OneOcrExecutablePath);
        ThemeManager.Initialize(Settings.Current.ThemeMode);
        var (engine, name) = CaptureEngineFactory.Create(Settings.Current.CaptureEngine);
        Engine = engine;
        EngineName = name;
        ApplyEngineSettings(engine);
        Log.Information("Engine.Initialized {EngineName}", name);
        History = new CaptureHistoryService();
        WatchFolder = new WatchFolderService(ImportWatchedImageAsync);
        var scratch = System.IO.Path.Combine(PortableMode.LocalDataDirectory, "plugin-scratch");
        PluginHost = new PluginHostBridge(scratch,
            toast: (t, m) => _tray?.ShowToast(t, m),
            log: msg => System.Diagnostics.Debug.WriteLine($"[plugin] {msg}"));
        Plugins = new PluginLoader(
            PluginHost,
            manifest => PluginCapabilityPolicy.IsApproved(Settings.Current, manifest),
            isArtifactTrusted: manifest => PluginArtifactTrustPolicy.IsApproved(Settings.Current, manifest));
        Orchestrator = new CaptureOrchestrator(Settings, Engine, History);
        Mcp = new McpServer(
            Settings,
            () => Engine,
            Orchestrator,
            History,
            tool => _tray?.ShowToast("MCP tool invoked", tool));
    }

    public void Start()
    {
        _tray = new TrayIconHost(Orchestrator);
        ApplyWatchFolderSettings();
        JumpListService.Apply();
        Hotkeys.Initialize();
        RewireHotkeys();

        _tray?.ShowToast("Snapture is running",
            $"Engine: {EngineName.ToUpperInvariant()}. PrintScreen captures a region; Alt+PrintScreen captures a window.");

        // First-run consent for borderless capture (Win11 22H2+).
        if (!Settings.Current.BorderlessConsentGiven)
        {
            TryRequestBorderlessConsent();
        }

        if (!Settings.Current.WinRtUpgradeToastShown && EngineName == CaptureEngineFactory.WinRt)
        {
            _tray?.ShowToast("Capture engine upgraded",
                "Snapture is using the modern WinRT engine. You can switch engines in Settings > Capture > Engine.");
            Settings.Current.WinRtUpgradeToastShown = true;
            Settings.Save();
        }

        // LAN share auto-start if user previously enabled it.
        if (Settings.Current.LanShareEnabled)
            TryStartLanShare();

        // MCP auto-start is opt-in and always binds to loopback.
        if (Settings.Current.McpEnabled)
            TryStartMcp();

        // Run history retention cleanup
        if (Settings.Current.HistoryRetentionDays > 0)
        {
            try
            {
                int purged = History.PurgeOlderThan(Settings.Current.HistoryRetentionDays);
                if (purged > 0) Log.Information("History.Purged {Count} entries older than {Days}d", purged, Settings.Current.HistoryRetentionDays);
            }
            catch (Exception ex) { Log.Warning(ex, "History.Purge.Failed"); }
        }

        // Discover and load plugins. Each lives in its own collectible context.
        try { Plugins.LoadAll(); }
        catch (Exception ex) { Log.Warning(ex, "Plugin.LoadAll.Failed"); }

        // Clean orphan files from previous crash
        try
        {
            int orphans = OrphanFileDetector.Sweep();
            if (orphans > 0) Log.Information("OrphanDetector.Cleaned {Count} orphan(s)", orphans);
        }
        catch (Exception ex) { Log.Warning(ex, "OrphanDetector.Failed"); }

        // Autosave recovery — check for leftover drafts from a crash.
        CheckForAutosaveRecovery();

        // Pre-warm UIA COM proxy so Smart Element Capture's first attach is snappy.
        Task.Run(() =>
        {
            try
            {
                _ = System.Windows.Automation.AutomationElement.RootElement;
                Log.Debug("UIA.Prewarmed");
            }
            catch (Exception ex) { Log.Debug(ex, "UIA.Prewarm.Failed"); }
        });

        // PrintScreen hijack detection — quietly check, toast once.
        if (!Settings.Current.PrintScreenHijackToastShown && PrintScreenHijackDetector.IsHijacked())
        {
            _tray?.ShowToast("PrintScreen is hijacked",
                "Windows is sending PrintScreen to the Snipping Tool. Use the tray menu's Restore PrintScreen shortcut action.");
            Settings.Current.PrintScreenHijackToastShown = true;
            Settings.Save();
        }
    }

    public async Task<int> RunCliAsync(CliCommand command)
    {
        if (command.Kind == CliCommandKind.Help)
        {
            Console.WriteLine(CliCommandLine.Usage);
            return 0;
        }

        if (command.Kind == CliCommandKind.Version)
        {
            Console.WriteLine(typeof(AppHost).Assembly.GetName().Version?.ToString(3) ?? "unknown");
            return 0;
        }

        var options = command.Capture;
        if (options is null)
        {
            Console.Error.WriteLine("No capture options were supplied.");
            return 2;
        }

        if (options.Profile is not null
            && !CapturePresetService.Apply(options.Profile, Settings.Current))
        {
            Console.Error.WriteLine($"Unknown capture profile: {options.Profile}");
            return 2;
        }
        ApplyEngineSettings();

        CaptureResult result;
        ICaptureEngine? cliEngine = null;
        try
        {
            var captureEngine = Engine;
            if (options.Engine is not null)
            {
                var selected = CaptureEngineFactory.Create(options.Engine);
                cliEngine = selected.Engine;
                captureEngine = cliEngine;
                ApplyEngineSettings(captureEngine);
            }

            using (Settings.Current.HideDesktopIcons
                ? DesktopIconVisibilityService.TryHide()
                : null)
            {
                result = options.Fullscreen
                    ? await captureEngine.CaptureVirtualScreenAsync().ConfigureAwait(true)
                    : await captureEngine.CaptureRegionAsync(options.Region!.Value).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cli.CaptureFailed");
            Console.Error.WriteLine($"Capture failed: {ex.Message}");
            return 1;
        }
        finally
        {
            if (cliEngine is IDisposable disposable)
                disposable.Dispose();
        }

        try
        {
            if (options.LanShare && !TryStartLanShare())
            {
                Console.Error.WriteLine("LAN share could not start on a usable local adapter.");
                return 1;
            }

            var delivery = await Orchestrator.DeliverCaptureForCliAsync(
                result,
                options.OutputPath,
                options.CopyToClipboard ? true : null,
                options.LanShare ? LanShare : null).ConfigureAwait(true);

            if (delivery.SavedPath is not null)
                Console.WriteLine($"Saved: {delivery.SavedPath}");
            if (delivery.LanUrl is not null)
                Console.WriteLine($"LAN URL: {delivery.LanUrl}");

            if (options.Hold)
            {
                if (options.BlockSeconds > 0)
                {
                    Console.WriteLine($"Holding for {options.BlockSeconds} second(s).");
                    await Task.Delay(TimeSpan.FromSeconds(options.BlockSeconds)).ConfigureAwait(true);
                }
                else
                {
                    Console.WriteLine("Holding until the CLI process is terminated.");
                    await Task.Delay(Timeout.InfiniteTimeSpan).ConfigureAwait(true);
                }
            }
            return delivery.SavedPath is null ? 1 : 0;
        }
        finally
        {
            result.Bitmap.Dispose();
        }
    }

    public void ApplyWatchFolderSettings()
    {
        WatchFolder.Stop();
        if (!Settings.Current.WatchFolderEnabled || string.IsNullOrWhiteSpace(Settings.Current.WatchFolderPath))
            return;

        try
        {
            WatchFolder.Start(Settings.Current.WatchFolderPath);
            _tray?.ShowToast("Watch folder enabled", $"Images dropped into {WatchFolder.FolderPath} are added to history.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WatchFolder.StartFailed");
            _tray?.ShowToast("Watch folder unavailable", ex.Message);
        }
    }

    private async Task ImportWatchedImageAsync(string path)
    {
        await Task.Yield();
        using var input = SafeImageInput.Open(path);
        using var bitmap = SKBitmap.Decode(input.Stream)
            ?? throw new InvalidDataException("The watched file is not a supported image.");
        History.Add(path, "Watch folder", null, null, bitmap.Width, bitmap.Height, null);
        _tray?.ShowToast("Watch folder import", Path.GetFileName(path));
    }

    public void OpenEditor(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        string path = Path.GetFullPath(imagePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("The image file does not exist.", path);
        if (!path.EndsWith(SnapFileFormat.Extension, StringComparison.OrdinalIgnoreCase))
            SafeImageInput.ValidateFile(path);

        EditorTabHostWindow.Open(new EditorWindow(path));
    }

    public Task RunUriAsync(CaptureUriRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Orchestrator.CaptureUriAsync(request);
    }

    private void CheckForAutosaveRecovery()
    {
        try
        {
            var pending = AutosaveService.GetPendingAutosaves();
            if (pending.Count == 0) return;

            Log.Information("Autosave.Found {Count} recovery file(s)", pending.Count);

            var result = MessageBox.Show(
                $"Snapture found {pending.Count} unsaved editing session(s) from a previous run.\n\n" +
                "Reopen them now?",
                "Snapture - Recover unsaved work",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var path in pending)
                {
                    var doc = AutosaveService.TryLoadAutosave(path);
                    if (doc is not null)
                    {
                        var editor = new EditorWindow(doc, path);
                        EditorTabHostWindow.Open(editor);
                        Log.Information("Autosave.Recovered {Path}", path);
                    }
                    else
                    {
                        // Corrupt file — discard it
                        AutosaveService.Discard(path);
                        Log.Warning("Autosave.DiscardedCorrupt {Path}", path);
                    }
                }
            }
            else
            {
                // User declined recovery — clean up the autosave files.
                foreach (var path in pending)
                    AutosaveService.Discard(path);
                Log.Information("Autosave.DeclinedRecovery — discarded {Count} file(s)", pending.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autosave.RecoveryCheckFailed");
        }
    }

    private void TryRequestBorderlessConsent()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        _ = RequestBorderlessConsentAsync();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.22621.0")]
    private async Task RequestBorderlessConsentAsync()
    {
        bool ok = await BorderlessConsent.RequestAsync();
        Settings.Current.BorderlessConsentGiven = ok;
        Settings.Save();
    }

    public bool TryStartLanShare()
    {
        try
        {
            string ip = Settings.Current.LanShareBindIp;
            if (string.IsNullOrWhiteSpace(ip))
            {
                var first = LanShareServer.EnumerateAdapters().FirstOrDefault();
                if (first.Ip is null) return false;
                ip = first.Ip;
                Settings.Current.LanShareBindIp = ip;
                Settings.Save();
            }
            LanShare.Start(ip, Settings.Current.LanSharePort);
            return LanShare.IsRunning;
        }
        catch
        {
            return false;
        }
    }

    public void StopLanShare() => LanShare.Stop();

    public bool TryStartMcp()
    {
        try
        {
            Mcp.Start(Settings.Current.McpPort);
            return Mcp.IsRunning;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mcp.Start.Failed {Port}", Settings.Current.McpPort);
            return false;
        }
    }

    public void StopMcp() => Mcp.Stop();

    public void SwitchEngine(string name)
    {
        var (engine, actual) = CaptureEngineFactory.Create(name);
        if (Engine is IDisposable d) d.Dispose();
        Engine = engine;
        EngineName = actual;
        ApplyEngineSettings(engine);
        Orchestrator.ReplaceEngine(engine);
        Settings.Current.CaptureEngine = actual;
        Settings.Save();
        Log.Information("Engine.Switched {EngineName}", actual);
    }

    public void ApplyEngineSettings() => ApplyEngineSettings(Engine);

    private void ApplyEngineSettings(ICaptureEngine engine)
    {
        if (engine is WinRtCaptureEngine wrt)
        {
            wrt.IncludeSecondaryWindows = Settings.Current.IncludeSecondaryWindows;
            wrt.IncludeCursor = Settings.Current.IncludeCursor;
            wrt.ToneMapOperator = HdrToneMapOperators.Parse(Settings.Current.HdrToneMapOperator);
            wrt.HdrColorCorrection = Settings.Current.HdrColorCorrection;
        }
    }

    public void RewireHotkeys()
    {
        Hotkeys.UnregisterAll();
        TryRegister(Settings.Current.RegionHotkey,     () => Run(() => Orchestrator.CaptureRegionAsync()));
        TryRegister(Settings.Current.WindowHotkey,     () => Run(() => Orchestrator.CaptureForegroundWindowAsync()));
        TryRegister(Settings.Current.FullscreenHotkey, () => Run(() => Orchestrator.CaptureFullscreenAsync()));
        TryRegister(Settings.Current.LastRegionHotkey, () => Run(() => Orchestrator.CaptureLastRegionAsync()));
        TryRegister(new HotkeyBinding(Native.MOD_CONTROL | Native.MOD_ALT, "V"),
            () => Run(PinLatestCaptureAsMarkdownAsync));
    }

    public Task PinLatestCaptureAsMarkdownAsync()
    {
        var latest = History.Recent(1).FirstOrDefault();
        if (latest is null)
        {
            _tray?.ShowToast("Markdown pin", "No saved capture is available yet.");
            return Task.CompletedTask;
        }

        var result = ClipboardIntegrationService.TryCopyCaptureAsMarkdown(
            latest.FilePath,
            Settings.Current.MarkdownVaultFolder,
            Settings.Current.MarkdownAttachmentFolder,
            ClipboardIntegrationService.GetForegroundTarget());
        if (result.Succeeded)
        {
            _tray?.ShowToast(
                "Markdown pin copied",
                $"{result.Markdown}\n{Path.GetFileName(result.DestinationPath)}");
        }
        else
        {
            _tray?.ShowToast("Markdown pin unavailable", result.Error ?? "Choose a vault folder in Settings > Output.");
        }

        return Task.CompletedTask;
    }

    private void TryRegister(HotkeyBinding b, Action handler)
    {
        try { Hotkeys.Register(b.Modifiers, KeyToVk(b.KeyName), handler); }
        catch (Exception ex) { Log.Warning(ex, "Hotkey.Register.Failed {Key}", b.KeyName); }
    }

    private static uint KeyToVk(string keyName) => Native.NameToVirtualKey(keyName);

    private static void Run(Func<Task> action)
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            try { await action(); }
            catch (CaptureExcludedException ex)
            {
                MessageBox.Show(ex.Message, "Snapture", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Capture failed:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    public void Dispose()
    {
        Hotkeys.Dispose();
        Mcp.Dispose();
        History.Dispose();
        LanShare.Dispose();
        Plugins.Dispose();
        WatchFolder.Dispose();
        if (Engine is IDisposable d) d.Dispose();
        _tray?.Dispose();
    }
}
