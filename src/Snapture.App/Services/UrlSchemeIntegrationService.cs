using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace Snapture.App.Services;

public enum UriCaptureMode
{
    Region,
    Window,
    Fullscreen,
    Scrolling,
    LastRegion
}

public sealed record CaptureUriRequest(
    UriCaptureMode Mode,
    bool? CopyToClipboardOverride,
    bool? OpenEditorOverride,
    string RawUri);

internal sealed record UrlSchemeRegistration(
    string RelativeKeyPath,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Parses the opt-in snapture:// capture protocol and owns its current-user
/// registration. File paths are intentionally not a supported command surface.
/// </summary>
internal static class UrlSchemeIntegrationService
{
    internal const string RegistryPath = @"Software\Classes\snapture";

    public static bool TryParse(
        string rawUri,
        out CaptureUriRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawUri) || rawUri.Length > 4096)
        {
            error = "The snapture URI is empty or too long.";
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(rawUri.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            error = "The snapture URI contains invalid escaping.";
            return false;
        }

        if (ContainsUnsafeFileReference(decoded))
        {
            error = "The snapture URI contains a rejected UNC, SMB, file URI, or traversal path.";
            return false;
        }

        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("snapture", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length > 0
            || uri.Port != -1
            || uri.Fragment.Length > 0
            || uri.Host.Length > 0 && !uri.Host.Equals("capture", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only snapture://capture URIs without credentials, ports, or fragments are accepted.";
            return false;
        }

        string path = uri.AbsolutePath.Trim('/');
        if (uri.Host.Length == 0 && !path.Equals("capture", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Length > 0 && path.Length > 0)
        {
            error = "The snapture URI must target the capture command.";
            return false;
        }

        var query = ParseQuery(uri.Query, out error);
        if (query is null) return false;

        UriCaptureMode mode = UriCaptureMode.Region;
        bool autoScroll = false;
        string? destination = null;
        foreach (var pair in query)
        {
            switch (pair.Key)
            {
                case "mode":
                    if (!TryParseMode(pair.Value, out mode))
                    {
                        error = "mode must be region, window, fullscreen, scrolling, or last-region.";
                        return false;
                    }
                    break;
                case "autoscroll":
                    if (!bool.TryParse(pair.Value, out autoScroll))
                    {
                        error = "autoscroll must be true or false.";
                        return false;
                    }
                    break;
                case "dest":
                    if (destination is not null
                        || !pair.Value.Equals("clipboard", StringComparison.OrdinalIgnoreCase)
                        && !pair.Value.Equals("editor", StringComparison.OrdinalIgnoreCase)
                        && !pair.Value.Equals("file", StringComparison.OrdinalIgnoreCase))
                    {
                        error = "dest must be clipboard, editor, or file and may only be specified once.";
                        return false;
                    }
                    destination = pair.Value.ToLowerInvariant();
                    break;
                case "filepath":
                case "path":
                case "source":
                    if (!IsSafeUserPath(pair.Value))
                    {
                        error = "rejected: file paths must remain under the current user's profile and cannot be UNC, SMB, file URI, or traversal paths.";
                        return false;
                    }
                    error = "File path parameters are not supported by the capture protocol.";
                    return false;
                default:
                    error = $"Unknown snapture URI parameter: {pair.Key}";
                    return false;
            }
        }

        if (autoScroll)
            mode = UriCaptureMode.Scrolling;

        bool? copyOverride = destination switch
        {
            "clipboard" => true,
            "editor" or "file" => false,
            _ => null
        };
        bool? openOverride = destination switch
        {
            "clipboard" or "file" => false,
            "editor" => true,
            _ => null
        };
        request = new CaptureUriRequest(mode, copyOverride, openOverride, rawUri);
        return true;
    }

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey($"{RegistryPath}\\shell\\open\\command");
        return key?.GetValue(null) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public static void Install(string? executablePath = null)
    {
        string exe = ResolveExecutablePath(executablePath);
        Uninstall();
        foreach (var registration in BuildRegistrations(exe))
        {
            string keyPath = string.IsNullOrEmpty(registration.RelativeKeyPath)
                ? RegistryPath
                : $"{RegistryPath}\\{registration.RelativeKeyPath}";
            using var key = Registry.CurrentUser.CreateSubKey(
                keyPath, writable: true)
                ?? throw new InvalidOperationException("Windows did not provide a writable user registry key.");
            foreach (var pair in registration.Values)
                key.SetValue(pair.Key, pair.Value, RegistryValueKind.String);
        }
        NotifyShellAssociationChanged();
    }

    public static void Uninstall()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        NotifyShellAssociationChanged();
    }

    internal static IReadOnlyList<UrlSchemeRegistration> BuildRegistrations(string executablePath)
    {
        string exe = Path.GetFullPath(executablePath);
        if (exe.Contains('"'))
            throw new ArgumentException("The executable path cannot contain a quote.", nameof(executablePath));

        return new[]
        {
            new UrlSchemeRegistration(
                string.Empty,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [string.Empty] = "URL:Snapture Protocol",
                    ["URL Protocol"] = string.Empty
                }),
            new UrlSchemeRegistration(
                "DefaultIcon",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [string.Empty] = exe
                }),
            new UrlSchemeRegistration(
                "shell\\open\\command",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [string.Empty] = $"\"{exe}\" --uri \"%1\""
                })
        };
    }

    private static bool TryParseMode(string value, out UriCaptureMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "region" => UriCaptureMode.Region,
            "window" => UriCaptureMode.Window,
            "fullscreen" or "full" => UriCaptureMode.Fullscreen,
            "scrolling" or "scroll" => UriCaptureMode.Scrolling,
            "last-region" or "last" => UriCaptureMode.LastRegion,
            _ => (UriCaptureMode)(-1)
        };
        return (int)mode >= 0;
    }

    private static Dictionary<string, string>? ParseQuery(string rawQuery, out string error)
    {
        error = string.Empty;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string query = rawQuery.TrimStart('?');
        if (query.Length == 0) return result;

        foreach (string segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = segment.IndexOf('=');
            string rawKey = separator >= 0 ? segment[..separator] : segment;
            string rawValue = separator >= 0 ? segment[(separator + 1)..] : string.Empty;
            string key;
            string value;
            try
            {
                key = Uri.UnescapeDataString(rawKey.Replace('+', ' ')).ToLowerInvariant();
                value = Uri.UnescapeDataString(rawValue.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                error = "The snapture URI query contains invalid escaping.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(key) || result.ContainsKey(key))
            {
                error = "The snapture URI contains an empty or duplicate parameter.";
                return null;
            }
            result[key] = value;
        }
        return result;
    }

    private static bool ContainsUnsafeFileReference(string decoded)
    {
        if (decoded.Contains("\\\\", StringComparison.Ordinal)
            || decoded.Contains("file://", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains("smb://", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains("..", StringComparison.Ordinal))
            return true;

        return decoded.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment[(segment.IndexOf('=') + 1)..])
            .Any(value => value.StartsWith("//", StringComparison.Ordinal));
    }

    private static bool IsSafeUserPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("\\\\", StringComparison.Ordinal)
            || value.Contains("file://", StringComparison.OrdinalIgnoreCase)
            || value.Contains("smb://", StringComparison.OrdinalIgnoreCase)
            || value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(segment => segment == ".."))
            return false;

        try
        {
            string profile = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string path = Path.GetFullPath(value);
            return path.StartsWith(profile, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
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
        try { SHChangeNotify(0x08000000, 0x0000, nint.Zero, nint.Zero); }
        catch { }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(uint eventId, uint flags, nint item1, nint item2);
}
