using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace Snapture.App.Services;

internal sealed class RecordingAudioMixer : IDisposable
{
    private const int ChunkMilliseconds = 20;
    private const int MaxBufferedSeconds = 5;

    private readonly AudioSampleSource _systemSource = new();
    private readonly AudioSampleSource _microphoneSource = new();
    private readonly Action<byte[], int, long, long> _writePcm;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _stateLock = new();
    private readonly float[] _mixBuffer;

    private WasapiLoopbackCapture? _systemCapture;
    private WasapiCapture? _microphoneCapture;
    private Task? _mixTask;
    private long _writtenFrames;
    private bool _paused;
    private bool _disposed;

    public RecordingAudioMixer(RecordingAudioOptions options, Action<byte[], int, long, long> writePcm)
    {
        _writePcm = writePcm;
        _mixBuffer = new float[PcmAudioConverter.OutputFramesPerChunk * PcmAudioConverter.OutputChannels];
        _systemSource.Enabled = options.IncludeSystemAudio;
        _microphoneSource.Enabled = options.IncludeMicrophone;
    }

    public bool IsSystemAudioEnabled => _systemSource.Enabled;
    public bool IsMicrophoneEnabled => _microphoneSource.Enabled;
    public float SystemLevel => _systemSource.Level;
    public float MicrophoneLevel => _microphoneSource.Level;

    public string Description
    {
        get
        {
            string system = _systemSource.Enabled ? "system" : "system off";
            string microphone = _microphoneSource.Enabled ? "mic" : "mic off";
            return $"AAC audio: {system}, {microphone}";
        }
    }

    public void Start()
    {
        ThrowIfDisposed();

        if (_systemSource.Enabled)
            SetSystemAudioEnabled(true);
        if (_microphoneSource.Enabled)
            SetMicrophoneEnabled(true);

        _mixTask = Task.Run(MixLoop);
    }

