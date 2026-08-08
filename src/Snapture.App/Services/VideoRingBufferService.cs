using System.IO;
using Serilog;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Keeps a bounded rolling MP4 on disk and renders a requested recent range into a
/// user-selected file. Each session has an atomic manifest and short fragmented-MP4
/// segments so an interrupted process can be quarantined instead of silently losing
/// or exposing stale screen data.
/// </summary>
internal sealed class VideoRingBufferService : IDisposable
{
    private const int MaximumBufferSeconds = 90;
    private const int SegmentDurationSeconds = 30;
    private const int MaximumSegmentCount = 3;
    private const long MaximumBufferBytes = 256L * 1024 * 1024;
    private const long MinimumFreeSpaceBytes = 64L * 1024 * 1024;
    private static readonly string BufferDirectory = VideoRingBufferRecovery.DefaultBufferDirectory;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _maintenance;
    private VideoRecorder? _recorder;
    private string? _currentPath;
    private string? _sessionDirectory;
    private RingBufferSessionManifest? _manifest;
    private RingSource? _source;
    private int _segmentNumber;
    private TimeSpan _completedDuration;
    private int _fps;
    private int _bitrateMbps;
    private int _outputWidth;
    private int _outputHeight;
    private bool _autoTighten;
    private HdrToneMapOperator _toneMapOperator = HdrToneMapOperator.Reinhard;
    private bool _hdrColorCorrection = true;
    private bool _running;
    private bool _disposed;

    public bool IsRunning => Volatile.Read(ref _running);
    public TimeSpan BufferedDuration
    {
        get
        {
            TimeSpan duration = _completedDuration + (_recorder?.Elapsed ?? TimeSpan.Zero);
            return duration > TimeSpan.FromSeconds(MaximumBufferSeconds)
                ? TimeSpan.FromSeconds(MaximumBufferSeconds)
                : duration;
        }
    }

    public string Status { get; private set; } = "Ring buffer off";
    public event Action? StateChanged;

    public void StartWindow(
        nint hwnd,
        int fps,
        int bitrateMbps,
        int outputWidth,
        int outputHeight,
        bool autoTighten,
        HdrToneMapOperator toneMapOperator = HdrToneMapOperator.Reinhard,
        bool hdrColorCorrection = true)
    {
        if (hwnd == 0)
            throw new ArgumentException("A foreground window is required.", nameof(hwnd));

        Start(new RingSource(RingSourceMode.Window, hwnd), fps, bitrateMbps, outputWidth, outputHeight, autoTighten, toneMapOperator, hdrColorCorrection);
    }

    public void StartMonitor(
        nint hMonitor,
        int fps,
        int bitrateMbps,
        int outputWidth,
        int outputHeight,
        bool autoTighten,
        HdrToneMapOperator toneMapOperator = HdrToneMapOperator.Reinhard,
        bool hdrColorCorrection = true)
    {
        if (hMonitor == 0)
            throw new ArgumentException("A monitor is required.", nameof(hMonitor));

        Start(new RingSource(RingSourceMode.Monitor, hMonitor), fps, bitrateMbps, outputWidth, outputHeight, autoTighten, toneMapOperator, hdrColorCorrection);
    }

