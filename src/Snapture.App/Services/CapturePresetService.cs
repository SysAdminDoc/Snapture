using System.IO;

namespace Snapture.App.Services;

public sealed record CapturePresetDefinition(
    string Key,
    string Label,
    string Description);

public static class CapturePresetService
{
    public const string CustomKey = "custom";

    private static readonly string CaptureRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapture");

    public static IReadOnlyList<CapturePresetDefinition> Presets { get; } =
    [
        new(CustomKey, "Custom", "Keep the current capture and output settings."),
        new("bug-report", "Bug-report", "PNG captures with cursor, a border, editor review, and a process-aware filename."),
        new("code-block", "Code-block", "Clean PNG captures for code and terminals, without the cursor or automatic border."),
        new("documentation", "Documentation", "Editor-ready PNG captures in a documentation folder with a window-aware filename."),
        new("quick-share-lan", "Quick-share-LAN", "Editor-ready PNG captures with the existing opt-in LAN share server enabled.")
    ];

    public static CapturePresetDefinition? Find(string? key) =>
        Presets.FirstOrDefault(preset => string.Equals(preset.Key, key, StringComparison.OrdinalIgnoreCase));

    public static bool Apply(string key, SnaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var preset = Find(key);
        if (preset is null || preset.Key == CustomKey)
            return false;

        switch (preset.Key)
        {
            case "bug-report":
                SetCommon(settings, "BugReports", "Bug_{ProcessName}_{yyyy-MM-dd}_{HH-mm-ss}");
                settings.CopyToClipboard = true;
                settings.OpenEditorAfterCapture = true;
                settings.IncludeCursor = true;
                settings.AutoBorderOnCapture = true;
                settings.LanShareEnabled = false;
                break;

            case "code-block":
                SetCommon(settings, "Code", "Code_{ProcessName}_{yyyy-MM-dd}_{HH-mm-ss}");
                settings.CopyToClipboard = true;
                settings.OpenEditorAfterCapture = true;
                settings.IncludeCursor = false;
                settings.AutoBorderOnCapture = false;
                settings.LanShareEnabled = false;
                break;

            case "documentation":
                SetCommon(settings, "Documentation", "Doc_{WindowTitle}_{yyyy-MM-dd}_{HH-mm-ss}");
                settings.CopyToClipboard = false;
                settings.OpenEditorAfterCapture = true;
                settings.IncludeCursor = false;
                settings.AutoBorderOnCapture = true;
                settings.LanShareEnabled = false;
                break;

            case "quick-share-lan":
                SetCommon(settings, "Shared", "Share_{ProcessName}_{yyyy-MM-dd}_{HH-mm-ss}");
                settings.CopyToClipboard = true;
                settings.OpenEditorAfterCapture = true;
                settings.IncludeCursor = true;
                settings.AutoBorderOnCapture = false;
                settings.LanShareEnabled = true;
                break;

            default:
                return false;
        }

        settings.ActiveCapturePreset = preset.Key;
        return true;
    }

    private static void SetCommon(SnaptureSettings settings, string folder, string filenamePattern)
    {
        settings.OutputFolder = Path.Combine(CaptureRoot, folder);
        settings.FilenamePattern = filenamePattern;
        settings.OutputFormat = "PNG";
        settings.QuickMode = false;
        settings.ShowToastOnSave = true;
    }
}
