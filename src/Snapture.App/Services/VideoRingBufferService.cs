using System.IO;
using Serilog;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Keeps a bounded rolling MP4 on disk and renders a requested recent range into a
/// user-selected file. The working file is private temp state; it is never presented
/// as a completed recording and is deleted after rotation or shutdown.
/// </summary>
internal sealed class VideoRingBufferService : IDisposable
{
    private const int MaximumBufferSeconds = 90;
    private static readonly string BufferDirectory = Path.Combine(Path.GetTempPath(), "Snapture", "ring-buffer");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Timer? _maintenance;
    private VideoRecorder? _recorder;
    private string? _currentPath;
    private RingSource? _source;
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
    public TimeSpan BufferedDuration => _recorder?.Elapsed ?? TimeSpan.Zero;
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
            StopCurrentLocked(deleteFile: true);
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
        string? sourcePath = null;
        RingSource? source = null;
        try
        {
            ThrowIfDisposed();
            if (!IsRunning || _recorder is null || _currentPath is null || _source is null)
                throw new InvalidOperationException("The ring buffer is not running.");

            sourcePath = _currentPath;
            source = _source;
            StopCurrentLocked(deleteFile: false);

            var duration = await VideoSegmentService.GetDurationAsync(sourcePath);
            var start = SelectRecentStart(duration, requestedDuration);
            await VideoSegmentService.TrimAsync(sourcePath, outputPath, start, duration, cancellationToken);
            TryDelete(sourcePath);

            StartCoreLocked(source.Value, _fps, _bitrateMbps, _outputWidth, _outputHeight, _autoTighten, _toneMapOperator, _hdrColorCorrection);
            Status = $"Ring buffer saved last {requestedDuration.TotalSeconds:0}s";
            RaiseStateChanged();
            return outputPath;
        }
        catch
        {
            if (sourcePath is not null)
                TryDelete(sourcePath);

            if (source is { } restartSource)
            {
                try
                {
                    StartCoreLocked(restartSource, _fps, _bitrateMbps, _outputWidth, _outputHeight, _autoTighten, _toneMapOperator, _hdrColorCorrection);
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
            StartCoreLocked(source, fps, bitrateMbps, outputWidth, outputHeight, autoTighten, toneMapOperator, hdrColorCorrection);
            _maintenance = new Timer(MaintainBuffer, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }
        catch
        {
            StopCurrentLocked(deleteFile: true);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void StartCoreLocked(
        RingSource source,
        int fps,
        int bitrateMbps,
        int outputWidth,
        int outputHeight,
        bool autoTighten,
        HdrToneMapOperator toneMapOperator,
        bool hdrColorCorrection)
    {
        Directory.CreateDirectory(BufferDirectory);
        string path = Path.Combine(BufferDirectory, $"active-{Guid.NewGuid():N}.mp4");
        var recorder = new VideoRecorder(
            new RecordingAudioOptions { IncludeSystemAudio = false },
            autoTightenEnabled: autoTighten,
            toneMapOperator: toneMapOperator,
            hdrColorCorrection: hdrColorCorrection);
        try
        {
            if (source.Mode == RingSourceMode.Window)
                recorder.StartWindow(source.Handle, path, fps, bitrateMbps, outputWidth, outputHeight);
            else
                recorder.StartMonitor(source.Handle, path, fps, bitrateMbps, outputWidth, outputHeight);

            _recorder = recorder;
            _currentPath = path;
            _source = source;
            _running = true;
            Status = "Ring buffer recording";
            RaiseStateChanged();
            Log.Information("VideoRingBuffer.Started {Source} {MaxSeconds}s", source.Mode, MaximumBufferSeconds);
        }
        catch
        {
            recorder.Dispose();
            TryDelete(path);
            throw;
        }
    }

    private async void MaintainBuffer(object? state)
    {
        if (_disposed || !IsRunning || BufferedDuration < TimeSpan.FromSeconds(MaximumBufferSeconds))
            return;

        bool acquired = false;
        try
        {
            await _gate.WaitAsync();
            acquired = true;
            if (_disposed || !IsRunning || _source is not { } source)
                return;

            StopCurrentLocked(deleteFile: true);
            StartCoreLocked(source, _fps, _bitrateMbps, _outputWidth, _outputHeight, _autoTighten, _toneMapOperator, _hdrColorCorrection);
        }
        catch (Exception ex)
        {
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

    private void StopCurrentLocked(bool deleteFile)
    {
        var recorder = _recorder;
        var path = _currentPath;
        _recorder = null;
        _currentPath = null;
        _running = false;

        if (recorder is not null)
        {
            try { recorder.Stop(); }
            catch (Exception ex) { Log.Debug(ex, "VideoRingBuffer.StopFailed"); }
            recorder.Dispose();
        }

        if (deleteFile && path is not null)
            TryDelete(path);
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