    public void Stop()
    {
        if (_disposed)
            return;

        _gate.Wait();
        try
        {
            _maintenance?.Dispose();
            _maintenance = null;
            if (_manifest is not null && _sessionDirectory is not null)
            {
                try
                {
                    _manifest.State = RingBufferSessionState.CleanStop;
                    _manifest.ActiveFileName = null;
                    VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "VideoRingBuffer.CleanStopManifestFailed");
                }
            }

            StopCurrentLocked(deleteFile: true);
            DeleteSessionLocked();
            _source = null;
            _completedDuration = TimeSpan.Zero;
            Status = "Ring buffer off";
            RaiseStateChanged();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SaveRecentAsync(
        TimeSpan requestedDuration,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        if (requestedDuration <= TimeSpan.Zero || requestedDuration > TimeSpan.FromSeconds(MaximumBufferSeconds))
            throw new ArgumentOutOfRangeException(nameof(requestedDuration), "Choose a recent range between 1 and 90 seconds.");
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("An output path is required.", nameof(outputPath));

        await _gate.WaitAsync(cancellationToken);
        RingSource? source = null;
        string? oldSessionDirectory = null;
        bool oldSessionDeleted = false;
        try
        {
            ThrowIfDisposed();
            if (!IsRunning || _recorder is null || _currentPath is null || _source is null || _manifest is null || _sessionDirectory is null)
                throw new InvalidOperationException("The ring buffer is not running.");

            source = _source;
            oldSessionDirectory = _sessionDirectory;
            _manifest.State = RingBufferSessionState.Saving;
            VideoRingBufferRecovery.WriteManifest(oldSessionDirectory, _manifest);
            StopCurrentLocked(deleteFile: false);

            var segments = _manifest.Segments
                .Where(segment => segment.State == "complete")
                .Where(segment => File.Exists(Path.Combine(oldSessionDirectory, segment.FileName)))
                .OrderBy(segment => segment.StartedUtc)
                .ToArray();
            if (segments.Length == 0)
                throw new InvalidOperationException("The ring buffer has no finalized video segment to save.");

            var paths = segments.Select(segment => Path.Combine(oldSessionDirectory, segment.FileName)).ToArray();
            var durations = new TimeSpan[paths.Length];
            for (int i = 0; i < paths.Length; i++)
                durations[i] = await VideoSegmentService.GetDurationAsync(paths[i]);

            await VideoSegmentService.RenderRecentAsync(paths, durations, requestedDuration, outputPath, cancellationToken);
            DeleteSessionLocked();
            oldSessionDeleted = true;
            _completedDuration = TimeSpan.Zero;
            StartSessionLocked(source.Value);
            Status = $"Ring buffer saved last {requestedDuration.TotalSeconds:0}s";
            RaiseStateChanged();
            return outputPath;
        }
        catch
        {
            if (!oldSessionDeleted && _recorder is not null)
                StopCurrentLocked(deleteFile: false);

            if (!oldSessionDeleted && _manifest is not null && oldSessionDirectory is not null)
            {
                _manifest.State = RingBufferSessionState.RecoveryRequired;
                _manifest.ActiveFileName = null;
                _manifest.LastError = "Saving the requested range failed; the interrupted segments were retained for review.";
                VideoRingBufferRecovery.WriteManifest(oldSessionDirectory, _manifest);
                _manifest = null;
                _sessionDirectory = null;
                _currentPath = null;
                _completedDuration = TimeSpan.Zero;
            }

            if (source is { } restartSource)
            {
                try
                {
                    StartSessionLocked(restartSource);
                }
                catch (Exception ex)
                {
                    Status = $"Ring buffer stopped: {ex.Message}";
                    Log.Warning(ex, "VideoRingBuffer.RestartAfterSaveFailed");
                    RaiseStateChanged();
                }
            }
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    internal static TimeSpan SelectRecentStart(TimeSpan duration, TimeSpan requestedDuration)
        => duration <= requestedDuration ? TimeSpan.Zero : duration - requestedDuration;

    private void Start(
        RingSource source,
        int fps,
        int bitrateMbps,
        int outputWidth,
        int outputHeight,
        bool autoTighten,
        HdrToneMapOperator toneMapOperator,
        bool hdrColorCorrection)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(VideoRingBufferService));

        _gate.Wait();
        try
        {
            if (IsRunning)
                throw new InvalidOperationException("The ring buffer is already running.");

            _fps = fps;
            _bitrateMbps = bitrateMbps;
            _outputWidth = outputWidth;
            _outputHeight = outputHeight;
            _autoTighten = autoTighten;
            _toneMapOperator = toneMapOperator;
            _hdrColorCorrection = hdrColorCorrection;
            StartSessionLocked(source);
            _maintenance = new Timer(MaintainBuffer, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch
        {
            StopCurrentLocked(deleteFile: true);
            DeleteSessionLocked();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartSessionLocked(RingSource source)
    {
        if (_sessionDirectory is not null)
            throw new InvalidOperationException("A ring-buffer session is already allocated.");

        // A failed save can leave a quarantined session while this process starts
        // a fresh one. Reapply the same age/count/byte policy before allocating it
        // so repeated failures cannot grow the temp tree without bound.
        VideoRingBufferRecovery.RecoverOrphans(BufferDirectory);
        EnsureDiskSpaceLocked();
        string directory = VideoRingBufferRecovery.CreateSessionDirectory(BufferDirectory);
        var manifest = new RingBufferSessionManifest
        {
            SessionId = Path.GetFileName(directory),
            State = RingBufferSessionState.Starting,
            SourceMode = source.Mode.ToString(),
            SourceHandle = source.Handle.ToInt64(),
            Fps = _fps,
            BitrateMbps = _bitrateMbps,
            OutputWidth = _outputWidth,
            OutputHeight = _outputHeight,
            AutoTighten = _autoTighten,
            ToneMapOperator = _toneMapOperator.ToString(),
            HdrColorCorrection = _hdrColorCorrection,
            StartedUtc = DateTime.UtcNow
        };
        _sessionDirectory = directory;
        _manifest = manifest;
        _source = source;
        _segmentNumber = 0;
        _completedDuration = TimeSpan.Zero;
        try
        {
            StartSegmentLocked(source);
        }
        catch
        {
            DeleteSessionLocked();
            throw;
        }
    }

    private void StartSegmentLocked(RingSource source)
    {
        if (_sessionDirectory is null || _manifest is null)
            throw new InvalidOperationException("No ring-buffer session is allocated.");

        EnsureDiskSpaceLocked();
        string fileName = $"segment-{++_segmentNumber:000}.mp4";
        string path = Path.Combine(_sessionDirectory, fileName);
        var segment = new RingBufferSegmentManifest
        {
            FileName = fileName,
            StartedUtc = DateTime.UtcNow,
            State = "active"
        };
        _manifest.Segments.Add(segment);
        _manifest.ActiveFileName = fileName;
        _manifest.State = RingBufferSessionState.Starting;
        VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);

        var recorder = new VideoRecorder(
            new RecordingAudioOptions { IncludeSystemAudio = false },
            autoTightenEnabled: _autoTighten,
            toneMapOperator: _toneMapOperator,
            hdrColorCorrection: _hdrColorCorrection);
        try
        {
            if (source.Mode == RingSourceMode.Window)
                recorder.StartWindow(source.Handle, path, _fps, _bitrateMbps, _outputWidth, _outputHeight);
            else
                recorder.StartMonitor(source.Handle, path, _fps, _bitrateMbps, _outputWidth, _outputHeight);

            recorder.SourceClosed += OnRecorderSourceClosed;
            recorder.EnvironmentChanged += OnRecorderEnvironmentChanged;
            _recorder = recorder;
            _currentPath = path;
            _running = true;
            _manifest.State = RingBufferSessionState.Recording;
            VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
            Status = "Ring buffer recording";
            RaiseStateChanged();
            Log.Information("VideoRingBuffer.Started {Source} {MaxSeconds}s {SegmentSeconds}s", source.Mode, MaximumBufferSeconds, SegmentDurationSeconds);
        }
        catch
        {
            recorder.Dispose();
            _manifest.Segments.Remove(segment);
            _manifest.ActiveFileName = null;
            _manifest.State = RingBufferSessionState.RecoveryRequired;
            VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
            TryDelete(path);
            throw;
        }
    }

    private async void MaintainBuffer(object? state)
    {
        if (_disposed || !IsRunning || (_recorder?.Elapsed ?? TimeSpan.Zero) < TimeSpan.FromSeconds(SegmentDurationSeconds))
            return;

        bool acquired = false;
        try
        {
            await _gate.WaitAsync();
            acquired = true;
            if (_disposed || !IsRunning || _source is not { } source || _manifest is null || _sessionDirectory is null)
                return;

            RotateSegmentLocked(source, "segment reached its 30-second boundary");
        }
        catch (Exception ex)
        {
            if (_manifest is not null && _sessionDirectory is not null)
            {
                _manifest.State = RingBufferSessionState.RecoveryRequired;
                _manifest.LastError = ex.Message;
                _manifest.ActiveFileName = null;
                VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
            }
            Status = $"Ring buffer stopped: {ex.Message}";
            Log.Warning(ex, "VideoRingBuffer.RotationFailed");
            RaiseStateChanged();
        }
        finally
        {
            if (acquired)
                _gate.Release();
        }
    }

    private void OnRecorderSourceClosed()
    {
        if (_disposed)
            return;

        _ = Task.Run(() =>
        {
            bool acquired = false;
            try
            {
                _gate.Wait();
                acquired = true;
                if (!IsRunning || _manifest is null || _sessionDirectory is null)
                    return;

                _manifest.State = RingBufferSessionState.RecoveryRequired;
                _manifest.LastError = "The capture source closed or changed before the ring-buffer session stopped.";
                VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
                StopCurrentLocked(deleteFile: false);
                Status = "Ring buffer stopped: source changed; recording kept for review";
                RaiseStateChanged();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "VideoRingBuffer.SourceClosedRecoveryFailed");
            }
            finally
            {
                if (acquired)
                    _gate.Release();
            }
        });
    }

    private void OnRecorderEnvironmentChanged(string reason)
    {
        if (_disposed)
            return;

        _ = Task.Run(() =>
        {
            bool acquired = false;
            try
            {
                _gate.Wait();
                acquired = true;
                if (!IsRunning || _source is not { } source || _manifest is null || _sessionDirectory is null)
                    return;

                RotateSegmentLocked(source, $"recording environment changed: {reason}");
            }
            catch (Exception ex)
            {
                if (_manifest is not null && _sessionDirectory is not null)
                {
                    _manifest.State = RingBufferSessionState.RecoveryRequired;
                    _manifest.LastError = ex.Message;
                    _manifest.ActiveFileName = null;
                    VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
                }
                Status = $"Ring buffer stopped: {ex.Message}";
                Log.Warning(ex, "VideoRingBuffer.EnvironmentRotationFailed");
                RaiseStateChanged();
            }
            finally
            {
                if (acquired)
                    _gate.Release();
            }
        });
    }

    private void RotateSegmentLocked(RingSource source, string reason)
    {
        if (_manifest is null || _sessionDirectory is null)
            throw new InvalidOperationException("No ring-buffer session is available for rotation.");

        _manifest.State = RingBufferSessionState.Rotating;
        _manifest.LastError = reason;
        VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
        StopCurrentLocked(deleteFile: false);
        var removed = VideoRingBufferRecovery.PruneSegments(
            _sessionDirectory,
            _manifest,
            MaximumSegmentCount,
            MaximumBufferBytes,
            DateTime.UtcNow);
        foreach (var segment in removed)
            _completedDuration -= TimeSpan.FromSeconds(Math.Max(0, segment.DurationSeconds));
        if (_completedDuration < TimeSpan.Zero)
            _completedDuration = TimeSpan.Zero;
        StartSegmentLocked(source);
        Status = $"Ring buffer recording · {reason}";
        RaiseStateChanged();
    }

    private void StopCurrentLocked(bool deleteFile)
    {
        var recorder = _recorder;
        var path = _currentPath;
        _recorder = null;
        _currentPath = null;
        _running = false;

        TimeSpan duration = recorder?.Elapsed ?? TimeSpan.Zero;
        if (recorder is not null)
        {
            recorder.SourceClosed -= OnRecorderSourceClosed;
            recorder.EnvironmentChanged -= OnRecorderEnvironmentChanged;
            try { recorder.Stop(); }
            catch (Exception ex) { Log.Debug(ex, "VideoRingBuffer.StopFailed"); }
            recorder.Dispose();
        }

        if (path is null)
            return;

        if (deleteFile)
        {
            TryDelete(path);
            return;
        }

        CompleteActiveSegmentLocked(path, duration);
    }

    private void CompleteActiveSegmentLocked(string path, TimeSpan duration)
    {
        if (_manifest is null || _sessionDirectory is null)
            return;

        var segment = _manifest.Segments.FirstOrDefault(item =>
            string.Equals(item.FileName, Path.GetFileName(path), StringComparison.OrdinalIgnoreCase));
        if (segment is null)
            return;

        segment.State = "complete";
        segment.CompletedUtc = DateTime.UtcNow;
        segment.DurationSeconds = Math.Max(0, duration.TotalSeconds);
        segment.SizeBytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        _manifest.ActiveFileName = null;
        _completedDuration += duration;
        VideoRingBufferRecovery.WriteManifest(_sessionDirectory, _manifest);
    }

    private void EnsureDiskSpaceLocked()
    {
        string fullDirectory = Path.GetFullPath(BufferDirectory);
        string root = Path.GetPathRoot(fullDirectory) ?? fullDirectory;
        var drive = new DriveInfo(root);
        long estimatedSegmentBytes = Math.Max(
            MinimumFreeSpaceBytes,
            (long)Math.Max(1, _bitrateMbps) * 125_000L * SegmentDurationSeconds + 8L * 1024 * 1024);
        if (!VideoRingBufferRecovery.HasSufficientDiskSpace(drive.AvailableFreeSpace, estimatedSegmentBytes))
        {
            throw new IOException($"Not enough free disk space for the ring buffer (need about {estimatedSegmentBytes / (1024 * 1024)} MiB).");
        }
    }

    private void DeleteSessionLocked()
    {
        string? directory = _sessionDirectory;
        _sessionDirectory = null;
        _manifest = null;
        _currentPath = null;
        _recorder = null;
        _running = false;
        if (directory is null)
            return;

        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "VideoRingBuffer.SessionDeleteFailed {Directory}", directory);
        }
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RaiseStateChanged()
    {
        try { StateChanged?.Invoke(); }
        catch (Exception ex) { Log.Debug(ex, "VideoRingBuffer.StateChangedFailed"); }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) { Log.Debug(ex, "VideoRingBuffer.TempDeleteFailed {Path}", path); }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _disposed = true;
        _gate.Dispose();
    }

    private enum RingSourceMode { Window, Monitor }

    private readonly record struct RingSource(RingSourceMode Mode, nint Handle);
}
