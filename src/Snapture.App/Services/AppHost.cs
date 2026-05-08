using System.Windows;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class AppHost : IDisposable
{
    public SettingsService Settings { get; } = new();
    public ICaptureEngine Engine { get; }
    public CaptureOrchestrator Orchestrator { get; }
    public HotkeyService Hotkeys { get; } = new();
    private TrayIconHost? _tray;

    public AppHost()
    {
        Native.SetProcessDPIAware();
        Settings.Load();
        Engine = new GdiCaptureEngine();
        Orchestrator = new CaptureOrchestrator(Settings, Engine);
    }

    public void Start()
    {
        _tray = new TrayIconHost(Orchestrator);
        Hotkeys.Initialize();

        // Wire hotkeys (best effort — collisions with other apps are non-fatal)
        TryRegister(Settings.Current.RegionHotkey,    () => Run(() => Orchestrator.CaptureRegionAsync()));
        TryRegister(Settings.Current.WindowHotkey,    () => Run(() => Orchestrator.CaptureForegroundWindowAsync()));
        TryRegister(Settings.Current.FullscreenHotkey,() => Run(() => Orchestrator.CaptureFullscreenAsync()));

        _tray?.ShowToast("Snapture is running",
            "PrintScreen for region · Alt+PrintScreen for window · Ctrl+PrintScreen for fullscreen.");
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
        _tray?.Dispose();
    }
}
