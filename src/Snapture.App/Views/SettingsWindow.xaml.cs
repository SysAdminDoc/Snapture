using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly SnaptureSettings _draft;

    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        _draft = Clone(settings.Current);
        Bind();
    }

    private static SnaptureSettings Clone(SnaptureSettings s)
    {
        var json = JsonSerializer.Serialize(s);
        return JsonSerializer.Deserialize<SnaptureSettings>(json) ?? new SnaptureSettings();
    }

    private void Bind()
    {
        LaunchAtStartupCheck.IsChecked = _draft.LaunchAtStartup;
        OpenEditorCheck.IsChecked      = _draft.OpenEditorAfterCapture;
        CopyClipboardCheck.IsChecked   = _draft.CopyToClipboard;
        ShowToastCheck.IsChecked       = _draft.ShowToastOnSave;

        SelectComboByTag(EngineCombo, _draft.CaptureEngine);
        SelectComboByTag(FormatCombo, _draft.OutputFormat);

        EngineCapsText.Text = WinRtCaptureEngine.IsSupported
            ? "WinRT capture is available on this system."
            : "WinRT is unavailable on this system; the auto setting will fall back to GDI.";

        OutputFolderBox.Text = _draft.OutputFolder;
        FilenameTemplateBox.Text = _draft.FilenamePattern;

        RegionHotkeyBox.Text       = HotkeyToString(_draft.RegionHotkey);
        WindowHotkeyBox.Text       = HotkeyToString(_draft.WindowHotkey);
        FullscreenHotkeyBox.Text   = HotkeyToString(_draft.FullscreenHotkey);
        LastRegionHotkeyBox.Text   = HotkeyToString(_draft.LastRegionHotkey);

        SettingsPathText.Text = SettingsService.GetFilePath();
        DiagnosticsText.Text =
            $"OS:        {Environment.OSVersion}\n" +
            $".NET:      {Environment.Version}\n" +
            $"Engine:    {App.Host?.EngineName ?? "n/a"} (active)\n" +
            $"WinRT:     {(WinRtCaptureEngine.IsSupported ? "supported" : "unsupported")}\n" +
            $"Monitors:  {MonitorEnumerator.Enumerate().Count}\n" +
            $"AUMID:     {AppIdentity.AppUserModelId}";

        bool hijacked = PrintScreenHijackDetector.IsHijacked();
        PrintScreenStatus.Text = hijacked
            ? "Windows is currently sending PrintScreen to the Snipping Tool. Click Reclaim to send it back to apps."
            : "PrintScreen is not hijacked.";
        ReclaimPrintScreenButton.IsEnabled = hijacked;
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals((string?)item.Tag, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string HotkeyToString(HotkeyBinding b)
    {
        var parts = new List<string>();
        if ((b.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((b.Modifiers & 1) != 0) parts.Add("Alt");
        if ((b.Modifiers & 4) != 0) parts.Add("Shift");
        if ((b.Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(b.KeyName);
        return string.Join("+", parts);
    }

    private void OnHotkeyPreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftAlt  || key == Key.RightAlt  ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LWin || key == Key.RWin)
        {
            return; // wait for the non-modifier
        }
        uint mods = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     mods |= 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   mods |= 4;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;

        var box = (TextBox)sender;
        var keyName = key.ToString();
        var binding = new HotkeyBinding(mods, keyName);
        box.Text = HotkeyToString(binding);
        switch ((string)box.Tag!)
        {
            case "region":     _draft.RegionHotkey = binding; break;
            case "window":     _draft.WindowHotkey = binding; break;
            case "fullscreen": _draft.FullscreenHotkey = binding; break;
            case "lastRegion": _draft.LastRegionHotkey = binding; break;
        }
    }

    private void OnBrowseFolderClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(OutputFolderBox.Text) ? OutputFolderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Title = "Pick a folder for new captures"
        };
        if (dlg.ShowDialog(this) == true)
            OutputFolderBox.Text = dlg.FolderName;
    }

    private void OnRequestBorderlessClicked(object sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            StatusText.Text = "Borderless capture access requires Win11 22H2+.";
            return;
        }
        _ = RequestBorderlessAsync();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.22621.0")]
    private async Task RequestBorderlessAsync()
    {
        bool ok = await BorderlessConsent.RequestAsync();
        StatusText.Text = ok ? "Borderless access granted." : "Borderless access denied or unavailable.";
        _draft.BorderlessConsentGiven = ok;
    }

    private void OnReclaimPrintScreenClicked(object sender, RoutedEventArgs e)
    {
        if (PrintScreenHijackDetector.Reclaim())
        {
            PrintScreenStatus.Text = "Reclaimed. Sign out + back in for the change to take full effect.";
            ReclaimPrintScreenButton.IsEnabled = false;
        }
        else
        {
            StatusText.Text = "Could not write the registry value.";
        }
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Snapture settings (*.json)|*.json|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var json = File.ReadAllText(dlg.FileName);
            var imported = JsonSerializer.Deserialize<SnaptureSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (imported is null) { StatusText.Text = "Could not parse settings."; return; }
            CopyInto(imported, _draft);
            Bind();
            StatusText.Text = $"Imported settings from {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Import failed: {ex.Message}";
        }
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Snapture settings (*.json)|*.json",
            FileName = $"snapture-settings-{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_draft,
                new JsonSerializerOptions { WriteIndented = true }));
            StatusText.Text = $"Exported to {Path.GetFileName(dlg.FileName)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Export failed: {ex.Message}";
        }
    }

    private void OnRevealClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SettingsService.GetFilePath()}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        // Pull edited fields from controls
        _draft.LaunchAtStartup        = LaunchAtStartupCheck.IsChecked == true;
        _draft.OpenEditorAfterCapture = OpenEditorCheck.IsChecked == true;
        _draft.CopyToClipboard        = CopyClipboardCheck.IsChecked == true;
        _draft.ShowToastOnSave        = ShowToastCheck.IsChecked == true;
        _draft.OutputFolder           = OutputFolderBox.Text;
        _draft.FilenamePattern        = FilenameTemplateBox.Text;
        _draft.OutputFormat           = ((ComboBoxItem)FormatCombo.SelectedItem).Tag as string ?? "PNG";
        var newEngine                 = ((ComboBoxItem)EngineCombo.SelectedItem).Tag as string ?? "auto";

        bool engineChanged = !string.Equals(newEngine, _settings.Current.CaptureEngine, StringComparison.OrdinalIgnoreCase);

        CopyInto(_draft, _settings.Current);
        _settings.Current.CaptureEngine = newEngine;
        _settings.Save();

        if (engineChanged) App.Host?.SwitchEngine(newEngine);
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void CopyInto(SnaptureSettings src, SnaptureSettings dst)
    {
        dst.OutputFolder = src.OutputFolder;
        dst.FilenamePattern = src.FilenamePattern;
        dst.OutputFormat = src.OutputFormat;
        dst.CopyToClipboard = src.CopyToClipboard;
        dst.OpenEditorAfterCapture = src.OpenEditorAfterCapture;
        dst.ShowToastOnSave = src.ShowToastOnSave;
        dst.LaunchAtStartup = src.LaunchAtStartup;
        dst.CaptureEngine = src.CaptureEngine;
        dst.BorderlessConsentGiven = src.BorderlessConsentGiven;
        dst.PrintScreenHijackToastShown = src.PrintScreenHijackToastShown;
        dst.LastRegion = src.LastRegion;
        dst.RegionHotkey = src.RegionHotkey;
        dst.WindowHotkey = src.WindowHotkey;
        dst.FullscreenHotkey = src.FullscreenHotkey;
        dst.LastRegionHotkey = src.LastRegionHotkey;
    }
}
