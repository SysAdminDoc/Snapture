namespace Snapture.App.Services;

internal sealed class RecordingClickSoundMixer
{
    private const int MaxActiveClicks = 12;
    private readonly object _lock = new();
    private readonly List<int> _activeFrameOffsets = new();
    private int _pendingClicks;

    public bool HasActiveClicks
    {
        get
        {
            lock (_lock)
            {
                return _pendingClicks > 0 || _activeFrameOffsets.Count > 0;
            }
        }
    }

    public void QueueClick()
    {
        lock (_lock)
        {
            if (_pendingClicks + _activeFrameOffsets.Count < MaxActiveClicks)
                _pendingClicks++;
        }
    }

    public void MixInto(float[] stereoSamples)
    {
        if (stereoSamples.Length == 0)
            return;

        lock (_lock)
        {
            while (_pendingClicks > 0 && _activeFrameOffsets.Count < MaxActiveClicks)
            {
                _activeFrameOffsets.Add(0);
                _pendingClicks--;
            }

            for (int i = _activeFrameOffsets.Count - 1; i >= 0; i--)
            {
                int nextOffset = RecordingClickSound.MixInto(stereoSamples, _activeFrameOffsets[i]);
                if (nextOffset >= RecordingClickSound.DurationFrames)
                    _activeFrameOffsets.RemoveAt(i);
                else
                    _activeFrameOffsets[i] = nextOffset;
            }
        }
    }
}

internal static class RecordingClickSound
{
    public const int DurationMilliseconds = 80;
    public const int DurationFrames = PcmAudioConverter.OutputSampleRate * DurationMilliseconds / 1000;

    private const double FrequencyOne = 1650.0;
    private const double FrequencyTwo = 2900.0;
    private const double Gain = 0.24;
    private const int AttackFrames = PcmAudioConverter.OutputSampleRate * 3 / 1000;

    public static int MixInto(float[] stereoSamples, int startFrame)
    {
        int frameCount = stereoSamples.Length / PcmAudioConverter.OutputChannels;
        int sourceFrame = startFrame;

        for (int frame = 0; frame < frameCount && sourceFrame < DurationFrames; frame++, sourceFrame++)
        {
            float sample = SampleAt(sourceFrame);
            int offset = frame * PcmAudioConverter.OutputChannels;
            stereoSamples[offset] += sample;
            stereoSamples[offset + 1] += sample;
        }

        return sourceFrame;
    }

    internal static float SampleAt(int frame)
    {
        if (frame < 0 || frame >= DurationFrames)
            return 0f;

        double t = frame / (double)PcmAudioConverter.OutputSampleRate;
        double progress = frame / (double)DurationFrames;
        double attack = AttackFrames <= 0 ? 1.0 : Math.Min(1.0, frame / (double)AttackFrames);
        double envelope = attack * Math.Pow(1.0 - progress, 3.2);
        double tone = (0.7 * Math.Sin(2.0 * Math.PI * FrequencyOne * t))
            + (0.3 * Math.Sin(2.0 * Math.PI * FrequencyTwo * t));

        return (float)(tone * envelope * Gain);
    }
}
