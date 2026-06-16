using System.Diagnostics;
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

    public bool IsRecording { get; private set; }
    public bool IsPaused { get; private set; }
    public int FrameCount { get; private set; }
    public int SkippedCleanFrameCount => _dirtyRegionFilter.SkippedFrameCount;
    public TimeSpan Elapsed => _sw.Elapsed;
    public string SelectedCodecName { get; private set; } = "H.264";
    public string SelectedCodecDescription { get; private set; } = "H.264";
    public string DirtyRegionDescription => _dirtyRegionFilter.ReportingEnabled
        ? "dirty-region skip enabled"
        : "dirty-region skip unavailable";
    public string ContainerDescription { get; private set; } = "fragmented MP4";
    public event Action<int, TimeSpan>? Progress;

    private readonly Stopwatch _sw = new();
    private readonly object _lock = new();
    private readonly DirtyRegionFrameFilter _dirtyRegionFilter = new();
    private MFInterop.IMFSinkWriter? _writer;
    private uint _videoStreamIndex;
    private int _sourceWidth, _sourceHeight;
    private int _width, _height;
    private long _frameDurationHns;

    // WGC continuous capture
    private nint _d3dDevice;
    private nint _d3dContext;
    private IDirect3DDevice? _direct3DDevice;
    private Direct3D11CaptureFramePool? _framePool;
    private GraphicsCaptureSession? _session;
    private long _firstTimestamp = -1;
    private long _pauseOffsetTicks;
    private long _pauseStartTicks;

    private string? _outputPath;
    private bool _disposed;
    private bool _mfStarted;

    private const ulong Mp4FragmentDurationHns = 20_000_000; // two seconds

    /// <summary>
    /// Start recording the foreground window to the given output path.
    /// </summary>
    public void StartWindow(nint hwnd, string outputPath, int fps = 30, int bitrateMbps = 8)
    {
        if (IsRecording) return;
        EnsureDevice();
        var item = CaptureItemFactory.CreateForWindow(hwnd)
            ?? throw new InvalidOperationException("CreateForWindow returned null.");
        StartInternal(item, outputPath, fps, bitrateMbps);
    }

    /// <summary>
    /// Start recording a specific monitor to the given output path.
    /// </summary>
    public void StartMonitor(nint hMonitor, string outputPath, int fps = 30, int bitrateMbps = 8)
    {
        if (IsRecording) return;
        EnsureDevice();
        var item = CaptureItemFactory.CreateForMonitor(hMonitor)
            ?? throw new InvalidOperationException("CreateForMonitor returned null.");
        StartInternal(item, outputPath, fps, bitrateMbps);
    }

    public void Pause()
    {
        if (!IsRecording || IsPaused) return;
        IsPaused = true;
        _pauseStartTicks = _sw.ElapsedTicks;
        _sw.Stop();
        Log.Information("VideoRecorder.Paused");
    }

    public void Resume()
    {
        if (!IsRecording || !IsPaused) return;
        _pauseOffsetTicks += _sw.ElapsedTicks - _pauseStartTicks;
        IsPaused = false;
        _dirtyRegionFilter.ForceNextFrame();
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

        _session?.Dispose();
        _session = null;
        _framePool?.Dispose();
        _framePool = null;

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

        Log.Information("VideoRecorder.Stopped {Frames} {SkippedCleanFrames} {Duration}",
            FrameCount, SkippedCleanFrameCount, Elapsed);
        return _outputPath;
    }

    private void StartInternal(GraphicsCaptureItem item, string outputPath, int fps, int bitrateMbps)
    {
        _outputPath = outputPath;
        _sourceWidth = item.Size.Width;
        _sourceHeight = item.Size.Height;
        _width = _sourceWidth;
        _height = _sourceHeight;

        // Ensure width/height are even for MP4 encoders. The WGC source texture keeps
        // its native size; WriteFrame crops the final row/column if needed.
        if (_width % 2 != 0) _width--;
        if (_height % 2 != 0) _height--;
        if (_width <= 0 || _height <= 0)
            throw new InvalidOperationException("Capture item has invalid dimensions.");

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
        _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _direct3DDevice!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            3,
            item.Size);

        _framePool.FrameArrived += OnFrameArrived;

        _session = _framePool.CreateCaptureSession(item);
        TrySetBorderRequired(_session, false);
        TrySetCursorCapture(_session, true);
        _dirtyRegionFilter.Reset(TrySetDirtyRegionReporting(_session));

        FrameCount = 0;
        _firstTimestamp = -1;
        _pauseOffsetTicks = 0;
        IsPaused = false;
        IsRecording = true;
        _sw.Restart();

        _session.StartCapture();
        Log.Information("VideoRecorder.Started {Width}x{Height} {Fps}fps {Bitrate}Mbps DirtyRegions={DirtyRegions}",
            _width, _height, fps, bitrateMbps, _dirtyRegionFilter.ReportingEnabled);
    }

    private void ConfigureSinkWriter(string outputPath, int fps, int bitrateMbps)
    {
        var candidates = MediaFoundationVideoCodecDiscovery.GetPreferredEncodingCandidates();
        if (candidates.Count == 0)
            throw new InvalidOperationException("No Media Foundation video encoder was found.");

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                ConfigureSinkWriter(outputPath, fps, bitrateMbps, candidate);
                SelectedCodecName = candidate.CodecName;
                SelectedCodecDescription = candidate.EncoderSummary;
                Log.Information("VideoRecorder.CodecSelected {Codec} {Encoder}",
                    candidate.CodecName, candidate.EncoderSummary);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Log.Warning(ex, "VideoRecorder.CodecConfigureFailed {Codec} {Encoder}",
                    candidate.CodecName, candidate.EncoderSummary);
                ReleaseWriter();
                TryDeletePartialFile(outputPath);
            }
        }

        throw new InvalidOperationException("No Media Foundation video encoder could be configured.", lastError);
    }

    private void ConfigureSinkWriter(
        string outputPath,
        int fps,
        int bitrateMbps,
        MediaFoundationVideoCodecDiscovery.EncoderCandidate candidate)
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
            outputType.SetUINT64(ref frameSizeKey, MFInterop.Pack2x32((uint)_width, (uint)_height));

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
            _writer.BeginWriting();
        }
        finally
        {
            if (inputType is not null) Marshal.ReleaseComObject(inputType);
            if (outputType is not null) Marshal.ReleaseComObject(outputType);
            Marshal.ReleaseComObject(writerAttrs);
        }
    }

    private void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
    {
        if (!IsRecording || IsPaused) return;

        using var frame = sender.TryGetNextFrame();
        if (frame is null) return;

        long timestamp = frame.SystemRelativeTime.Ticks;

        if (_firstTimestamp < 0)
            _firstTimestamp = timestamp;

        long pts = (timestamp - _firstTimestamp) - _pauseOffsetTicks;
        if (pts < 0) pts = 0;

        int? dirtyRegionCount = DirtyRegionInterop.TryGetDirtyRegionCount(frame);
        if (!_dirtyRegionFilter.ShouldEncode(dirtyRegionCount))
        {
            Progress?.Invoke(FrameCount, Elapsed);
            return;
        }

        try
        {
            WriteFrame(frame, pts);
            FrameCount++;
            Progress?.Invoke(FrameCount, Elapsed);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "VideoRecorder.WriteFrame.Failed");
        }
    }

    private unsafe void WriteFrame(Direct3D11CaptureFrame frame, long pts)
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
                Format = D3D11Interop.DXGI_FORMAT_B8G8R8A8_UNORM,
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
                        byte* src = (byte*)mapped.pData;
                        byte* dst = (byte*)bufPtr;
                        int rowBytes = _width * 4;
                        for (int y = 0; y < _height; y++)
                        {
                            Buffer.MemoryCopy(
                                src + y * (long)mapped.RowPitch,
                                dst + y * (long)rowBytes,
                                rowBytes, rowBytes);
                        }
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

    private void ReleaseWriter()
    {
        if (_writer is null) return;
        Marshal.ReleaseComObject(_writer);
        _writer = null;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRecording) Stop();
        if (_d3dContext != 0) { Marshal.Release(_d3dContext); _d3dContext = 0; }
        if (_d3dDevice != 0) { Marshal.Release(_d3dDevice); _d3dDevice = 0; }
    }
}
