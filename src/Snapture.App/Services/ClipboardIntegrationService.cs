using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Snapture.App.Services;

public sealed record ClipboardTargetContext(string? ProcessName, string? WindowTitle);

public sealed record MarkdownCopyResult(
    bool Succeeded,
    string? Markdown,
    string? DestinationPath,
    string? Error);

/// <summary>
/// Builds safe relative Markdown links and copies capture PNGs into a user-selected
/// Obsidian/Joplin-compatible folder. Clipboard writes are kept behind a delegate so the
/// filesystem and path contract can be tested without touching the user's clipboard.
/// </summary>
public static class ClipboardIntegrationService
{
    public const string ImageMode = "image";
    public const string MarkdownMode = "markdown";
    public const string DefaultAttachmentFolder = "attachments";

    public static ClipboardTargetContext GetForegroundTarget()
    {
        var hwnd = Native2.GetForegroundWindow();
        var (processName, windowTitle) = CaptureHistoryService.DescribeForeground(hwnd);
        return new ClipboardTargetContext(processName, windowTitle);
    }

    public static string? ResolveVaultFolder(
        string? configuredFolder,
        ClipboardTargetContext? target,
        string? obsidianConfigPath = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder) && Directory.Exists(configuredFolder))
            return Path.GetFullPath(configuredFolder);

        if (!IsProcess(target?.ProcessName, "Obsidian"))
            return null;

        var configPath = obsidianConfigPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "obsidian", "obsidian.json");
        if (!File.Exists(configPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (!document.RootElement.TryGetProperty("vaults", out var vaults)
                || vaults.ValueKind != JsonValueKind.Object)
                return null;

            var candidates = new List<string>();
            foreach (var vault in vaults.EnumerateObject())
            {
                if (!vault.Value.TryGetProperty("path", out var path)
                    || path.ValueKind != JsonValueKind.String)
                    continue;

                var value = path.GetString();
                if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                    candidates.Add(Path.GetFullPath(value));
            }

            if (candidates.Count == 0)
                return null;

            var title = target?.WindowTitle ?? string.Empty;
            var titleMatch = candidates.FirstOrDefault(path =>
                title.Contains(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)),
                    StringComparison.OrdinalIgnoreCase));
            return titleMatch ?? (candidates.Count == 1 ? candidates[0] : null);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static MarkdownCopyResult TryCopyCaptureAsMarkdown(
        string capturePath,
        string? configuredVaultFolder,
        string? attachmentFolder,
        ClipboardTargetContext? target = null,
        string? obsidianConfigPath = null,
        Action<string>? clipboardWriter = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(capturePath) || !File.Exists(capturePath))
                return Failure("The most recent capture is no longer available.");

            var vault = ResolveVaultFolder(configuredVaultFolder, target ?? GetForegroundTarget(), obsidianConfigPath);
            if (vault is null)
            {
                return Failure(
                    "Choose a Markdown vault folder in Settings > Output, or focus an Obsidian vault first.");
            }

            var destination = CopyAsPng(capturePath, vault, attachmentFolder);
            var relative = Path.GetRelativePath(vault, destination)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            var markdown = BuildMarkdown(relative);
            (clipboardWriter ?? Clipboard.SetText)(markdown);
            return new MarkdownCopyResult(true, markdown, destination, null);
        }
        catch (Exception ex)
        {
            return Failure($"Markdown copy failed: {ex.Message}");
        }
    }

    public static bool TryCopyCaptureAsImage(string capturePath, out string? error)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(capturePath) || !File.Exists(capturePath))
                throw new FileNotFoundException("The capture file is not available.", capturePath);

            var image = SafeImageInput.LoadBitmapImage(capturePath);
            Clipboard.SetImage(image);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string BuildMarkdown(string relativePath, string altText = "Snapture capture")
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("A relative image path is required.", nameof(relativePath));

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(normalized))
            throw new ArgumentException("The image path must be relative to the vault.", nameof(relativePath));
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("The image path must stay inside the vault.", nameof(relativePath));

        var link = string.Join("/", segments.Select(Uri.EscapeDataString));
        var escapedAlt = (altText ?? "Snapture capture")
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);
        return $"![{escapedAlt}]({link})";
    }

    private static string CopyAsPng(string capturePath, string vault, string? attachmentFolder)
    {
        var folder = NormalizeAttachmentFolder(attachmentFolder);
        var root = Path.GetFullPath(vault);
        var destinationDirectory = Path.GetFullPath(Path.Combine(root, folder));
        if (!IsChildOrSame(destinationDirectory, root))
            throw new InvalidOperationException("The attachment folder must stay inside the vault.");

        Directory.CreateDirectory(destinationDirectory);
        var fileName = Path.GetFileNameWithoutExtension(capturePath) + ".png";
        var destination = GetAvailablePath(destinationDirectory, fileName);
        var source = Path.GetFullPath(capturePath);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            return destination;

        using var input = SafeImageInput.Open(source);
        try
        {
            if (input.Info.Format == SafeImageFormat.Png)
            {
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.Stream.CopyTo(output);
            }
            else
            {
                using var image = Image.FromStream(input.Stream, useEmbeddedColorManagement: false, validateImageData: true);
                image.Save(destination, ImageFormat.Png);
            }
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        return destination;
    }

    private static string NormalizeAttachmentFolder(string? value)
    {
        var folder = string.IsNullOrWhiteSpace(value) ? DefaultAttachmentFolder : value.Trim();
        if (Path.IsPathRooted(folder))
            throw new ArgumentException("The attachment folder must be relative.", nameof(value));

        var segments = folder
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
            throw new ArgumentException("The attachment folder must be a safe relative path.", nameof(value));
        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static string GetAvailablePath(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        for (var index = 1; File.Exists(candidate); index++)
            candidate = Path.Combine(directory, $"{stem}_{index}{extension}");
        return candidate;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The original error is more useful to the caller than cleanup failure.
        }
    }

    private static bool IsChildOrSame(string path, string root)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return normalizedPath.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProcess(string? actual, string expected)
    {
        var name = string.IsNullOrWhiteSpace(actual)
            ? string.Empty
            : Path.GetFileNameWithoutExtension(actual.Trim());
        return string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static MarkdownCopyResult Failure(string error) =>
        new(false, null, null, error);
}
