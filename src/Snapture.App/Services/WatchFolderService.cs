using System.Collections.Concurrent;
using System.IO;
using Serilog;

namespace Snapture.App.Services;

/// <summary>
/// Watches one user-selected folder for completed image files. The watcher never opens
/// files from an event callback until size and last-write time are stable, which keeps
/// partial copies out of history and avoids duplicate imports from multi-event writes.
/// </summary>
public sealed class WatchFolderService : IDisposable
{
    private readonly Func<string, Task> _onImageReady;
    private readonly ConcurrentDictionary<string, byte> _queued = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _processed = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _lifetime;
    private bool _disposed;

    public WatchFolderService(Func<string, Task> onImageReady)
    {
        _onImageReady = onImageReady ?? throw new ArgumentNullException(nameof(onImageReady));
    }

    public bool IsRunning => _watcher is not null;
    public string? FolderPath { get; private set; }

    public void Start(string folderPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        string fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"The watch folder does not exist: {fullPath}");

        Stop();
        _lifetime = new CancellationTokenSource();
        _watcher = new FileSystemWatcher(fullPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*.*",
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFileEvent;
        _watcher.Changed += OnFileEvent;
        _watcher.Renamed += OnFileRenamed;
        FolderPath = fullPath;
    }

    public void Stop()
    {
        _watcher?.Dispose();
        _watcher = null;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        FolderPath = null;
        _queued.Clear();
        _processed.Clear();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Queue(e.FullPath);

    private void OnFileRenamed(object sender, RenamedEventArgs e) => Queue(e.FullPath);

    private void Queue(string path)
    {
        if (_disposed || !IsImage(path) || !_queued.TryAdd(path, 0))
            return;

        _ = ProcessWhenStableAsync(path, _lifetime?.Token ?? CancellationToken.None);
    }

    private async Task ProcessWhenStableAsync(string path, CancellationToken ct)
    {
        try
        {
            (long Length, DateTime LastWriteUtc) previous = (-1, DateTime.MinValue);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > 0)
                    {
                        var current = (info.Length, info.LastWriteTimeUtc);
                        if (current == previous)
                        {
                            if (!_processed.TryGetValue(path, out var processedAt)
                                || processedAt != current.LastWriteTimeUtc)
                            {
                                SafeImageInput.ValidateFile(path);
                                await _onImageReady(path).ConfigureAwait(false);
                                _processed[path] = current.LastWriteTimeUtc;
                            }
                            return;
                        }
                        previous = current;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }

                await Task.Delay(100, ct).ConfigureAwait(false);
            }

            Log.Debug("WatchFolder.FileNeverStabilized {Path}", path);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Log.Warning(ex, "WatchFolder.ImportFailed {Path}", path);
        }
        finally
        {
            _queued.TryRemove(path, out _);
        }
    }

    private static bool IsImage(string path)
        => SafeImageInput.IsSupportedExtension(Path.GetExtension(path));

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }
}
