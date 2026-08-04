using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Snapture.Capture;

namespace Snapture.App.Services;

public sealed record StepCaptureFrame(
    int Number,
    string FilePath,
    DateTime CapturedAtUtc,
    string? WindowTitle,
    string? ProcessName,
    IReadOnlyList<StepCaptureKeyStroke>? KeyEvents = null,
    IReadOnlyList<StepCaptureClick>? ClickEvents = null)
{
    public IReadOnlyList<StepCaptureKeyStroke> Keystrokes { get; } =
        KeyEvents ?? Array.Empty<StepCaptureKeyStroke>();

    public IReadOnlyList<StepCaptureClick> Clicks { get; } =
        ClickEvents ?? Array.Empty<StepCaptureClick>();
}

/// <summary>
/// Records every left-click anywhere on screen and snapshots the foreground window. Frames
/// are written to a session folder so memory stays bounded; the review window reads them
/// back lazily.
/// </summary>
public sealed class StepCaptureSession : IDisposable
{
    public string SessionFolder { get; }
    public IReadOnlyList<StepCaptureFrame> Frames => _frames;
    public bool IsRunning { get; private set; }
    public bool KeyboardTrackingAvailable => _keyboardTracker?.IsRunning == true;
    public bool PointerTrackingAvailable => _pointerTracker?.IsRunning == true;
    public event Action<StepCaptureFrame>? FrameAdded;

    private readonly ICaptureEngine _engine;
    private readonly List<StepCaptureFrame> _frames = new();
    private readonly object _inputLock = new();
    private readonly List<StepCaptureKeyStroke> _pendingKeystrokes = new();
    private readonly List<StepCaptureClick> _pendingClicks = new();
    private RecordingPointerTracker? _pointerTracker;
    private RecordingKeyboardTracker? _keyboardTracker;
    private DateTime _lastCaptureClick = DateTime.MinValue;
    private int _capturing;

    public StepCaptureSession(ICaptureEngine engine)
    {
        _engine = engine;
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        SessionFolder = Path.Combine(
            PortableMode.LocalDataDirectory, "step-sessions", stamp);
        Directory.CreateDirectory(SessionFolder);
    }

    public void Start()
    {
        if (IsRunning) return;

        lock (_inputLock)
        {
            _pendingKeystrokes.Clear();
            _pendingClicks.Clear();
        }

        _pointerTracker = new RecordingPointerTracker();
        _pointerTracker.PointerClicked += OnPointerClicked;
        _pointerTracker.Start();

        _keyboardTracker = new RecordingKeyboardTracker();
        _keyboardTracker.KeyPressed += OnKeyPressed;
        _keyboardTracker.Start();

        IsRunning = PointerTrackingAvailable;
        if (!IsRunning)
            Stop();
    }

    public void Stop()
    {
        if (_pointerTracker is not null)
        {
            _pointerTracker.PointerClicked -= OnPointerClicked;
            _pointerTracker.Dispose();
            _pointerTracker = null;
        }

        if (_keyboardTracker is not null)
        {
            _keyboardTracker.KeyPressed -= OnKeyPressed;
            _keyboardTracker.Dispose();
            _keyboardTracker = null;
        }

        IsRunning = false;
    }

    private void OnKeyPressed(RecordingKeyPress keyPress)
    {
        if (!IsRunning)
            return;

        lock (_inputLock)
        {
            _pendingKeystrokes.Add(new StepCaptureKeyStroke(keyPress.Text, keyPress.TimestampUtc));
            while (_pendingKeystrokes.Count > 256)
                _pendingKeystrokes.RemoveAt(0);
        }
    }

