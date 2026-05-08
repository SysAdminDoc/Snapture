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
    private TrayIconHost? _tray;

    public AppHost()
    {
        Native.SetProcessDPIAware();
        AppIdentity.SetAumid();
        Settings.Load();
        var (engine, name) = CaptureEngineFactory.Create(Settings.Current.CaptureEngine);
        Engine = engine;
        EngineName = name;
        Orchestrator = new CaptureOrchestrator(Settings, Engine);
    }

    public void Start()
    {
        _tray = new TrayIconHost(Orchestrator);
        Hotkeys.Initialize();

        TryRegister(Settings.Current.RegionHotkey,     () => Run(() => Orchestrator.CaptureRegionAsync()));
        TryRegister(Settings.Current.WindowHotkey,     () => Run(() => Orchestrator.CaptureForegroundWindowAsync()));
        TryRegister(Settings.Current.FullscreenHotkey, () => Run(() => Orchestrator.CaptureFullscreenAsync()));
        TryRegister(Settings.Current.LastRegionHotkey, () => Run(() => Orchestrator.CaptureLastRegionAsync()));

        _tray?.ShowToast("Snapture is running",
            $"Engine: {EngineName.ToUpperInvariant()}. PrintScreen for region · Alt+PS for window · Ctrl+PS fullscreen · Shift+PS recapture last region.");

        // First-run consent for borderless capture (Win11 22H2+).
        if (!Settings.Current.BorderlessConsentGiven)
        {
            TryRequestBorderlessConsent();
        }

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

    private void TryRegister(HotkeyBinding b, Action handler)
    {
        try { Hotkeys.Register(b.Modifiers, KeyToVk(b.KeyName), handler); }
        catch { /* hotkey already in use; tray menu still works */ }
    }

    private static uint KeyToVk(string keyName) => keyName switch
    {
        "PrintScreen" => Native.VK_SNAPSHOT,
        _ => Native.VK_SNAPSHOT
    };

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
        if (Engine is IDisposable d) d.Dispose();
        _tray?.Dispose();
    }
}
