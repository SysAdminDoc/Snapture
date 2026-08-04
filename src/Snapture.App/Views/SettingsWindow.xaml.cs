using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Snapture.App.Editor;
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

        SelectComboByTag(ThemeCombo, ThemeManager.NormalizeMode(_draft.ThemeMode));
        SelectComboByTag(EngineCombo, _draft.CaptureEngine);
        SelectComboByTag(ToneMapCombo, HdrToneMapOperators.ToKey(
            HdrToneMapOperators.Parse(_draft.HdrToneMapOperator)));
        HdrColorCorrectionCheck.IsChecked = _draft.HdrColorCorrection;
        HdrWriteJxrCheck.IsChecked = _draft.HdrWriteJxr;
        RapidOcrDirectMlCheck.IsChecked = _draft.RapidOcrUseDirectMl;
        RapidOcrStatusText.Text = $"Provider: {OcrService.RapidOcrProviderStatus}";
        BindHdrCalibrationWarning();
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
            $"Theme:     {ThemeManager.DisplayName(_draft.ThemeMode)} ({ThemeManager.EffectiveMode})\n" +
            $"WinRT:     {(WinRtCaptureEngine.IsSupported ? "supported" : "unsupported")}\n" +
            $"Monitors:  {MonitorEnumerator.Enumerate().Count}\n" +
            $"AUMID:     {AppIdentity.AppUserModelId}";

        // LAN share tab bindings
        LanEnableCheck.IsChecked = _draft.LanShareEnabled;
        LanPortBox.Text = _draft.LanSharePort.ToString();
        LanTtlBox.Text = _draft.LanShareTtlMinutes.ToString();
        LanAdapterCombo.Items.Clear();
        var adapters = LanShareServer.EnumerateAdapters();
        foreach (var (adapter, ip) in adapters)
        {
            LanAdapterCombo.Items.Add(new ComboBoxItem { Content = $"{adapter} - {ip}", Tag = ip });
        }
        // Pre-select the saved binding, else the first non-loopback IPv4.
        var savedIp = _draft.LanShareBindIp;
        bool selected = false;
        foreach (ComboBoxItem item in LanAdapterCombo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals((string?)item.Tag, savedIp, StringComparison.OrdinalIgnoreCase))
            {
                LanAdapterCombo.SelectedItem = item;
                selected = true;
                break;
            }
        }
        if (!selected && LanAdapterCombo.Items.Count > 0)
            LanAdapterCombo.SelectedIndex = 0;
        UpdateLanStatus();

        BuildRedactRulesList();

        bool hijacked = PrintScreenHijackDetector.IsHijacked();
        PrintScreenStatus.Text = hijacked
            ? "Windows is currently sending PrintScreen to the Snipping Tool. Restore the shortcut to let apps receive it again."
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
            PrintScreenStatus.Text = "PrintScreen is restored. Sign out and back in for the change to take full effect.";
            ReclaimPrintScreenButton.IsEnabled = false;
        }
        else
        {
            StatusText.Text = "Could not write the registry value.";
        }
    }

    private void BuildRedactRulesList()
    {
        RedactRulesList.Children.Clear();
        var disabled = new HashSet<string>(_draft.DisabledRedactRules, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in SecretDetector.Rules)
        {
            string id = rule.Id;
            var row = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            row.SetResourceReference(Border.BackgroundProperty, "AppSurface");
            row.SetResourceReference(Border.BorderBrushProperty, "AppBorder");

            var cb = new CheckBox
            {
                IsChecked = !disabled.Contains(id),
                Tag = id
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var idText = new TextBlock
            {
                Text = $"{id}",
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Code, Consolas, monospace"),
                VerticalAlignment = VerticalAlignment.Center
            };
            idText.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
            Grid.SetColumn(idText, 0);
            grid.Children.Add(idText);

            var descriptionText = new TextBlock
            {
                Text = rule.Description,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            descriptionText.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
            Grid.SetColumn(descriptionText, 1);
            grid.Children.Add(descriptionText);

            cb.Content = grid;
            cb.Click += (_, _) =>
            {
                var ruleId = (string)cb.Tag!;
                if (cb.IsChecked == true)
                    _draft.DisabledRedactRules.Remove(ruleId);
                else if (!_draft.DisabledRedactRules.Contains(ruleId))
                    _draft.DisabledRedactRules.Add(ruleId);
            };
            row.Child = cb;
            RedactRulesList.Children.Add(row);
        }
    }

    private void OnRedactEnableAllClicked(object sender, RoutedEventArgs e)
    {
        _draft.DisabledRedactRules.Clear();
        BuildRedactRulesList();
    }

    private void OnRedactDisableAllClicked(object sender, RoutedEventArgs e)
    {
        _draft.DisabledRedactRules = SecretDetector.Rules.Select(r => r.Id).ToList();
        BuildRedactRulesList();
    }

    private void OnLanStartClicked(object sender, RoutedEventArgs e)
    {
        SaveLanFieldsToDraft();
        if (App.Host is null) return;
        try
        {
            App.Host.Settings.Current.LanShareBindIp = _draft.LanShareBindIp;
            App.Host.Settings.Current.LanSharePort = _draft.LanSharePort;
            App.Host.LanShare.Stop();
            App.Host.LanShare.Start(_draft.LanShareBindIp, _draft.LanSharePort);
            UpdateLanStatus();
        }
        catch (Exception ex)
        {
            LanStatusText.Text = $"Failed to start: {ex.Message}";
        }
    }

    private void OnLanStopClicked(object sender, RoutedEventArgs e)
    {
        App.Host?.LanShare.Stop();
        UpdateLanStatus();
    }

    private void SaveLanFieldsToDraft()
    {
        _draft.LanShareEnabled = LanEnableCheck.IsChecked == true;
        if (LanAdapterCombo.SelectedItem is ComboBoxItem item)
            _draft.LanShareBindIp = (string)(item.Tag ?? "");
        if (int.TryParse(LanPortBox.Text, out var port) && port > 0 && port < 65536)
            _draft.LanSharePort = port;
        if (int.TryParse(LanTtlBox.Text, out var ttl) && ttl > 0)
            _draft.LanShareTtlMinutes = ttl;
    }

    private void UpdateLanStatus()
    {
        var lan = App.Host?.LanShare;
        if (lan is null || !lan.IsRunning)
        {
            LanStatusText.Text = "Server is stopped.";
            return;
        }
        LanStatusText.Text =
            $"Running on {lan.BaseUrl}\n" +
            $"Active tokens: {lan.ActiveEntries().Count} (single-fetch, expire after TTL)\n" +
            $"Test: open {lan.BaseUrl}/ in any LAN browser";
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

    private void BindHdrCalibrationWarning()
    {
        var suspicious = HdrCalibrationProbe.FindSuspiciousMonitors();
        if (suspicious.Count == 0)
        {
            HdrCalibrationPanel.Visibility = Visibility.Collapsed;
            return;
        }

        string displays = string.Join(", ", suspicious.Select(info =>
            $"{info.DeviceName} ({info.MaxLuminance:0} nits)"));
        HdrCalibrationText.Text =
            $"Windows reports a very low HDR peak on {displays}. Calibrate the display before capturing to avoid dim or clipped highlights.";
        HdrCalibrationPanel.Visibility = Visibility.Visible;
    }

    private void OnOpenHdrCalibrationClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:display",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            HdrCalibrationText.Text = $"Could not open Windows HDR settings: {ex.Message}";
        }
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
        _draft.HdrToneMapOperator     = ((ComboBoxItem)ToneMapCombo.SelectedItem).Tag as string ?? HdrToneMapOperators.DefaultKey;
        _draft.HdrColorCorrection    = HdrColorCorrectionCheck.IsChecked == true;
        _draft.HdrWriteJxr            = HdrWriteJxrCheck.IsChecked == true;
        _draft.RapidOcrUseDirectMl    = RapidOcrDirectMlCheck.IsChecked == true;
        var newTheme                  = ((ComboBoxItem)ThemeCombo.SelectedItem).Tag as string ?? ThemeManager.SystemMode;
        var newEngine                 = ((ComboBoxItem)EngineCombo.SelectedItem).Tag as string ?? "auto";

        bool engineChanged = !string.Equals(newEngine, _settings.Current.CaptureEngine, StringComparison.OrdinalIgnoreCase);
        bool themeChanged = !string.Equals(newTheme, _settings.Current.ThemeMode, StringComparison.OrdinalIgnoreCase);

        SaveLanFieldsToDraft();
        bool lanWasEnabled = _settings.Current.LanShareEnabled;

        CopyInto(_draft, _settings.Current);
        _settings.Current.CaptureEngine = newEngine;
        _settings.Current.ThemeMode = ThemeManager.NormalizeMode(newTheme);
        _settings.Save();

        if (themeChanged) ThemeManager.Apply(_settings.Current.ThemeMode);
        if (engineChanged) App.Host?.SwitchEngine(newEngine);
        OcrService.ConfigureRapidOcr(_settings.Current.RapidOcrUseDirectMl);

        // LAN share lifecycle reacts to the toggle.
        if (App.Host is not null)
        {
            if (_settings.Current.LanShareEnabled && !App.Host.LanShare.IsRunning)
                App.Host.TryStartLanShare();
            else if (!_settings.Current.LanShareEnabled && App.Host.LanShare.IsRunning)
                App.Host.LanShare.Stop();
            else if (lanWasEnabled && App.Host.LanShare.IsRunning)
            {
                // Adapter or port changed — restart.
                if (App.Host.LanShare.BindAddress != _settings.Current.LanShareBindIp ||
                    App.Host.LanShare.Port != _settings.Current.LanSharePort)
                {
                    App.Host.LanShare.Stop();
                    App.Host.TryStartLanShare();
                }
            }
        }

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
        dst.ThemeMode = ThemeManager.NormalizeMode(src.ThemeMode);
        dst.CaptureEngine = src.CaptureEngine;
        dst.HdrToneMapOperator = HdrToneMapOperators.ToKey(
            HdrToneMapOperators.Parse(src.HdrToneMapOperator));
        dst.HdrColorCorrection = src.HdrColorCorrection;
        dst.HdrWriteJxr = src.HdrWriteJxr;
        dst.RapidOcrUseDirectMl = src.RapidOcrUseDirectMl;
        dst.BorderlessConsentGiven = src.BorderlessConsentGiven;
        dst.PrintScreenHijackToastShown = src.PrintScreenHijackToastShown;
        dst.LastRegion = src.LastRegion;
        dst.LanShareEnabled = src.LanShareEnabled;
        dst.LanShareBindIp = src.LanShareBindIp;
        dst.LanSharePort = src.LanSharePort;
        dst.LanShareTtlMinutes = src.LanShareTtlMinutes;
        dst.DisabledRedactRules = new List<string>(src.DisabledRedactRules);
        dst.RegionHotkey = src.RegionHotkey;
        dst.WindowHotkey = src.WindowHotkey;
        dst.FullscreenHotkey = src.FullscreenHotkey;
        dst.LastRegionHotkey = src.LastRegionHotkey;
    }
}
