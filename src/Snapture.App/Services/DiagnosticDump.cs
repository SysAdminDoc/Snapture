using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Serilog;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Bundles last 7 days of logs + scrubbed settings + system info into a ZIP
/// that the user can attach to a GitHub issue. No images included.
/// </summary>
public static class DiagnosticDump
{
    public static string Create()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var path = Path.Combine(desktop, $"Snapture_diagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        AddSystemInfo(zip);
        AddLogs(zip);
        AddScrubSettings(zip);

        Log.Information("DiagnosticDump.Created {Path}", path);
        return path;
    }

    private static void AddSystemInfo(ZipArchive zip)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Snapture v{typeof(DiagnosticDump).Assembly.GetName().Version?.ToString(3) ?? "?"}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Processors: {Environment.ProcessorCount}");
        sb.AppendLine($"Working Set: {Environment.WorkingSet / (1024 * 1024)} MB");
        sb.AppendLine($"Engine: {App.Host?.EngineName ?? "?"}");
        sb.AppendLine($"Theme: {App.Host?.Settings.Current.ThemeMode ?? "?"}");

        sb.AppendLine();
        sb.AppendLine("Monitors:");
        try
        {
            foreach (var mon in MonitorEnumerator.Enumerate())
                sb.AppendLine($"  {mon.DeviceName}: {mon.Bounds.Width}x{mon.Bounds.Height} DPI:{mon.DpiX}x{mon.DpiY} Primary:{mon.IsPrimary}");
        }
        catch { sb.AppendLine("  (could not enumerate)"); }

        sb.AppendLine();
        sb.AppendLine("Plugins:");
        try
        {
            if (App.Host?.Plugins.All is { } plugins)
                foreach (var p in plugins)
                    sb.AppendLine($"  {p.Info.Name} v{p.Info.Version} by {p.Info.Author}");
        }
        catch { sb.AppendLine("  (could not list)"); }

        using var entry = new StreamWriter(zip.CreateEntry("system-info.txt").Open());
        entry.Write(sb.ToString());
    }

    private static void AddLogs(ZipArchive zip)
    {
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Snapture", "logs");
        if (!Directory.Exists(logDir)) return;

        foreach (var logFile in Directory.EnumerateFiles(logDir, "snapture-*.log"))
        {
            try
            {
                var name = Path.GetFileName(logFile);
                using var source = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dest = zip.CreateEntry($"logs/{name}").Open();
                source.CopyTo(dest);
            }
            catch { }
        }
    }

    private static void AddScrubSettings(ZipArchive zip)
    {
        var settingsPath = SettingsService.GetFilePath();
        if (!File.Exists(settingsPath)) return;

        try
        {
            var json = File.ReadAllText(settingsPath);
            var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            using var writer = new Utf8JsonWriter(new MemoryStream(), new JsonWriterOptions { Indented = true });

            sb.AppendLine("(Settings scrubbed — paths and IPs redacted)");
            sb.AppendLine(json
                .Replace(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "[USERPROFILE]")
                .Replace(Environment.UserName, "[USERNAME]"));

            using var entry = new StreamWriter(zip.CreateEntry("settings-scrubbed.json").Open());
            entry.Write(sb.ToString());
        }
        catch { }
    }
}
