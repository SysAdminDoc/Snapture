using System.Windows;
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
        ThemeManager.Initialize(Settings.Current.ThemeMode);
        var (engine, name) = CaptureEngineFactory.Create(Settings.Current.CaptureEngine);
        Engine = engine;
        EngineName = name;
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
            $"Engine: {EngineName.ToUpperInvariant()}. PrintScreen for region · Alt+PS for window · Ctrl+PS fullscreen · Shift+PS recapture last region.");

        // First-run consent for borderless capture (Win11 22H2+).
        if (!Settings.Current.BorderlessConsentGiven)
        {
            TryRequestBorderlessConsent();
        }

        // LAN share auto-start if user previously enabled it.
        if (Settings.Current.LanShareEnabled)
            TryStartLanShare();

        // Discover and load plugins. Each lives in its own collectible context.
        try { Plugins.LoadAll(); } catch { /* loader failures are user-facing logs only */ }

        // PrintScreen hijack detection — quietly check, toast once.
        if (!Settings.Current.PrintScreenHijackToastShown && PrintScreenHijackDetector.IsHijacked())
        {
            _tray?.ShowToast("PrintScreen is hijacked",
                "Windows is sending PrintScreen to the Snipping Tool. Right-click the Snapture tray → Reclaim PrintScreen.");
            Settings.Current.PrintScreenHijackToastShown = true;
            Settings.Save();
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
        // Replace the engine on the orchestrator. The old engine, if disposable, is dropped.
        if (Engine is IDisposable d) d.Dispose();
        Engine = engine;
        EngineName = actual;
        Orchestrator.ReplaceEngine(engine);
        Settings.Current.CaptureEngine = actual;
        Settings.Save();
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
        catch { /* hotkey already in use; tray menu still works */ }
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
