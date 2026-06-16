using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Snapture.App.Services;

internal sealed class ProcessLoopbackCapture : IWaveIn, IDisposable
{
    private const string VirtualAudioDeviceProcessLoopback = "VAD\\Process_Loopback";
    private const ushort VtBlob = 65;

    private readonly int _targetProcessId;
    private readonly bool _includeProcessTree;
    private readonly AutoResetEvent _sampleReadyEvent = new(false);
    private readonly object _lock = new();

    private AudioClient? _audioClient;
    private AudioCaptureClient? _captureClient;
    private Thread? _captureThread;
    private bool _recording;
    private Exception? _captureException;

    public ProcessLoopbackCapture(int targetProcessId, bool includeProcessTree)
    {
        if (targetProcessId <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetProcessId));

        _targetProcessId = targetProcessId;
        _includeProcessTree = includeProcessTree;
        WaveFormat = new WaveFormat(PcmAudioConverter.OutputSampleRate, PcmAudioConverter.OutputBitsPerSample, PcmAudioConverter.OutputChannels);
    }

    public WaveFormat WaveFormat { get; set; }
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;

    public void StartRecording()
    {
        lock (_lock)
        {
            if (_recording) return;

            _audioClient = ProcessLoopbackActivator.Activate(_targetProcessId, _includeProcessTree);
            var flags = AudioClientStreamFlags.Loopback
                        | AudioClientStreamFlags.EventCallback
                        | AudioClientStreamFlags.AutoConvertPcm;
            _audioClient.Initialize(AudioClientShareMode.Shared, flags, 0, 0, WaveFormat, Guid.Empty);
            _captureClient = _audioClient.AudioCaptureClient;
            _audioClient.SetEventHandle(_sampleReadyEvent.SafeWaitHandle.DangerousGetHandle());

            _recording = true;
            _captureException = null;
            _captureThread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "Snapture process-loopback audio"
            };
            _captureThread.Start();
            _audioClient.Start();
        }
    }

    public void StopRecording()
    {
        Thread? thread;
        Exception? stoppedException;
        lock (_lock)
        {
            if (!_recording) return;
            _recording = false;
            try { _audioClient?.Stop(); } catch { }
            _sampleReadyEvent.Set();
            thread = _captureThread;
        }

        if (thread is not null && thread.IsAlive)
            thread.Join(TimeSpan.FromSeconds(1));

        lock (_lock)
        {
            stoppedException = _captureException;
            _captureThread = null;
            _captureClient?.Dispose();
            _captureClient = null;
            _audioClient?.Dispose();
            _audioClient = null;
        }

        RecordingStopped?.Invoke(this, new StoppedEventArgs(stoppedException));
    }

    private void CaptureLoop()
    {
        try
        {
            while (_recording)
            {
                _sampleReadyEvent.WaitOne(TimeSpan.FromMilliseconds(200));
                if (!_recording) break;
                DrainCapturePackets();
            }
        }
        catch (Exception ex)
        {
            _captureException = ex;
            _recording = false;
        }
    }

    private void DrainCapturePackets()
    {
        if (_captureClient is null) return;

        while (_recording && _captureClient.GetNextPacketSize() > 0)
        {
            nint buffer = _captureClient.GetBuffer(
                out int framesAvailable,
                out AudioClientBufferFlags flags,
                out _,
                out _);

            int byteCount = framesAvailable * WaveFormat.BlockAlign;
            var managed = new byte[byteCount];
            try
            {
                if ((flags & AudioClientBufferFlags.Silent) == 0 && byteCount > 0)
                    Marshal.Copy(buffer, managed, 0, byteCount);
            }
            finally
            {
                _captureClient.ReleaseBuffer(framesAvailable);
            }

            if (byteCount > 0)
                DataAvailable?.Invoke(this, new WaveInEventArgs(managed, byteCount));
        }
    }

    public void Dispose()
    {
        StopRecording();
        _sampleReadyEvent.Dispose();
    }

    private static class ProcessLoopbackActivator
    {
        public static AudioClient Activate(int processId, bool includeProcessTree)
        {
            var activationParams = new AudioClientActivationParams
            {
                ActivationType = AudioClientActivationType.ProcessLoopback,
                ProcessLoopbackParams = new AudioClientProcessLoopbackParams
                {
                    TargetProcessId = processId,
                    ProcessLoopbackMode = includeProcessTree
                        ? ProcessLoopbackMode.IncludeTargetProcessTree
                        : ProcessLoopbackMode.ExcludeTargetProcessTree
                }
            };

            int size = Marshal.SizeOf<AudioClientActivationParams>();
            nint activationParamsPtr = Marshal.AllocHGlobal(size);
            IActivateAudioInterfaceAsyncOperation? operation = null;
            try
            {
                Marshal.StructureToPtr(activationParams, activationParamsPtr, false);
                var propVariant = new PropVariant
                {
                    vt = VtBlob,
                    blob = new Blob
                    {
                        cbSize = (uint)size,
                        pBlobData = activationParamsPtr
                    }
                };

                var completion = new ActivateCompletionHandler();
                Guid iidAudioClient = typeof(IAudioClient).GUID;
                int hr = ActivateAudioInterfaceAsync(
                    VirtualAudioDeviceProcessLoopback,
                    ref iidAudioClient,
                    ref propVariant,
                    completion,
                    out operation);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);

                return completion.WaitForAudioClient();
            }
            finally
            {
                if (operation is not null) Marshal.ReleaseComObject(operation);
                Marshal.FreeHGlobal(activationParamsPtr);
            }
        }

        [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        private static extern int ActivateAudioInterfaceAsync(
            string deviceInterfacePath,
            ref Guid riid,
            ref PropVariant activationParams,
            IActivateAudioInterfaceCompletionHandler completionHandler,
            out IActivateAudioInterfaceAsyncOperation activationOperation);
    }

    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class ActivateCompletionHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly TaskCompletionSource<AudioClient> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            try
            {
                int hr = activateOperation.GetActivateResult(out int activateResult, out object activatedInterface);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (activateResult < 0) Marshal.ThrowExceptionForHR(activateResult);

                var audioClient = (IAudioClient)activatedInterface;
                _completion.TrySetResult(new AudioClient(audioClient));
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }

            return 0;
        }

        public AudioClient WaitForAudioClient()
        {
            if (!_completion.Task.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("Process loopback audio activation timed out.");

            return _completion.Task.GetAwaiter().GetResult();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort wReserved1;
        public ushort wReserved2;
        public ushort wReserved3;
        public Blob blob;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Blob
    {
        public uint cbSize;
        public nint pBlobData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public AudioClientActivationType ActivationType;
        public AudioClientProcessLoopbackParams ProcessLoopbackParams;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientProcessLoopbackParams
    {
        public int TargetProcessId;
        public ProcessLoopbackMode ProcessLoopbackMode;
    }

    private enum AudioClientActivationType
    {
        Default = 0,
        ProcessLoopback = 1
    }

    private enum ProcessLoopbackMode
    {
        IncludeTargetProcessTree = 0,
        ExcludeTargetProcessTree = 1
    }
}