    private void OnPointerClicked(RecordingPointerClick click)
    {
        if (!IsRunning)
            return;

        // Debounce only the screenshot trigger. Non-left clicks remain in the track so the
        // exported workflow preserves the complete pointer story between captured steps.
        if (click.Button == RecordingPointerButton.Left
            && (click.TimestampUtc - _lastCaptureClick).TotalMilliseconds <= 250)
            return;

        lock (_inputLock)
        {
            _pendingClicks.Add(new StepCaptureClick(
                click.ScreenPoint.X,
                click.ScreenPoint.Y,
                click.Button switch
                {
                    RecordingPointerButton.Right => StepCaptureClickButton.Right,
                    RecordingPointerButton.Middle => StepCaptureClickButton.Middle,
                    _ => StepCaptureClickButton.Left
                },
                click.TimestampUtc));
            while (_pendingClicks.Count > 256)
                _pendingClicks.RemoveAt(0);
        }

        if (click.Button != RecordingPointerButton.Left)
            return;

        _lastCaptureClick = click.TimestampUtc;
        if (Interlocked.CompareExchange(ref _capturing, 1, 0) == 0)
            _ = CaptureFrameAsync();
    }

    private async Task CaptureFrameAsync()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == 0) return;

            // Resolve title + process for caption defaults
            var (proc, title) = CaptureHistoryService.DescribeForeground(hwnd);

            // Small delay so the click-action UI updates settle before we capture.
            await Task.Delay(120).ConfigureAwait(false);
            var result = await _engine.CaptureWindowAsync(hwnd).ConfigureAwait(false);
            try
            {
                int next = _frames.Count + 1;
                var path = Path.Combine(SessionFolder, $"step_{next:D3}.png");
                result.Bitmap.Save(path, ImageFormat.Png);
                var (keystrokes, clicks) = TakePendingInput();
                var frame = new StepCaptureFrame(next, path, DateTime.UtcNow, title, proc, keystrokes, clicks);
                _frames.Add(frame);
                FrameAdded?.Invoke(frame);
            }
            finally { result.Bitmap.Dispose(); }
        }
        catch { /* swallow — step-capture must never crash the app */ }
        finally { Interlocked.Exchange(ref _capturing, 0); }
    }

    private (IReadOnlyList<StepCaptureKeyStroke> Keystrokes, IReadOnlyList<StepCaptureClick> Clicks)
        TakePendingInput()
    {
        lock (_inputLock)
        {
            var keystrokes = _pendingKeystrokes.ToArray();
            var clicks = _pendingClicks.ToArray();
            _pendingKeystrokes.Clear();
            _pendingClicks.Clear();
            return (keystrokes, clicks);
        }
    }

    public void Dispose() => Stop();

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}

/// <summary>Markdown / HTML exporter for a finished session.</summary>
public static class StepCaptureExporter
{
    public sealed record StepEntry(
        int Number,
        string FilePath,
        string Caption,
        IReadOnlyList<StepCaptureKeyStroke>? Keystrokes = null,
        IReadOnlyList<StepCaptureClick>? Clicks = null);

    public static string ExportMarkdown(string outDir, string title, IEnumerable<StepEntry> entries)
    {
        Directory.CreateDirectory(outDir);
        var imgsDir = Path.Combine(outDir, "images");
        Directory.CreateDirectory(imgsDir);
        var sw = new System.Text.StringBuilder();
        sw.AppendLine($"# {title}");
        sw.AppendLine();
        sw.AppendLine($"_Generated by Snapture · {DateTime.Now:yyyy-MM-dd HH:mm}_");
        sw.AppendLine();
        int idx = 0;
        foreach (var e in entries)
        {
            idx++;
            string imgName = $"step_{idx:D3}{Path.GetExtension(e.FilePath)}";
            string copied = Path.Combine(imgsDir, imgName);
            try { File.Copy(e.FilePath, copied, overwrite: true); } catch { }
            sw.AppendLine($"## Step {idx}");
            sw.AppendLine();
            if (!string.IsNullOrWhiteSpace(e.Caption))
            {
                sw.AppendLine(e.Caption.Trim());
                sw.AppendLine();
            }
            var inputTrack = StepCaptureInputFormatter.FormatMarkdown(e.Keystrokes, e.Clicks);
            if (inputTrack is not null)
            {
                sw.AppendLine(inputTrack);
                sw.AppendLine();
            }
            sw.AppendLine($"![Step {idx}](images/{imgName})");
            sw.AppendLine();
        }
        var mdPath = Path.Combine(outDir, "steps.md");
        File.WriteAllText(mdPath, sw.ToString());
        return mdPath;
    }
}