    public bool SetSystemAudioEnabled(bool enabled)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            _systemSource.Enabled = enabled;
            _systemSource.Clear();
            if (!enabled) return true;
            return TryStartSystemAudioCapture();
        }
    }

    public bool SetMicrophoneEnabled(bool enabled)
    {
        ThrowIfDisposed();
        lock (_stateLock)
        {
            _microphoneSource.Enabled = enabled;
            _microphoneSource.Clear();
            if (!enabled) return true;
            return TryStartMicrophoneCapture();
        }
    }

    public void SetPaused(bool paused)
    {
        ThrowIfDisposed();
        _paused = paused;
        if (!paused)
        {
            _systemSource.Clear();
            _microphoneSource.Clear();
        }
    }

    private async Task MixLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (!_paused)
                    EmitChunk();

                await Task.Delay(ChunkMilliseconds, _cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "VideoRecorder.Audio.MixFailed");
                await Task.Delay(ChunkMilliseconds, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    private void EmitChunk()
    {
        Array.Clear(_mixBuffer);

        if (_systemSource.Enabled)
            _systemSource.MixInto(_mixBuffer);
        if (_microphoneSource.Enabled)
            _microphoneSource.MixInto(_mixBuffer);

        var pcm = PcmAudioConverter.FloatStereoToPcm16(_mixBuffer);
        long sampleTime = PcmAudioConverter.FramesToHundredNanoseconds(_writtenFrames);
        long duration = PcmAudioConverter.FramesToHundredNanoseconds(PcmAudioConverter.OutputFramesPerChunk);
        _writtenFrames += PcmAudioConverter.OutputFramesPerChunk;
        _writePcm(pcm, pcm.Length, sampleTime, duration);
    }

    private bool TryStartSystemAudioCapture()
    {
        if (_systemCapture is not null)
            return true;

        try
        {
            var capture = new WasapiLoopbackCapture();
            capture.DataAvailable += OnSystemAudioAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _systemCapture = capture;
            capture.StartRecording();
            Log.Information("VideoRecorder.Audio.SystemStarted {Format}", capture.WaveFormat);
            return true;
        }
        catch (Exception ex)
        {
            _systemSource.Enabled = false;
            Log.Warning(ex, "VideoRecorder.Audio.SystemUnavailable");
            return false;
        }
    }

    private bool TryStartMicrophoneCapture()
    {
        if (_microphoneCapture is not null)
            return true;

        try
        {
            var capture = new WasapiCapture();
            capture.DataAvailable += OnMicrophoneAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            _microphoneCapture = capture;
            capture.StartRecording();
            Log.Information("VideoRecorder.Audio.MicrophoneStarted {Format}", capture.WaveFormat);
            return true;
        }
        catch (Exception ex)
        {
            _microphoneSource.Enabled = false;
            Log.Warning(ex, "VideoRecorder.Audio.MicrophoneUnavailable");
            return false;
        }
    }

    private void OnSystemAudioAvailable(object? sender, WaveInEventArgs e)
    {
        if (_systemCapture is null || e.BytesRecorded <= 0) return;
        _systemSource.Add(e.Buffer, e.BytesRecorded, _systemCapture.WaveFormat);
    }

    private void OnMicrophoneAvailable(object? sender, WaveInEventArgs e)
    {
        if (_microphoneCapture is null || e.BytesRecorded <= 0) return;
        _microphoneSource.Add(e.Buffer, e.BytesRecorded, _microphoneCapture.WaveFormat);
    }

    private static void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            Log.Warning(e.Exception, "VideoRecorder.Audio.CaptureStopped");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try { _mixTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }

        StopCapture(_systemCapture);
        StopCapture(_microphoneCapture);
        _cts.Dispose();
    }

    private static void StopCapture(IWaveIn? capture)
    {
        if (capture is null) return;
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
    }

    private sealed class AudioSampleSource
    {
        private readonly Queue<float> _samples = new();
        private readonly object _lock = new();
        private DateTime _lastLevelAt = DateTime.MinValue;
        private float _level;

        public bool Enabled { get; set; }

        public float Level
        {
            get
            {
                lock (_lock)
                {
                    return Enabled && DateTime.UtcNow - _lastLevelAt < TimeSpan.FromMilliseconds(600)
                        ? _level
                        : 0f;
                }
            }
        }

        public void Add(byte[] buffer, int bytesRecorded, WaveFormat format)
        {
            var converted = PcmAudioConverter.ConvertToStereo48(buffer.AsSpan(0, bytesRecorded), format);
            if (converted.Samples.Length == 0)
                return;

            lock (_lock)
            {
                foreach (float sample in converted.Samples)
                    _samples.Enqueue(sample);

                int maxSamples = PcmAudioConverter.OutputSampleRate
                    * PcmAudioConverter.OutputChannels
                    * MaxBufferedSeconds;
                while (_samples.Count > maxSamples)
                    _samples.Dequeue();

                _level = converted.Peak;
                _lastLevelAt = DateTime.UtcNow;
            }
        }

        public void MixInto(float[] target)
        {
            lock (_lock)
            {
                int count = Math.Min(target.Length, _samples.Count);
                for (int i = 0; i < count; i++)
                    target[i] += _samples.Dequeue();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _samples.Clear();
                _level = 0f;
                _lastLevelAt = DateTime.MinValue;
            }
        }
    }
}

internal static class PcmAudioConverter
{
    public const int OutputSampleRate = 48_000;
    public const int OutputChannels = 2;
    public const int OutputBitsPerSample = 16;
    public const int OutputBlockAlign = OutputChannels * OutputBitsPerSample / 8;
    public const int OutputAverageBytesPerSecond = OutputSampleRate * OutputBlockAlign;
    public const int OutputFramesPerChunk = OutputSampleRate / 50;

    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00AA00389B71");

