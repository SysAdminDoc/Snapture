using System.IO;

namespace Snapture.App.Services;

/// <summary>
/// Resolves Snapture's user-data root. A portable copy is enabled explicitly with
/// <c>--portable</c> or by placing a <c>Snapture.ini</c> marker next to the executable.
/// The marker is deliberately tiny; the existing JSON settings contract remains the
/// authoritative settings format inside the portable data directory.
/// </summary>
public static class PortableMode
{
    public const string Flag = "--portable";
    public const string IniFileName = "Snapture.ini";

    private static readonly string InstalledLocalDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snapture");
    private static readonly string InstalledSettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Snapture");

    public static bool IsEnabled { get; private set; }

    public static string ExecutableDirectory =>
        Path.GetFullPath(AppContext.BaseDirectory);

    public static string IniPath =>
        Path.Combine(ExecutableDirectory, IniFileName);

    /// <summary>Local data root for history, plugins, logs, autosave, and crash artifacts.</summary>
    public static string LocalDataDirectory => IsEnabled
        ? Path.Combine(ExecutableDirectory, "SnaptureData")
        : InstalledLocalDataDirectory;

    /// <summary>Settings root. Portable settings stay beside the executable in SnaptureData.</summary>
    public static string SettingsDirectory => IsEnabled
        ? Path.Combine(ExecutableDirectory, "SnaptureData")
        : InstalledSettingsDirectory;

    public static string DefaultOutputDirectory => IsEnabled
        ? Path.Combine(LocalDataDirectory, "captures")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapture");

    /// <summary>Apply the process startup modifier before any settings/data service is constructed.</summary>
    public static void Initialize(IReadOnlyList<string>? args)
    {
        bool requested = args?.Any(arg => arg.Equals(Flag, StringComparison.OrdinalIgnoreCase)) == true;
        IsEnabled = requested || IsPortableIni(IniPath);
    }

    /// <summary>
    /// Recognize the portable marker without requiring a full INI parser. An empty or sectionless
    /// marker is accepted for hand-created portable bundles; an explicit Portable=false disables
    /// auto-detection unless the command-line flag is present.
    /// </summary>
    internal static bool IsPortableIni(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            bool inSnaptureSection = false;
            bool sawPortableValue = false;
            foreach (var rawLine in File.ReadLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inSnaptureSection = string.Equals(
                        line[1..^1].Trim(), "Snapture", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                int separator = line.IndexOf('=');
                if (separator < 1 || !inSnaptureSection) continue;
                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (!string.Equals(key, "Portable", StringComparison.OrdinalIgnoreCase)) continue;
                sawPortableValue = true;
                return value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("1", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            return !sawPortableValue;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
