using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed class SnaptureSettings
{
    public int SchemaVersion { get; set; } = 1;

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapture");

    public string FilenamePattern { get; set; } = "Snapture_{yyyy-MM-dd}_{HH-mm-ss}";

    public string OutputFormat { get; set; } = "PNG"; // PNG, JPG, BMP, WEBP

    public bool CopyToClipboard { get; set; } = true;

    public bool OpenEditorAfterCapture { get; set; } = true;

    public bool ShowToastOnSave { get; set; } = true;

    public bool LaunchAtStartup { get; set; } = false;

    /// <summary>"system" | "light" | "dark".</summary>
    public string ThemeMode { get; set; } = ThemeManager.SystemMode;

    /// <summary>"auto" | "winrt" | "gdi". <c>auto</c> picks WinRT on Win10 1809+, GDI otherwise.</summary>
    public string CaptureEngine { get; set; } = "auto";

    /// <summary>Records whether the user has accepted the WGC borderless-capture access prompt.</summary>
    public bool BorderlessConsentGiven { get; set; } = false;

    /// <summary>Set after the user has been notified about Win11 24H2 PrintScreen hijack (one-shot toast).</summary>
    public bool PrintScreenHijackToastShown { get; set; } = false;

    /// <summary>Set after the user sees the one-shot "engine upgraded to WinRT" toast.</summary>
    public bool WinRtUpgradeToastShown { get; set; } = false;

    /// <summary>Stores the last region capture for Shift+PrintScreen recapture.</summary>
    public CaptureRect? LastRegion { get; set; }

    /// <summary>LAN share server: opt-in only. Off by default.</summary>
    public bool LanShareEnabled { get; set; } = false;
    /// <summary>Adapter IP to bind. Empty = pick the first non-loopback IPv4.</summary>
    public string LanShareBindIp { get; set; } = "";
    public int LanSharePort { get; set; } = 9087;
    /// <summary>Default TTL for shared files in minutes.</summary>
    public int LanShareTtlMinutes { get; set; } = 15;

    /// <summary>
    /// Auto-redact: rule IDs that are disabled. Empty list = all rules enabled (the default).
    /// We persist disabled rather than enabled so a future rule pack expansion ships enabled
    /// for existing users without forcing a settings migration.
    /// </summary>
    public List<string> DisabledRedactRules { get; set; } = new()
    {
        "phi-mrn", "phi-npi", "phi-dea", "phi-dicom-uid", "phi-dob-marker", "phi-patient-marker"
    };

    /// <summary>Quick mode: copy to clipboard, skip editor entirely (ksnip #968).</summary>
    public bool QuickMode { get; set; } = false;

    /// <summary>History retention in days. 0 = unlimited.</summary>
    public int HistoryRetentionDays { get; set; } = 0;

    /// <summary>Include popups, menus, dropdowns in window-mode capture (Win11 22H2+).</summary>
    public bool IncludeSecondaryWindows { get; set; } = false;

    /// <summary>Include cursor in screenshot captures.</summary>
    public bool IncludeCursor { get; set; } = true;

    /// <summary>HDR FP16-to-SDR operator: "reinhard" (default), "aces", or "hable".</summary>
    public string HdrToneMapOperator { get; set; } = HdrToneMapOperators.DefaultKey;

    /// <summary>Apply HDR screenshot color correction before the BGRA8 export boundary.</summary>
    public bool HdrColorCorrection { get; set; } = true;

    /// <summary>Also write an optional SDR-clamped JXR copy for Game Bar compatibility.</summary>
    public bool HdrWriteJxr { get; set; } = false;

    /// <summary>Play a shutter sound on capture.</summary>
    public bool PlayShutterSound { get; set; } = false;

    /// <summary>Draw a thin border around captured images (Greenshot #696, Flameshot #690).</summary>
    public bool AutoBorderOnCapture { get; set; } = false;

    /// <summary>Auto-border color (ARGB).</summary>
    public uint AutoBorderColor { get; set; } = 0xFF888888;

    /// <summary>User-saved color swatches (ARGB uint values).</summary>
    public List<uint> SavedColorSwatches { get; set; } = new();

    /// <summary>Recording quality preset. Maps to bitrate and FPS.</summary>
    public string RecordingQuality { get; set; } = RecordingPresets.DefaultQuality;

    /// <summary>Recording output resolution. "native" keeps the capture source size.</summary>
    public string RecordingResolution { get; set; } = RecordingPresets.NativeResolution;

    /// <summary>Crop stable edge-mounted tabs, docks, and taskbars from new recordings when UIA can prove a safe crop.</summary>
    public bool RecordingAutoTighten { get; set; } = false;

    public HotkeyBinding RegionHotkey { get; set; } = new(0, "PrintScreen");
    public HotkeyBinding WindowHotkey { get; set; } = new(1 /*Alt*/, "PrintScreen");
    public HotkeyBinding FullscreenHotkey { get; set; } = new(2 /*Ctrl*/, "PrintScreen");
    public HotkeyBinding LastRegionHotkey { get; set; } = new(4 /*Shift*/, "PrintScreen");
}

public sealed record CaptureRect(int X, int Y, int Width, int Height);

public sealed record HotkeyBinding(uint Modifiers, string KeyName);

public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapture");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    public static string GetFilePath() => FilePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SnaptureSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) { Save(); return; }
            var json = File.ReadAllText(FilePath);
            Current = JsonSerializer.Deserialize<SnaptureSettings>(json, JsonOpts) ?? new SnaptureSettings();
        }
        catch { Current = new SnaptureSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(Current, JsonOpts);
            File.WriteAllText(FilePath, json);
        }
        catch { /* non-fatal */ }
    }
}