    public static (float[] Samples, float Peak) ConvertToStereo48(ReadOnlySpan<byte> bytes, WaveFormat sourceFormat)
    {
        WaveFormat format = Normalize(sourceFormat);
        if (!IsSupported(format) || format.BlockAlign <= 0 || format.Channels <= 0 || format.SampleRate <= 0)
            return (Array.Empty<float>(), 0f);

        int sourceFrames = bytes.Length / format.BlockAlign;
        if (sourceFrames == 0)
            return (Array.Empty<float>(), 0f);

        var source = new float[sourceFrames * OutputChannels];
        float peak = 0f;
        int bytesPerSample = format.BitsPerSample / 8;

        for (int frame = 0; frame < sourceFrames; frame++)
        {
            int frameOffset = frame * format.BlockAlign;
            float left = ReadSample(bytes, frameOffset, bytesPerSample, format);
            float right = format.Channels == 1
                ? left
                : ReadSample(bytes, frameOffset + bytesPerSample, bytesPerSample, format);

            int output = frame * OutputChannels;
            source[output] = left;
            source[output + 1] = right;
            peak = Math.Max(peak, Math.Max(Math.Abs(left), Math.Abs(right)));
        }

        if (format.SampleRate == OutputSampleRate)
            return (source, Math.Clamp(peak, 0f, 1f));

        int outputFrames = Math.Max(1, (int)Math.Round(sourceFrames * (double)OutputSampleRate / format.SampleRate));
        var resampled = new float[outputFrames * OutputChannels];
        double sourceFramesPerOutputFrame = format.SampleRate / (double)OutputSampleRate;

        for (int frame = 0; frame < outputFrames; frame++)
        {
            int sourceFrame = Math.Min(sourceFrames - 1, (int)Math.Round(frame * sourceFramesPerOutputFrame));
            int sourceOffset = sourceFrame * OutputChannels;
            int outputOffset = frame * OutputChannels;
            resampled[outputOffset] = source[sourceOffset];
            resampled[outputOffset + 1] = source[sourceOffset + 1];
        }

        return (resampled, Math.Clamp(peak, 0f, 1f));
    }

    public static byte[] FloatStereoToPcm16(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float sample = float.IsFinite(samples[i]) ? Math.Clamp(samples[i], -1f, 1f) : 0f;
            short value = sample <= -1f
                ? short.MinValue
                : (short)Math.Round(sample * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), value);
        }

        return pcm;
    }

    public static long FramesToHundredNanoseconds(long frames)
        => frames * 10_000_000L / OutputSampleRate;

    private static WaveFormat Normalize(WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Extensible || format is not WaveFormatExtensible extensible)
            return format;

        try
        {
            return extensible.ToStandardWaveFormat();
        }
        catch
        {
            return extensible.SubFormat == IeeeFloatSubFormat
                ? WaveFormat.CreateIeeeFloatWaveFormat(extensible.SampleRate, extensible.Channels)
                : format;
        }
    }

    private static bool IsSupported(WaveFormat format)
        => (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
           || (format.Encoding == WaveFormatEncoding.Pcm
               && (format.BitsPerSample == 16 || format.BitsPerSample == 24 || format.BitsPerSample == 32));

    private static float ReadSample(ReadOnlySpan<byte> bytes, int offset, int bytesPerSample, WaveFormat format)
    {
        return format.Encoding switch
        {
            WaveFormatEncoding.IeeeFloat => ClampFinite(BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(offset, 4))),
            WaveFormatEncoding.Pcm when bytesPerSample == 2 => BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(offset, 2)) / 32768f,
            WaveFormatEncoding.Pcm when bytesPerSample == 3 => ReadPcm24(bytes.Slice(offset, 3)),
            WaveFormatEncoding.Pcm when bytesPerSample == 4 => BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(offset, 4)) / 2147483648f,
            _ => 0f
        };
    }

    private static float ReadPcm24(ReadOnlySpan<byte> bytes)
    {
        int sample = bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);
        if ((sample & 0x800000) != 0)
            sample |= unchecked((int)0xFF000000);
        return sample / 8388608f;
    }

    private static float ClampFinite(float value)
        => float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0f;
}
