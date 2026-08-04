using System.IO;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using Microsoft.Windows.Storage.Pickers;

namespace Snapture.App.Services;

/// <summary>
/// Provides the WinAppSDK 1.8 storage pickers to WPF windows and keeps a
/// Microsoft.Win32 fallback for tray-only calls or hosts without the runtime.
/// </summary>
public static class StoragePickerService
{
    public sealed record FileTypeChoice(string Label, IReadOnlyList<string> Extensions);

    public static async Task<string?> PickOpenFileAsync(
        Window? owner,
        string fallbackFilter,
        IEnumerable<string> extensions,
        string? initialDirectory = null,
        string? title = null)
    {
        var resolvedOwner = ResolveOwner(owner);
        if (TryGetWindowId(resolvedOwner, out var windowId))
        {
            try
            {
                var picker = new FileOpenPicker(windowId);
                AddExtensions(picker.FileTypeFilter, extensions);
                picker.CommitButtonText = "Open";
                picker.SuggestedStartLocation = ResolveStartLocation(initialDirectory);

                var result = await picker.PickSingleFileAsync();
                return result?.Path;
            }
            catch
            {
                // The new picker is unavailable on some deployed Windows App SDK
                // configurations. Fall through to the compatible desktop dialog.
            }
        }

        var dialog = new OpenFileDialog
        {
            Filter = fallbackFilter,
            CheckFileExists = true,
            Title = title ?? string.Empty
        };
        if (Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(resolvedOwner) == true ? dialog.FileName : null;
    }

    public static async Task<string?> PickSaveFileAsync(
        Window? owner,
        string fallbackFilter,
        string suggestedFileName,
        string defaultExtension,
        IEnumerable<FileTypeChoice> fileTypes,
        string? initialDirectory = null,
        string? title = null)
    {
        var normalizedDefaultExtension = NormalizeExtension(defaultExtension);
        var resolvedOwner = ResolveOwner(owner);
        var choices = fileTypes
            .Select(choice => new FileTypeChoice(
                choice.Label.Trim(),
                NormalizeExtensions(choice.Extensions)))
            .Where(choice => choice.Label.Length > 0 && choice.Extensions.Count > 0)
            .ToArray();

        if (TryGetWindowId(resolvedOwner, out var windowId))
        {
            try
            {
                var picker = new FileSavePicker(windowId)
                {
                    // The modern picker appends DefaultFileExtension itself.
                    SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
                    DefaultFileExtension = normalizedDefaultExtension,
                    CommitButtonText = "Save",
                    SuggestedStartLocation = ResolveStartLocation(initialDirectory)
                };

                foreach (var choice in choices)
                    picker.FileTypeChoices.Add(choice.Label, choice.Extensions.ToList());

                if (choices.Length == 0)
                {
                    picker.FileTypeChoices.Add(
                        "File",
                        new[] { normalizedDefaultExtension });
                }

                var result = await picker.PickSaveFileAsync();
                return result?.Path;
            }
            catch
            {
                // Fall through to the compatible desktop dialog.
            }
        }

        var dialog = new SaveFileDialog
        {
            Filter = fallbackFilter,
            FileName = Path.GetFileName(suggestedFileName),
            DefaultExt = normalizedDefaultExtension.TrimStart('.'),
            AddExtension = true,
            OverwritePrompt = true,
            Title = title ?? string.Empty
        };
        if (Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(resolvedOwner) == true ? dialog.FileName : null;
    }

    public static async Task<string?> PickFolderAsync(
        Window? owner,
        string? initialDirectory = null,
        string? title = null)
    {
        var resolvedOwner = ResolveOwner(owner);
        if (TryGetWindowId(resolvedOwner, out var windowId))
        {
            try
            {
                var picker = new FolderPicker(windowId);
                picker.CommitButtonText = "Select folder";
                picker.SuggestedStartLocation = ResolveStartLocation(initialDirectory);

                var result = await picker.PickSingleFolderAsync();
                return result?.Path;
            }
            catch
            {
                // Fall through to the compatible desktop dialog.
            }
        }

        var dialog = new OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
            Title = title ?? string.Empty
        };
        return dialog.ShowDialog(resolvedOwner) == true ? dialog.FolderName : null;
    }

    internal static string BuildFilter(IEnumerable<FileTypeChoice> fileTypes)
    {
        var entries = fileTypes
            .Select(choice =>
            {
                var extensions = NormalizeExtensions(choice.Extensions);
                var patterns = string.Join(';', extensions.Select(extension => $"*{extension}"));
                return (Label: choice.Label.Trim(), Pattern: patterns);
            })
            .Where(entry => entry.Label.Length > 0 && entry.Pattern.Length > 0)
            .ToArray();

        return entries.Length == 0
            ? "All files (*.*)|*.*"
            : string.Join('|', entries.Select(entry => $"{entry.Label} ({entry.Pattern})|{entry.Pattern}"));
    }

    internal static IReadOnlyList<string> NormalizeExtensions(IEnumerable<string> extensions) =>
        extensions
            .Select(NormalizeExtension)
            .Where(extension => extension.Length > 1 && !extension.Contains('*'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static string NormalizeExtension(string extension)
    {
        var normalized = extension.Trim();
        if (normalized.Length == 0 || normalized == "*" || normalized == "*.*")
            return ".*";

        normalized = normalized.TrimStart('*');
        return normalized.StartsWith('.') ? normalized.ToLowerInvariant() : $".{normalized.ToLowerInvariant()}";
    }

    private static void AddExtensions(IList<string> target, IEnumerable<string> extensions)
    {
        foreach (var extension in NormalizeExtensions(extensions))
            target.Add(extension);
    }

    private static Window? ResolveOwner(Window? owner)
    {
        if (owner is not null)
            return owner;

        var windows = Application.Current?.Windows.OfType<Window>();
        return windows?.FirstOrDefault(window => window.IsVisible && window.IsActive)
            ?? windows?.FirstOrDefault(window => window.IsVisible);
    }

    private static bool TryGetWindowId(Window? owner, out Microsoft.UI.WindowId windowId)
    {
        windowId = default;
        if (owner is null)
            return false;

        var hwnd = new WindowInteropHelper(owner).Handle;
        if (hwnd == nint.Zero)
            return false;

        windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return windowId.Value != 0;
    }

    private static PickerLocationId ResolveStartLocation(string? initialDirectory)
    {
        if (!Directory.Exists(initialDirectory))
            return PickerLocationId.Unspecified;

        var fullPath = Path.GetFullPath(initialDirectory!);
        var locations = new (Environment.SpecialFolder Folder, PickerLocationId Location)[]
        {
            (Environment.SpecialFolder.MyDocuments, PickerLocationId.DocumentsLibrary),
            (Environment.SpecialFolder.MyPictures, PickerLocationId.PicturesLibrary),
            (Environment.SpecialFolder.MyVideos, PickerLocationId.VideosLibrary),
            (Environment.SpecialFolder.MyMusic, PickerLocationId.MusicLibrary),
            (Environment.SpecialFolder.Desktop, PickerLocationId.Desktop)
        };

        foreach (var (folder, location) in locations)
        {
            var knownPath = Environment.GetFolderPath(folder);
            if (PathEqualsOrChildOf(fullPath, knownPath))
                return location;
        }

        var downloadsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        return PathEqualsOrChildOf(fullPath, downloadsPath)
            ? PickerLocationId.Downloads
            : PickerLocationId.Unspecified;
    }

    private static bool PathEqualsOrChildOf(string path, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent))
            return false;

        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        return normalizedPath.Equals(normalizedParent, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
