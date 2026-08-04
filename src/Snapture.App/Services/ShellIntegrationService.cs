using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace Snapture.App.Services;

internal sealed record ShellIntegrationRegistration(
    string RelativeKeyPath,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Installs the optional Snapture image verbs under the current user's image
/// association. No elevation or machine-wide file association is required.
/// </summary>
internal static class ShellIntegrationService
{
    internal const string ImageAssociationPath =
        @"Software\Classes\SystemFileAssociations\image\shell";
    internal const string RootKeyName = "Snapture";

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            $"{ImageAssociationPath}\\{RootKeyName}\\shell\\open\\command");
        return key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public static void Install(string? executablePath = null)
    {
        string exe = ResolveExecutablePath(executablePath);
        Uninstall();

        foreach (var registration in BuildRegistrations(exe))
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $"{ImageAssociationPath}\\{registration.RelativeKeyPath}", writable: true)
                ?? throw new InvalidOperationException("Windows did not provide a writable user registry key.");

            foreach (var pair in registration.Values)
                key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
        }

        NotifyShellAssociationChanged();
    }

    public static void Uninstall()
    {
        Registry.CurrentUser.DeleteSubKeyTree(
            $"{ImageAssociationPath}\\{RootKeyName}", throwOnMissingSubKey: false);
        NotifyShellAssociationChanged();
    }

    internal static IReadOnlyList<ShellIntegrationRegistration> BuildRegistrations(string executablePath)
    {
        string exe = Path.GetFullPath(executablePath);
        if (exe.Contains('"'))
            throw new ArgumentException("The executable path cannot contain a quote.", nameof(executablePath));

        string command(string arguments) => $"\"{exe}\" {arguments}";
        var registrations = new List<ShellIntegrationRegistration>
        {
            new(
                RootKeyName,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MUIVerb"] = "Snapture",
                    ["Icon"] = exe,
                    ["SubCommands"] = string.Empty
                }),
            new(
                $"{RootKeyName}\\shell\\open",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MUIVerb"] = "Open in Snapture editor"
                }),
            new(
                $"{RootKeyName}\\shell\\open\\command",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [string.Empty] = command("--open \"%1\"")
                }),
            new(
                $"{RootKeyName}\\shell\\convert",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["MUIVerb"] = "Resize / Convert",
                    ["SubCommands"] = string.Empty
                }),
        };

        AddCommand(registrations, RootKeyName, "convert", "png", "Convert to PNG", command("--convert \"%1\" --format png"));
        AddCommand(registrations, RootKeyName, "convert", "jpg", "Convert to JPEG", command("--convert \"%1\" --format jpg"));
        AddCommand(registrations, RootKeyName, "convert", "resize50", "Resize to 50%", command("--convert \"%1\" --resize 50"));
        AddCommand(registrations, RootKeyName, "convert", "resize75", "Resize to 75%", command("--convert \"%1\" --resize 75"));
        AddCommand(registrations, RootKeyName, "convert", "resize125", "Resize to 125%", command("--convert \"%1\" --resize 125"));
        AddCommand(registrations, RootKeyName, "convert", "resize200", "Resize to 200%", command("--convert \"%1\" --resize 200"));
        return registrations;
    }

    private static void AddCommand(
        ICollection<ShellIntegrationRegistration> registrations,
        string root,
        string parent,
        string name,
        string label,
        string command)
    {
        registrations.Add(new ShellIntegrationRegistration(
            $"{root}\\shell\\{parent}\\shell\\{name}",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MUIVerb"] = label
            }));
        registrations.Add(new ShellIntegrationRegistration(
            $"{root}\\shell\\{parent}\\shell\\{name}\\command",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [string.Empty] = command
            }));
    }

    private static string ResolveExecutablePath(string? executablePath)
    {
        string path = string.IsNullOrWhiteSpace(executablePath)
            ? Environment.ProcessPath ?? throw new InvalidOperationException("The current executable path is unavailable.")
            : executablePath;
        return Path.GetFullPath(path);
    }

    private static void NotifyShellAssociationChanged()
    {
        try
        {
            SHChangeNotify(0x08000000, 0x0000, nint.Zero, nint.Zero);
        }
        catch
        {
            // Explorer refresh is best effort; the registry remains authoritative.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
