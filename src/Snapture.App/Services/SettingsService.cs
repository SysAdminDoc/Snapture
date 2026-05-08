using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapture.App.Services;

public sealed class SnaptureSettings
{
    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapture");

    public string FilenamePattern { get; set; } = "Snapture_{yyyy-MM-dd}_{HH-mm-ss}";

    public string OutputFormat { get; set; } = "PNG"; // PNG, JPG

    public bool CopyToClipboard { get; set; } = true;

    public bool OpenEditorAfterCapture { get; set; } = true;

    public bool ShowToastOnSave { get; set; } = true;

    public bool LaunchAtStartup { get; set; } = false;

    public HotkeyBinding RegionHotkey { get; set; } = new(0, "PrintScreen");
    public HotkeyBinding WindowHotkey { get; set; } = new(1 /*Alt*/, "PrintScreen");
    public HotkeyBinding FullscreenHotkey { get; set; } = new(2 /*Ctrl*/, "PrintScreen");
}

public sealed record HotkeyBinding(uint Modifiers, string KeyName);

public sealed class SettingsService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapture");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

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
