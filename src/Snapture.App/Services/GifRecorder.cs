using System.Drawing;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Captures the foreground window (or a fixed virtual region) on a fixed cadence and encodes
/// every frame into an animated GIF on stop. Encoding happens in-memory while recording is
/// active to keep the on-disk footprint small until the user explicitly saves.
///
/// This is the v0.6 minimum-viable recorder. MP4 / HEVC / AV1 ship in v0.7 once Media
/// Foundation SinkWriter integration lands.
/// </summary>
public sealed class GifRecorder : IDisposable
{
    public enum RecordSource { ForegroundWindow, VirtualScreen }

    public bool IsRunning { get; private set; }
    public int FrameCount { get; private set; }
    public TimeSpan Elapsed => _started == DateTime.MinValue ? TimeSpan.Zero : DateTime.UtcNow - _started;
    public event Action<int, TimeSpan>? Progress;

    private readonly ICaptureEngine _engine;
    private readonly List<Bitmap> _frames = new();
    private CancellationTokenSource? _cts;
    private Task? _captureTask;
    private DateTime _started;
    private RecordSource _source;
    private nint _hwnd;
    private Rectangle _virtualRegion;
    private int _frameDelayMs = 100; // 10 fps

    public GifRecorder(ICaptureEngine engine) => _engine = engine;

    public void StartForegroundWindow(int targetFps = 10)
    {
        if (IsRunning) return;
        _hwnd = Native2.GetForegroundWindow();
        if (_hwnd == 0) throw new InvalidOperationException("No foreground window.");
        _source = RecordSource.ForegroundWindow;
        _frameDelayMs = Math.Max(40, 1000 / Math.Max(1, Math.Min(30, targetFps)));
        StartLoop();
    }

    public void StartVirtualScreen(int targetFps = 10)
    {
        if (IsRunning) return;
        _virtualRegion = MonitorEnumerator.GetVirtualScreen();
        _source = RecordSource.VirtualScreen;
        _frameDelayMs = Math.Max(40, 1000 / Math.Max(1, Math.Min(30, targetFps)));
        StartLoop();
    }

    private void StartLoop()
    {
        DisposeFrames();
        _cts = new CancellationTokenSource();
        _started = DateTime.UtcNow;
        FrameCount = 0;
        IsRunning = true;
        _captureTask = Task.Run(() => CaptureLoopAsync(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    public async Task StopAsync()
    {
        _cts?.Cancel();
        var task = _captureTask;
        if (task is not null)
        {
            try { await task.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                CaptureResult result;
                try
                {
                    result = _source == RecordSource.ForegroundWindow
                        ? await _engine.CaptureWindowAsync(_hwnd, ct).ConfigureAwait(false)
                        : await _engine.CaptureVirtualScreenAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch { continue; }

                lock (_frames) _frames.Add(result.Bitmap);
                FrameCount = _frames.Count;
                Progress?.Invoke(FrameCount, Elapsed);

                int wait = _frameDelayMs - (int)sw.ElapsedMilliseconds;
                if (wait > 0)
                {
                    try { await Task.Delay(wait, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Encode the recorded frames to an animated GIF at the given path. Idempotent.</summary>
    public void EncodeTo(string outputPath, int? overrideDelayMs = null)
    {
        Bitmap[] snapshot;
        lock (_frames) snapshot = _frames.ToArray();
        if (snapshot.Length == 0)
            throw new InvalidOperationException("No frames recorded.");

        int delay = overrideDelayMs ?? _frameDelayMs;
        GifEncoder.Encode(
            outputPath,
            snapshot.Select(bitmap => new GifFrameInput(bitmap, delay)),
            GifEncodingOptions.Default);
    }

    internal GifFrameEditor CreateFrameEditor()
    {
        Bitmap[] snapshot;
        lock (_frames)
            snapshot = _frames.Select(frame => new Bitmap(frame)).ToArray();
        return new GifFrameEditor(snapshot, _frameDelayMs, takeOwnership: true);
    }

    public void DisposeFrames()
    {
        lock (_frames)
        {
            foreach (var f in _frames) f.Dispose();
            _frames.Clear();
        }
        FrameCount = 0;
    }

    public void Dispose()
    {
        Stop();
        DisposeFrames();
    }
}
