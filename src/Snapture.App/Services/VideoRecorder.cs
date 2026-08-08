using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;
using Snapture.Capture;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

namespace Snapture.App.Services;

/// <summary>
/// Records video from a WGC capture session to an MP4 file via Media Foundation SinkWriter.
/// Continuous WGC frames flow through a queue-depth-3 frame pool; each frame is written
/// as a BGRA sample to the SinkWriter. Codec selection is discovered at runtime:
/// hardware AV1, then HEVC, then H.264, with software AV1 intentionally skipped.
///
/// SystemRelativeTime from WGC frames maps directly to presentation timestamps — both
/// use 100-nanosecond units, no QPC conversion needed.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class VideoRecorder : IDisposable
{
    public enum RecordSource { ForegroundWindow, Monitor, VirtualScreen }

    public VideoRecorder(
        RecordingAudioOptions? audioOptions = null,
        bool autoTightenEnabled = false,
        HdrToneMapOperator toneMapOperator = HdrToneMapOperator.Reinhard,
        bool hdrColorCorrection = true)
    {
        _audioOptions = (audioOptions ?? new RecordingAudioOptions()).Clone();
        _autoTightenEnabled = autoTightenEnabled;
        _toneMapOperator = toneMapOperator;
        _hdrColorCorrection = hdrColorCorrection;
    }

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public int FrameCount { get; private set; }
    public int SkippedCleanFrameCount => _dirtyRegionFilter.SkippedFrameCount;
    public TimeSpan Elapsed => _sw.Elapsed;
    public string SelectedCodecName { get; private set; } = "H.264";
    public string SelectedCodecDescription { get; private set; } = "H.264";
    public string CodecAvailabilityDescription { get; private set; } = "Codec extension detection pending.";
    public string DirtyRegionDescription => _dirtyRegionFilter.ReportingEnabled
        ? "dirty-region skip enabled"
        : "dirty-region skip unavailable";
    public string ContainerDescription { get; private set; } = "fragmented MP4";
    public bool HasAudioStream => _audioStreamEnabled;
    public bool IsSystemAudioEnabled => _audioCapture?.IsSystemAudioEnabled ?? _audioOptions.IncludeSystemAudio;
    public bool IsMicrophoneEnabled => _audioCapture?.IsMicrophoneEnabled ?? _audioOptions.IncludeMicrophone;
    public bool CanUseAppAudio => _audioOptions.TargetProcessId > 0 && HasAudioStream;
    public bool IsAppAudioOnly => _audioCapture?.IsTargetProcessAudioEnabled ?? _audioOptions.UseTargetProcessAudio;
    public bool IsZoomSuggestionsEnabled => _zoomSuggestionsEnabled;
    public int ZoomSuggestionClickCount => _zoomSuggestions.ClickCount;
    public bool IsAutoTightenEnabled => _autoTightenEnabled;
    public float SystemAudioLevel => _audioCapture?.SystemLevel ?? 0f;
    public float MicrophoneLevel => _audioCapture?.MicrophoneLevel ?? 0f;
    public string AudioDescription { get; private set; } = "AAC audio pending";
    public string ZoomSuggestionsDescription => _zoomSuggestionsEnabled
        ? $"auto-zoom hints: {ZoomSuggestionClickCount}"
        : "auto-zoom hints off";
    public string AutoTightenDescription => !_autoTightenEnabled
        ? "auto-tighten off"
        : _autoTightenPlan?.Description ?? "auto-tighten pending";
    public bool IsFp16CaptureEnabled => _capturePixelFormat.UsesFp16;
    public string CapturePixelFormatDescription => _capturePixelFormat.Description;
    public string ToneMapOperatorDescription => HdrToneMapOperators.DisplayName(_toneMapOperator);
    public string HdrColorCorrectionDescription => _hdrColorCorrection
        ? "HDR color correction on"
        : "HDR color correction off (highlight clamp)";
    public string OutputResolutionDescription => _outputWidth == _width && _outputHeight == _height
        ? $"{_outputWidth}x{_outputHeight}"
        : $"{_width}x{_height}→{_outputWidth}x{_outputHeight}";
    public bool IsSourceClosed => _sourceClosedFlag;
    public event Action? SourceClosed;
    public event Action<string>? EnvironmentChanged;
    public event Action<int, TimeSpan>? Progress;

    private readonly Stopwatch _sw = new();
    private readonly object _lock = new();
    private readonly DirtyRegionFrameFilter _dirtyRegionFilter = new();
    private readonly RecordingAudioOptions _audioOptions;
    private readonly RecordingZoomSuggestionEngine _zoomSuggestions = new();
    private MFInterop.IMFSinkWriter? _writer;
    private uint _videoStreamIndex;
    private uint _audioStreamIndex;
    private bool _audioStreamEnabled;
    private RecordingAudioMixer? _audioCapture;
    private RecordingPointerTracker? _pointerTracker;
    private RecordingKeyboardTracker? _keyboardTracker;
    private int _sourceWidth, _sourceHeight;
    private int _width, _height;
    private Rectangle _captureBounds = Rectangle.Empty;
    private long _frameDurationHns;
    private bool _zoomSuggestionsEnabled = true;
    private bool _autoTightenEnabled;
    private RecordingAutoTightenPlan? _autoTightenPlan;
    private Rectangle _sourceCrop;
    private CapturePixelFormatDecision _capturePixelFormat = CapturePixelFormatDecision.Sdr;
    private readonly HdrToneMapOperator _toneMapOperator;
    private readonly bool _hdrColorCorrection;

    // WGC continuous capture
    private nint _d3dDevice;
    private nint _d3dContext;
    private IDirect3DDevice? _direct3DDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _captureItem;
    private long _firstTimestamp = -1;
    private long _pauseOffsetTicks;
    private long _pauseStartTicks;
    private volatile bool _sourceClosedFlag;

    private string? _outputPath;
    private bool _disposed;
    private bool _mfStarted;

    private const ulong Mp4FragmentDurationHns = 20_000_000; // two seconds
    private const int AudioBitrate = 128_000;
    private const uint FrontLeftRightChannelMask = 0x3;

    /// <summary>
    /// Start recording the foreground window to the given output path.
    /// </summary>
    public void StartWindow(nint hwnd, string outputPath, int fps = 30, int bitrateMbps = 8, int outputWidth = 0, int outputHeight = 0)
    {
        if (IsRecording) return;
        EnsureDevice();
        _audioOptions.TargetProcessId = GetWindowProcessId(hwnd);
        var item = CaptureItemFactory.CreateForWindow(hwnd)
            ?? throw new InvalidOperationException("CreateForWindow returned null.");
        _captureBounds = ResolveWindowBounds(hwnd, item.Size.Width, item.Size.Height);
        _autoTightenPlan = _autoTightenEnabled
            ? RecordingAutoTightenEngine.DetectWindow(hwnd, _captureBounds)
            : null;
        StartInternal(item, outputPath, fps, bitrateMbps, outputWidth, outputHeight,
            ResolveMonitorHandle(_captureBounds));
    }

    /// <summary>
    /// Start recording a specific monitor to the given output path.
    /// </summary>
    public void StartMonitor(nint hMonitor, string outputPath, int fps = 30, int bitrateMbps = 8, int outputWidth = 0, int outputHeight = 0)
    {
        if (IsRecording) return;
        EnsureDevice();
        _audioOptions.TargetProcessId = 0;
        _audioOptions.UseTargetProcessAudio = false;
        var item = CaptureItemFactory.CreateForMonitor(hMonitor)
            ?? throw new InvalidOperationException("CreateForMonitor returned null.");
        _captureBounds = ResolveMonitorBounds(hMonitor, item.Size.Width, item.Size.Height);
        _autoTightenPlan = _autoTightenEnabled
            ? RecordingAutoTightenEngine.DetectMonitor(_captureBounds)
            : null;
        StartInternal(item, outputPath, fps, bitrateMbps, outputWidth, outputHeight, hMonitor);
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        IsPaused = true;
        _pauseStartTicks = _sw.ElapsedTicks;
        _audioCapture?.SetPaused(true);
        _pointerTracker?.ClearClicks();
        _keyboardTracker?.Clear();
        _sw.Stop();
        Log.Information("VideoRecorder.Paused");
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        _pauseOffsetTicks += _sw.ElapsedTicks - _pauseStartTicks;
        IsPaused = false;
        _dirtyRegionFilter.ForceNextFrame();
        _audioCapture?.SetPaused(false);
        _pointerTracker?.ClearClicks();
        _keyboardTracker?.Clear();
        _sw.Start();
        Log.Information("VideoRecorder.Resumed");
    }

    /// <summary>
    /// Stop recording and finalize the MP4 file.
    /// </summary>
    public string? Stop()
    {
        if (!IsRecording) return null;
        IsRecording = false;
        _sw.Stop();

        Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_captureItem is not null)
        {
            _captureItem.Closed -= OnCaptureItemClosed;
            _captureItem = null;
        }

        _session?.Dispose();
        _session = null;
        _framePool?.Dispose();
        _framePool = null;
        if (_audioCapture is not null)
        {
            _audioCapture.SourceChanged -= OnAudioSourceChanged;
            _audioCapture.Dispose();
            _audioCapture = null;
        }
        StopPointerTracking();
        StopKeyboardTracking();

        if (_writer is not null)
        {
            try { _writer.Finalize_(); }
            catch (Exception ex) { Log.Error(ex, "VideoRecorder.Finalize.Failed"); }
            Marshal.ReleaseComObject(_writer);
            _writer = null;
        }

        if (_mfStarted)
        {
            MFInterop.MFShutdown();
            _mfStarted = false;
        }

        Log.Information("VideoRecorder.Stopped {Frames} {SkippedCleanFrames} {Duration} Audio={Audio}",
            FrameCount, SkippedCleanFrameCount, Elapsed, _audioStreamEnabled);
        return _outputPath;
    }

    public bool SetSystemAudioEnabled(bool enabled)
    {
        _audioOptions.IncludeSystemAudio = enabled;
        if (_audioCapture is null) return !enabled;
        bool applied = _audioCapture.SetSystemAudioEnabled(enabled);
        if (!applied) _audioOptions.IncludeSystemAudio = false;
        AudioDescription = _audioCapture.Description;
        return applied;
    }

    public bool SetMicrophoneEnabled(bool enabled)
    {
        _audioOptions.IncludeMicrophone = enabled;
        if (_audioCapture is null) return !enabled;
        bool applied = _audioCapture.SetMicrophoneEnabled(enabled);
        if (!applied) _audioOptions.IncludeMicrophone = false;
        AudioDescription = _audioCapture.Description;
        return applied;
    }

    public bool SetAppAudioOnly(bool enabled)
    {
        if (_audioOptions.TargetProcessId <= 0)
        {
            _audioOptions.UseTargetProcessAudio = false;
            return !enabled;
        }

        _audioOptions.UseTargetProcessAudio = enabled;
        if (_audioCapture is null) return true;
        bool applied = _audioCapture.SetTargetProcessAudioEnabled(enabled);
        if (!applied) _audioOptions.UseTargetProcessAudio = !enabled;
        AudioDescription = _audioCapture.Description;
        return applied;
    }

    public void SetZoomSuggestionsEnabled(bool enabled)
        => _zoomSuggestionsEnabled = enabled;

    public bool SetAutoTightenEnabled(bool enabled)
    {
        if (IsRecording)
            return false;

        _autoTightenEnabled = enabled;
        return true;
    }

    public string? ExportZoomSuggestions(string videoPath)
    {
        return _zoomSuggestionsEnabled
            ? _zoomSuggestions.ExportSidecar(videoPath, _width, _height)
            : null;
    }

    private int _outputWidth, _outputHeight;

    private void StartInternal(
        GraphicsCaptureItem item,
        string outputPath,
        int fps,
        int bitrateMbps,
        int outputWidth = 0,
        int outputHeight = 0,
        nint targetMonitor = 0)
    {
        _outputPath = outputPath;
        _capturePixelFormat = CapturePixelFormatPolicy.ResolveForMonitor(targetMonitor);
        _sourceWidth = item.Size.Width;
        _sourceHeight = item.Size.Height;
        _sourceCrop = ResolveSourceCrop(_autoTightenPlan, _captureBounds, _sourceWidth, _sourceHeight);
        _width = _sourceCrop.Width;
        _height = _sourceCrop.Height;

        // Ensure width/height are even for MP4 encoders. The WGC source texture keeps
        // its native size; WriteFrame crops the final row/column if needed.
        if (_width % 2 != 0) _width--;
        if (_height % 2 != 0) _height--;
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("Capture item has invalid dimensions.");
        if (_captureBounds.IsEmpty)
            _captureBounds = new Rectangle(0, 0, _width, _height);

        // Output resolution: if a preset target is specified, use it (encoder scales).
        // Otherwise output at native capture dimensions.
        _outputWidth = outputWidth > 0 ? outputWidth : _width;
        _outputHeight = outputHeight > 0 ? outputHeight : _height;
        if (_outputWidth % 2 != 0) _outputWidth--;
        if (_outputHeight % 2 != 0) _outputHeight--;

        _frameDurationHns = 10_000_000L / Math.Max(1, fps);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

        // Initialize Media Foundation
        int hr = MFInterop.MFStartup(MFInterop.MF_VERSION, 0);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        _mfStarted = true;

        try
        {
            ConfigureSinkWriter(outputPath, fps, bitrateMbps);
        }
        catch
        {
            MFInterop.MFShutdown();
            _mfStarted = false;
            throw;
        }

        // Set up continuous WGC capture with queue depth 3
        _framePool = CreateFramePool(item);

        _framePool.FrameArrived += OnFrameArrived;

        _captureItem = item;
        _captureItem.Closed += OnCaptureItemClosed;
        _sourceClosedFlag = false;

        _session = _framePool.CreateCaptureSession(item);
        TrySetBorderRequired(_session, false);
        TrySetCursorCapture(_session, true);
        _dirtyRegionFilter.Reset(TrySetDirtyRegionReporting(_session));

        FrameCount = 0;
        _firstTimestamp = -1;
        _pauseOffsetTicks = 0;
        _zoomSuggestions.Clear();
        IsPaused = false;
        IsRecording = true;
        _sw.Restart();
        StartAudioCapture();
        StartPointerTracking();
        StartKeyboardTracking();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _session.StartCapture();
        Log.Information("VideoRecorder.Started {Width}x{Height}→{OutWidth}x{OutHeight} {Fps}fps {Bitrate}Mbps PixelFormat={PixelFormat} DirtyRegions={DirtyRegions} Audio={Audio} AutoTighten={AutoTighten}",
            _width, _height, _outputWidth, _outputHeight, fps, bitrateMbps, _capturePixelFormat.Description, _dirtyRegionFilter.ReportingEnabled, _audioStreamEnabled, AutoTightenDescription);
    }

    private Direct3D11CaptureFramePool CreateFramePool(GraphicsCaptureItem item)
    {
        try
        {
            return Direct3D11CaptureFramePool.CreateFreeThreaded(
                _direct3DDevice!, _capturePixelFormat.WinRtPixelFormat, 3, item.Size);
        }
        catch when (_capturePixelFormat.UsesFp16)
        {
            Log.Warning("VideoRecorder.Fp16FramePoolUnavailable — falling back to BGRA8");
            _capturePixelFormat = CapturePixelFormatDecision.Sdr;
            return Direct3D11CaptureFramePool.CreateFreeThreaded(
                _direct3DDevice!, _capturePixelFormat.WinRtPixelFormat, 3, item.Size);
        }
    }

    private void ConfigureSinkWriter(string outputPath, int fps, int bitrateMbps)
    {
        var discovery = MediaFoundationVideoCodecDiscovery.Discover();
        var candidates = discovery.Candidates;
        CodecAvailabilityDescription = discovery.Availability.Description;
        if (candidates.Count == 0)
            throw new InvalidOperationException(discovery.Availability.NoEncoderDescription);

        Exception? lastError = null;
        foreach (bool includeAudio in new[] { true, false })
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    ConfigureSinkWriter(outputPath, fps, bitrateMbps, candidate, includeAudio);
                    SelectedCodecName = candidate.CodecName;
                    SelectedCodecDescription = candidate.EncoderSummary;
                    Log.Information("VideoRecorder.CodecSelected {Codec} {Encoder} Audio={Audio}",
                        candidate.CodecName, candidate.EncoderSummary, includeAudio && _audioStreamEnabled);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Log.Warning(ex, "VideoRecorder.CodecConfigureFailed {Codec} {Encoder} Audio={Audio}",
                        candidate.CodecName, candidate.EncoderSummary, includeAudio);
                    ReleaseWriter();
                    TryDeletePartialFile(outputPath);
                }
            }

            if (includeAudio)
            {
                AudioDescription = "audio unavailable";
                Log.Warning("VideoRecorder.Audio.ConfigureFallbackToVideoOnly");
            }
        }

        throw new InvalidOperationException("No Media Foundation video encoder could be configured.", lastError);
    }

    private void ConfigureSinkWriter(
        string outputPath,
        int fps,
        int bitrateMbps,
        MediaFoundationVideoCodecDiscovery.EncoderCandidate candidate,
        bool includeAudio)
    {
        // Create attributes for the sink writer: enable HW transforms and fragmented MP4.
        int hr = MFInterop.MFCreateAttributes(out var writerAttrs, 4);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        MFInterop.IMFMediaType? outputType = null;
        MFInterop.IMFMediaType? inputType = null;
        try
        {
            var hwKey = MFInterop.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS;
            writerAttrs.SetUINT32(ref hwKey, candidate.UseHardwareTransforms ? 1u : 0u);

            var throttleKey = MFInterop.MF_SINK_WRITER_DISABLE_THROTTLING;
            writerAttrs.SetUINT32(ref throttleKey, 1);

            var containerKey = MFInterop.MF_TRANSCODE_CONTAINERTYPE;
            var fragmentedMp4Container = MFInterop.MFTranscodeContainerType_FMPEG4;
            writerAttrs.SetGUID(ref containerKey, ref fragmentedMp4Container);

            var fragmentDurationKey = MFInterop.MF_MPEG4SINK_MIN_FRAGMENT_DURATION;
            writerAttrs.SetUINT64(ref fragmentDurationKey, Mp4FragmentDurationHns);
            ContainerDescription = "fragmented MP4 (2s fragments)";

            hr = MFInterop.MFCreateSinkWriterFromURL(outputPath, 0, writerAttrs, out _writer!);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            hr = MFInterop.MFCreateMediaType(out outputType);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            var majorTypeKey = MFInterop.MF_MT_MAJOR_TYPE;
            var videoType = MFInterop.MFMediaType_Video;
            outputType.SetGUID(ref majorTypeKey, ref videoType);

            var subtypeKey = MFInterop.MF_MT_SUBTYPE;
            var subtype = candidate.Subtype;
            outputType.SetGUID(ref subtypeKey, ref subtype);

            var bitrateKey = MFInterop.MF_MT_AVG_BITRATE;
            outputType.SetUINT32(ref bitrateKey, (uint)(bitrateMbps * 1_000_000));

            var interlaceKey = MFInterop.MF_MT_INTERLACE_MODE;
            outputType.SetUINT32(ref interlaceKey, MFInterop.MFVideoInterlace_Progressive);

            var frameSizeKey = MFInterop.MF_MT_FRAME_SIZE;
            outputType.SetUINT64(ref frameSizeKey, MFInterop.Pack2x32((uint)_outputWidth, (uint)_outputHeight));

            var frameRateKey = MFInterop.MF_MT_FRAME_RATE;
            outputType.SetUINT64(ref frameRateKey, MFInterop.Pack2x32((uint)fps, 1));

            var parKey = MFInterop.MF_MT_PIXEL_ASPECT_RATIO;
            outputType.SetUINT64(ref parKey, MFInterop.Pack2x32(1, 1));

            _writer.AddStream(outputType, out _videoStreamIndex);

            hr = MFInterop.MFCreateMediaType(out inputType);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            inputType.SetGUID(ref majorTypeKey, ref videoType);

            var argb32 = MFInterop.MFVideoFormat_ARGB32;
            inputType.SetGUID(ref subtypeKey, ref argb32);

            inputType.SetUINT32(ref interlaceKey, MFInterop.MFVideoInterlace_Progressive);
            inputType.SetUINT64(ref frameSizeKey, MFInterop.Pack2x32((uint)_width, (uint)_height));
            inputType.SetUINT64(ref frameRateKey, MFInterop.Pack2x32((uint)fps, 1));
            inputType.SetUINT64(ref parKey, MFInterop.Pack2x32(1, 1));

            _writer.SetInputMediaType(_videoStreamIndex, inputType, null);
            _audioStreamEnabled = false;
            if (includeAudio)
                ConfigureAudioStream();

            _writer.BeginWriting();
        }
        finally
        {
            if (inputType is not null) Marshal.ReleaseComObject(inputType);
            if (outputType is not null) Marshal.ReleaseComObject(outputType);
            Marshal.ReleaseComObject(writerAttrs);
        }
    }

    private void ConfigureAudioStream()
    {
        MFInterop.IMFMediaType? outputType = null;
        MFInterop.IMFMediaType? inputType = null;
        try
        {
            int hr = MFInterop.MFCreateMediaType(out outputType);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            var majorTypeKey = MFInterop.MF_MT_MAJOR_TYPE;
            var audioType = MFInterop.MFMediaType_Audio;
            outputType.SetGUID(ref majorTypeKey, ref audioType);

            var subtypeKey = MFInterop.MF_MT_SUBTYPE;
            var aac = MFInterop.MFAudioFormat_AAC;
            outputType.SetGUID(ref subtypeKey, ref aac);

            var channelsKey = MFInterop.MF_MT_AUDIO_NUM_CHANNELS;
            outputType.SetUINT32(ref channelsKey, PcmAudioConverter.OutputChannels);

            var sampleRateKey = MFInterop.MF_MT_AUDIO_SAMPLES_PER_SECOND;
            outputType.SetUINT32(ref sampleRateKey, PcmAudioConverter.OutputSampleRate);

            var bitsKey = MFInterop.MF_MT_AUDIO_BITS_PER_SAMPLE;
            outputType.SetUINT32(ref bitsKey, PcmAudioConverter.OutputBitsPerSample);

            var avgBytesKey = MFInterop.MF_MT_AUDIO_AVG_BYTES_PER_SECOND;
            outputType.SetUINT32(ref avgBytesKey, AudioBitrate / 8);

            var blockAlignKey = MFInterop.MF_MT_AUDIO_BLOCK_ALIGNMENT;
            outputType.SetUINT32(ref blockAlignKey, 1);

            var channelMaskKey = MFInterop.MF_MT_AUDIO_CHANNEL_MASK;
            outputType.SetUINT32(ref channelMaskKey, FrontLeftRightChannelMask);

            var payloadTypeKey = MFInterop.MF_MT_AAC_PAYLOAD_TYPE;
            outputType.SetUINT32(ref payloadTypeKey, 0);

            var independentKey = MFInterop.MF_MT_ALL_SAMPLES_INDEPENDENT;
            outputType.SetUINT32(ref independentKey, 1);

            _writer!.AddStream(outputType, out _audioStreamIndex);

            hr = MFInterop.MFCreateMediaType(out inputType);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            inputType.SetGUID(ref majorTypeKey, ref audioType);

            var pcm = MFInterop.MFAudioFormat_PCM;
            inputType.SetGUID(ref subtypeKey, ref pcm);
            inputType.SetUINT32(ref channelsKey, PcmAudioConverter.OutputChannels);
            inputType.SetUINT32(ref sampleRateKey, PcmAudioConverter.OutputSampleRate);
            inputType.SetUINT32(ref bitsKey, PcmAudioConverter.OutputBitsPerSample);
            inputType.SetUINT32(ref avgBytesKey, PcmAudioConverter.OutputAverageBytesPerSecond);
            inputType.SetUINT32(ref blockAlignKey, PcmAudioConverter.OutputBlockAlign);
            inputType.SetUINT32(ref channelMaskKey, FrontLeftRightChannelMask);
            inputType.SetUINT32(ref independentKey, 1);

            _writer.SetInputMediaType(_audioStreamIndex, inputType, null);
            _audioStreamEnabled = true;
            AudioDescription = "AAC audio armed";
        }
        finally
        {
            if (inputType is not null) Marshal.ReleaseComObject(inputType);
            if (outputType is not null) Marshal.ReleaseComObject(outputType);
        }
    }

    private void StartAudioCapture()
    {
        if (!_audioStreamEnabled)
        {
            AudioDescription = "audio unavailable";
            return;
        }

        try
        {
            _audioCapture = new RecordingAudioMixer(_audioOptions, WriteAudioSample);
            _audioCapture.SourceChanged += OnAudioSourceChanged;
            _audioCapture.Start();
            AudioDescription = _audioCapture.Description;
        }
        catch (Exception ex)
        {
            _audioStreamEnabled = false;
            AudioDescription = "audio unavailable";
            Log.Warning(ex, "VideoRecorder.Audio.StartFailed");
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!IsRecording || IsPaused || _sourceClosedFlag) return;

        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        if (frame.ContentSize.Width <= 0 || frame.ContentSize.Height <= 0) return;

        long timestamp = frame.SystemRelativeTime.Ticks;

        if (_firstTimestamp < 0)
            _firstTimestamp = timestamp;

        long pts = (timestamp - _firstTimestamp) - _pauseOffsetTicks;
        if (pts < 0) pts = 0;

        int? dirtyRegionCount = DirtyRegionInterop.TryGetDirtyRegionCount(frame);
        DateTime nowUtc = DateTime.UtcNow;
        RecordingPointerFrame? pointerFrame = _pointerTracker?.CaptureFrame(_captureBounds, nowUtc);
        RecordingKeystrokeFrame? keystrokeFrame = _keyboardTracker?.CaptureFrame(nowUtc);
        if (_zoomSuggestionsEnabled && pointerFrame?.CursorPosition is { } cursor)
            _zoomSuggestions.AddCursorSample(TimeSpan.FromTicks(pts), cursor);
        bool overlayActive = (pointerFrame?.HasVisualActivity ?? false)
            || (keystrokeFrame?.HasVisualActivity ?? false);

        if (!_dirtyRegionFilter.ShouldEncode(dirtyRegionCount) && !overlayActive)
        {
            Progress?.Invoke(FrameCount, Elapsed);
            return;
        }

        try
        {
            WriteFrame(frame, pts, CropPointerFrame(pointerFrame), keystrokeFrame);
            FrameCount++;
            Progress?.Invoke(FrameCount, Elapsed);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "VideoRecorder.WriteFrame.Failed");
        }
    }

    private unsafe void WriteFrame(
        Direct3D11CaptureFrame frame,
        long pts,
        RecordingPointerFrame? pointerFrame,
        RecordingKeystrokeFrame? keystrokeFrame)
    {
        // Get ID3D11Texture2D from the frame surface
        nint surfacePtr = Marshal.GetIUnknownForObject(frame.Surface);
        nint texPtr;
        try
        {
            var iidAccess = typeof(D3D11Interop.IDirect3DDxgiInterfaceAccess).GUID;
            int hrQI = Marshal.QueryInterface(surfacePtr, in iidAccess, out nint accessPtr);
            if (hrQI < 0) Marshal.ThrowExceptionForHR(hrQI);
            try
            {
                var access = (D3D11Interop.IDirect3DDxgiInterfaceAccess)
                    Marshal.GetObjectForIUnknown(accessPtr);
                var iidTex = D3D11Interop.IID_ID3D11Texture2D;
                texPtr = access.GetInterface(ref iidTex);
            }
            finally { Marshal.Release(accessPtr); }
        }
        finally { Marshal.Release(surfacePtr); }

        try
        {
            // Create staging texture, copy GPU → CPU
            var desc = new D3D11Interop.D3D11_TEXTURE2D_DESC
            {
                Width = (uint)_sourceWidth,
                Height = (uint)_sourceHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = (uint)_capturePixelFormat.DxgiFormat,
                SampleDesc = new D3D11Interop.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                Usage = D3D11Interop.D3D11_USAGE_STAGING,
                BindFlags = 0,
                CPUAccessFlags = D3D11Interop.D3D11_CPU_ACCESS_READ,
                MiscFlags = 0
            };

            nint stagingTex = CreateTexture2D(_d3dDevice, ref desc);
            try
            {
                CopyResource(_d3dContext, stagingTex, texPtr);
                var mapped = MapResource(_d3dContext, stagingTex);
                try
                {
                    uint bufSize = (uint)(_width * _height * 4);
                    int hr = MFInterop.MFCreateMemoryBuffer(bufSize, out var mfBuffer);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                    mfBuffer.Lock(out nint bufPtr, out _, out _);
                    try
                    {
                        byte* dst = (byte*)bufPtr;
                        int rowBytes = _width * 4;
                        if (_capturePixelFormat.UsesFp16)
                        {
                            var source = new ReadOnlySpan<byte>(
                                (void*)mapped.pData,
                                checked((int)(mapped.RowPitch * (uint)_sourceHeight)));
                            var destination = new Span<byte>(dst, checked((int)bufSize));
                            HdrFrameConverter.ConvertRgba16FloatToBgra(
                                source, checked((int)mapped.RowPitch), destination, rowBytes,
                                _sourceCrop.X, _sourceCrop.Y, _width, _height,
                                _toneMapOperator, _hdrColorCorrection);
                        }
                        else
                        {
                            byte* src = (byte*)mapped.pData;
                            for (int y = 0; y < _height; y++)
                            {
                                Buffer.MemoryCopy(
                                    src + ((_sourceCrop.Y + y) * (long)mapped.RowPitch) + (_sourceCrop.X * 4L),
                                    dst + y * (long)rowBytes,
                                    rowBytes, rowBytes);
                            }
                        }

                        var pixels = new Span<byte>(dst, (int)bufSize);
                        if (pointerFrame is { } pointer)
                            CursorOverlayRenderer.RenderBgra(pixels, _width, _height, rowBytes, pointer);
                        if (keystrokeFrame is { } keystrokes)
                            KeystrokeOverlayRenderer.RenderBgra(pixels, _width, _height, rowBytes, keystrokes);
                    }
                    finally { mfBuffer.Unlock(); }

                    mfBuffer.SetCurrentLength(bufSize);

                    hr = MFInterop.MFCreateSample(out var sample);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                    sample.AddBuffer(mfBuffer);
                    sample.SetSampleTime(pts);
                    sample.SetSampleDuration(_frameDurationHns);

                    lock (_lock)
                    {
                        _writer?.WriteSample(_videoStreamIndex, sample);
                    }

                    Marshal.ReleaseComObject(sample);
                    Marshal.ReleaseComObject(mfBuffer);
                }
                finally { UnmapResource(_d3dContext, stagingTex); }
            }
            finally { Marshal.Release(stagingTex); }
        }
        finally { Marshal.Release(texPtr); }
    }

    private void WriteAudioSample(byte[] pcm, int byteCount, long sampleTime, long duration)
    {
        if (!IsRecording || IsPaused || !_audioStreamEnabled || _writer is null || byteCount <= 0)
            return;

        int hr = MFInterop.MFCreateMemoryBuffer((uint)byteCount, out var mfBuffer);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);

        MFInterop.IMFSample? sample = null;
        try
        {
            mfBuffer.Lock(out nint bufferPtr, out _, out _);
            try
            {
                Marshal.Copy(pcm, 0, bufferPtr, byteCount);
            }
            finally
            {
                mfBuffer.Unlock();
            }

            mfBuffer.SetCurrentLength((uint)byteCount);

            hr = MFInterop.MFCreateSample(out sample);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            sample.AddBuffer(mfBuffer);
            sample.SetSampleTime(sampleTime);
            sample.SetSampleDuration(duration);

            lock (_lock)
            {
                _writer?.WriteSample(_audioStreamIndex, sample);
            }
        }
        finally
        {
            if (sample is not null) Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(mfBuffer);
        }
    }

    private void ReleaseWriter()
    {
        if (_writer is null) return;
        Marshal.ReleaseComObject(_writer);
        _writer = null;
        _audioStreamEnabled = false;
    }

    private void StartPointerTracking()
    {
        StopPointerTracking();
        _pointerTracker = new RecordingPointerTracker();
        _pointerTracker.PointerClicked += OnPointerClicked;
        _pointerTracker.Start();
    }

    private void StopPointerTracking()
    {
        if (_pointerTracker is null) return;
        _pointerTracker.PointerClicked -= OnPointerClicked;
        _pointerTracker.Dispose();
        _pointerTracker = null;
    }

    private void StartKeyboardTracking()
    {
        StopKeyboardTracking();
        _keyboardTracker = new RecordingKeyboardTracker();
        _keyboardTracker.Start();
    }

    private void StopKeyboardTracking()
    {
        if (_keyboardTracker is null) return;
        _keyboardTracker.Dispose();
        _keyboardTracker = null;
    }

    private void OnPointerClicked(RecordingPointerClick click)
    {
        if (!IsRecording || IsPaused)
            return;

        var localPoint = RecordingPointerTracker.ToLocalPoint(click.ScreenPoint, _captureBounds);
        if (localPoint is null)
            return;

        if (_zoomSuggestionsEnabled)
            _zoomSuggestions.AddClick(_sw.Elapsed, localPoint.Value);

        _audioCapture?.PlayClick();
    }

    private RecordingPointerFrame? CropPointerFrame(RecordingPointerFrame? frame)
    {
        if (frame is not { } pointer)
            return null;

        System.Drawing.Point? Map(System.Drawing.Point point)
        {
            int x = point.X - _sourceCrop.Left;
            int y = point.Y - _sourceCrop.Top;
            return x >= 0 && y >= 0 && x < _width && y < _height
                ? new System.Drawing.Point(x, y)
                : null;
        }

        System.Drawing.Point? cursor = pointer.CursorPosition is { } cursorPoint ? Map(cursorPoint) : null;
        var clicks = pointer.Clicks
            .Select(click => Map(click.Position) is { } position
                ? click with { Position = position }
                : (RecordingPointerEffect?)null)
            .Where(click => click is not null)
            .Select(click => click!.Value)
            .ToList();

        return new RecordingPointerFrame(cursor, clicks, cursor is not null || clicks.Count > 0);
    }

    private static Rectangle ResolveSourceCrop(
        RecordingAutoTightenPlan? plan,
        Rectangle captureBounds,
        int sourceWidth,
        int sourceHeight)
    {
        var full = new Rectangle(0, 0, Math.Max(0, sourceWidth), Math.Max(0, sourceHeight));
        if (full.Width <= 0 || full.Height <= 0 || plan is null || !plan.IsApplied)
            return full;

        double scaleX = captureBounds.Width > 0 ? sourceWidth / (double)captureBounds.Width : 1.0;
        double scaleY = captureBounds.Height > 0 ? sourceHeight / (double)captureBounds.Height : 1.0;
        int left = (int)Math.Floor((plan.Crop.Left - captureBounds.Left) * scaleX);
        int top = (int)Math.Floor((plan.Crop.Top - captureBounds.Top) * scaleY);
        int right = (int)Math.Ceiling((plan.Crop.Right - captureBounds.Left) * scaleX);
        int bottom = (int)Math.Ceiling((plan.Crop.Bottom - captureBounds.Top) * scaleY);
        var crop = Rectangle.Intersect(full, Rectangle.FromLTRB(left, top, right, bottom));

        if (crop.Width < 2 || crop.Height < 2)
            return full;

        // H.264/HEVC/AV1 input types require even dimensions. Move the origin inward
        // only when necessary so the crop never samples outside the staging texture.
        if ((crop.X & 1) != 0) { crop.X++; crop.Width--; }
        if ((crop.Y & 1) != 0) { crop.Y++; crop.Height--; }
        if ((crop.Width & 1) != 0) crop.Width--;
        if ((crop.Height & 1) != 0) crop.Height--;

        return crop.Width >= 2 && crop.Height >= 2 ? crop : full;
    }

    private static void TryDeletePartialFile(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "VideoRecorder.PartialFileDeleteFailed {Path}", outputPath);
        }
    }

    // ---- D3D11 device management (same pattern as WinRtCaptureEngine) ----

    private void EnsureDevice()
    {
        if (_direct3DDevice is not null) return;
        int hr = D3D11Interop.D3D11CreateDevice(
            0, D3D11Interop.D3D_DRIVER_TYPE_HARDWARE, 0,
            (uint)D3D11Interop.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            0, 0, 7, out _d3dDevice, out _, out _d3dContext);
        if (hr < 0)
        {
            hr = D3D11Interop.D3D11CreateDevice(0, D3D11Interop.D3D_DRIVER_TYPE_WARP, 0,
                (uint)D3D11Interop.D3D11_CREATE_DEVICE_BGRA_SUPPORT, 0, 0, 7,
                out _d3dDevice, out _, out _d3dContext);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        }

        var iidDxgi = D3D11Interop.IID_IDXGIDevice;
        int hrQI = Marshal.QueryInterface(_d3dDevice, in iidDxgi, out nint dxgiDevice);
        if (hrQI < 0) Marshal.ThrowExceptionForHR(hrQI);
        try
        {
            int hrCreate = D3D11Interop.CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out nint inspectable);
            if (hrCreate < 0) Marshal.ThrowExceptionForHR(hrCreate);
            try
            {
                _direct3DDevice = (IDirect3DDevice)Marshal.GetObjectForIUnknown(inspectable);
            }
            finally { Marshal.Release(inspectable); }
        }
        finally { Marshal.Release(dxgiDevice); }
    }

    private static unsafe nint CreateTexture2D(nint device, ref D3D11Interop.D3D11_TEXTURE2D_DESC desc)
    {
        var vtbl = *(nint**)device;
        var fn = (delegate* unmanaged[Stdcall]<nint, ref D3D11Interop.D3D11_TEXTURE2D_DESC, nint, out nint, int>)vtbl[5];
        int hr = fn(device, ref desc, 0, out nint tex);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return tex;
    }

    private static unsafe void CopyResource(nint context, nint dst, nint src)
    {
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, nint, void>)vtbl[47];
        fn(context, dst, src);
    }

    private static unsafe D3D11Interop.D3D11_MAPPED_SUBRESOURCE MapResource(nint context, nint resource)
    {
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, uint, int, uint, out D3D11Interop.D3D11_MAPPED_SUBRESOURCE, int>)vtbl[14];
        int hr = fn(context, resource, 0, D3D11Interop.D3D11_MAP_READ, 0, out var mapped);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return mapped;
    }

    private static unsafe void UnmapResource(nint context, nint resource)
    {
        var vtbl = *(nint**)context;
        var fn = (delegate* unmanaged[Stdcall]<nint, nint, uint, void>)vtbl[15];
        fn(context, resource, 0);
    }

    private static void TrySetBorderRequired(GraphicsCaptureSession session, bool value)
    {
        try
        {
            if (Windows.Foundation.Metadata.ApiInformation.IsPropertyPresent(
                typeof(GraphicsCaptureSession).FullName!, "IsBorderRequired"))
                session.IsBorderRequired = value;
        }
        catch { }
    }

    private static void TrySetCursorCapture(GraphicsCaptureSession session, bool value)
    {
        try
        {
            if (ApiInformation.IsPropertyPresent(
                typeof(GraphicsCaptureSession).FullName!, "IsCursorCaptureEnabled"))
            {
#pragma warning disable CA1416
                session.IsCursorCaptureEnabled = value;
#pragma warning restore CA1416
            }
        }
        catch { }
    }

    private static bool TrySetDirtyRegionReporting(GraphicsCaptureSession session)
    {
        try
        {
            if (!ApiInformation.IsPropertyPresent(typeof(GraphicsCaptureSession).FullName!, "DirtyRegionMode"))
                return false;

            var prop = session.GetType().GetProperty("DirtyRegionMode");
            if (prop is null || !prop.CanWrite || prop.PropertyType.IsEnum is false)
                return false;

            prop.SetValue(session, Enum.ToObject(prop.PropertyType, 0));
            return true;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "VideoRecorder.DirtyRegionModeUnavailable");
            return false;
        }
    }

    private static int GetWindowProcessId(nint hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint processId);
        return checked((int)processId);
    }

    private static Rectangle ResolveWindowBounds(nint hwnd, int width, int height)
    {
        return WindowEnumerator.GetExtendedFrameBounds(hwnd, out var bounds)
            ? bounds
            : new Rectangle(0, 0, width, height);
    }

    private static Rectangle ResolveMonitorBounds(nint hMonitor, int width, int height)
    {
        return MonitorEnumerator.Enumerate().FirstOrDefault(m => m.Handle == hMonitor)?.Bounds
            ?? new Rectangle(0, 0, width, height);
    }

    private static nint ResolveMonitorHandle(Rectangle bounds)
    {
        if (bounds.IsEmpty) return 0;
        return MonitorEnumerator.FromPoint(
            new System.Drawing.Point(
                bounds.Left + Math.Max(0, bounds.Width / 2),
                bounds.Top + Math.Max(0, bounds.Height / 2)))?.Handle ?? 0;
    }

    private void OnCaptureItemClosed(GraphicsCaptureItem sender, object args)
    {
        _sourceClosedFlag = true;
        Log.Warning("VideoRecorder.CaptureItemClosed — source window/monitor went away");
        if (IsRecording && !IsPaused)
            Pause();
        SourceClosed?.Invoke();
    }

    private void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode == Microsoft.Win32.PowerModes.Suspend && IsRecording && !IsPaused)
        {
            Log.Information("VideoRecorder.PowerSuspend — pausing recording");
            Pause();
        }
        else if (e.Mode == Microsoft.Win32.PowerModes.Resume && IsRecording && IsPaused && !_sourceClosedFlag)
        {
            Log.Information("VideoRecorder.PowerResume — resuming recording");
            Resume();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!IsRecording) return;
        Log.Information("VideoRecorder.DisplaySettingsChanged — DPI or monitor topology changed during recording");
        _dirtyRegionFilter.ForceNextFrame();
        EnvironmentChanged?.Invoke("display settings changed");
    }

    private void OnAudioSourceChanged(string reason)
    {
        if (IsRecording)
            EnvironmentChanged?.Invoke(reason);
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRecording) Stop();
        if (_audioCapture is not null)
        {
            _audioCapture.SourceChanged -= OnAudioSourceChanged;
            _audioCapture.Dispose();
            _audioCapture = null;
        }
        StopPointerTracking();
        StopKeyboardTracking();
        if (_d3dContext != 0) { Marshal.Release(_d3dContext); _d3dContext = 0; }
        if (_d3dDevice != 0) { Marshal.Release(_d3dDevice); _d3dDevice = 0; }
    }
}
