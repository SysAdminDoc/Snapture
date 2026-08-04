using System.Windows;
using Serilog;
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
    public LanShareServer LanShare { get; } = new();
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
        var scratch = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Snapture", "plugin-scratch");
        PluginHost = new PluginHostBridge(scratch,
            toast: (t, m) => _tray?.ShowToast(t, m),
            log: msg => System.Diagnostics.Debug.WriteLine($"[plugin] {msg}"));
        Plugins = new PluginLoader(PluginHost);
        Orchestrator = new CaptureOrchestrator(Settings, Engine, History);
    }

    public void Start()
    {
        _tray = new TrayIconHost(Orchestrator);
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
        History.Dispose();
        LanShare.Dispose();
        Plugins.Dispose();
        if (Engine is IDisposable d) d.Dispose();
        _tray?.Dispose();
    }
}
