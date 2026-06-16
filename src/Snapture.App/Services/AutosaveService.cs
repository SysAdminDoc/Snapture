using System.IO;
using System.Windows.Threading;
using Serilog;
using Snapture.App.Editor;

namespace Snapture.App.Services;

/// <summary>
/// Periodically saves an <see cref="AnnotationDocument"/> to a crash-recovery
/// file under <c>%LOCALAPPDATA%\Snapture\autosave\</c>.  On clean close the
/// autosave file is deleted; if the app crashes it survives so the next launch
/// can offer recovery.
/// </summary>
public sealed class AutosaveService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>Directory that holds all autosave <c>.snapture-autosave</c> files.</summary>
    public static string AutosaveDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snapture", "autosave");

    private readonly AnnotationDocument _doc;
    private readonly DispatcherTimer _timer;
    private readonly string _autosavePath;
    private bool _disposed;

    public AutosaveService(AnnotationDocument doc)
    {
        _doc = doc ?? throw new ArgumentNullException(nameof(doc));

        Directory.CreateDirectory(AutosaveDirectory);
        _autosavePath = Path.Combine(AutosaveDirectory,
            $"{Guid.NewGuid():N}.snapture-autosave");

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Interval
        };
        _timer.Tick += OnTick;
        _timer.Start();

        Log.Debug("Autosave.Started {Path}", _autosavePath);
    }

    /// <summary>The full path of the autosave file managed by this instance.</summary>
    public string AutosavePath => _autosavePath;

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            SnapFileFormat.Save(_autosavePath, _doc);
            Log.Debug("Autosave.Written {Path}", _autosavePath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autosave.WriteFailed {Path}", _autosavePath);
        }
    }

    /// <summary>
    /// Deletes the autosave file (clean close). Call this when the editor
    /// window closes normally.
    /// </summary>
    public void DeleteAutosave()
    {
        _timer.Stop();
        try
        {
            if (File.Exists(_autosavePath))
            {
                File.Delete(_autosavePath);
                Log.Debug("Autosave.Deleted {Path}", _autosavePath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autosave.DeleteFailed {Path}", _autosavePath);
        }
    }

    /// <summary>
    /// Returns the paths of all <c>.snapture-autosave</c> files that exist
    /// in the autosave directory, ordered newest-first.
    /// </summary>
    public static IReadOnlyList<string> GetPendingAutosaves()
    {
        if (!Directory.Exists(AutosaveDirectory))
            return Array.Empty<string>();

        return Directory.GetFiles(AutosaveDirectory, "*.snapture-autosave")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    /// <summary>
    /// Loads an autosave file using <see cref="SnapFileFormat.Load"/>
    /// and returns the recovered document, or <c>null</c> if the file
    /// is corrupt.
    /// </summary>
    public static AnnotationDocument? TryLoadAutosave(string path)
    {
        try
        {
            // SnapFileFormat.Load expects a .snapture extension internally
            // but it just reads a zip — copy to a temp .snapture so Load
            // works unmodified.
            var tempPath = Path.Combine(Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(path) + SnapFileFormat.Extension);
            File.Copy(path, tempPath, overwrite: true);
            try
            {
                return SnapFileFormat.Load(tempPath);
            }
            finally
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autosave.LoadFailed {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// Removes a single autosave file from disk (e.g. after the user
    /// declines recovery or the file has been successfully opened).
    /// </summary>
    public static void Discard(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Autosave.DiscardFailed {Path}", path);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
    }
}
