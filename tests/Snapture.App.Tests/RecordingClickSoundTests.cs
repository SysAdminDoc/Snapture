using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class RecordingClickSoundTests
{
    [TestMethod]
    public void SampleAt_ReturnsFiniteDecayingTone()
    {
        float earlyPeak = PeakBetween(RecordingClickSound.DurationFrames / 8, RecordingClickSound.DurationFrames / 4);
        float latePeak = PeakBetween(RecordingClickSound.DurationFrames * 3 / 4, RecordingClickSound.DurationFrames - 1);

        Assert.IsTrue(float.IsFinite(earlyPeak));
        Assert.IsTrue(float.IsFinite(latePeak));
        Assert.IsGreaterThan(latePeak, earlyPeak);
    }

    [TestMethod]
    public void Mixer_QueuedClickWritesIntoStereoChunk()
    {
        var mixer = new RecordingClickSoundMixer();
        var samples = new float[PcmAudioConverter.OutputFramesPerChunk * PcmAudioConverter.OutputChannels];

        mixer.QueueClick();
        mixer.MixInto(samples);

        Assert.IsTrue(samples.Any(static sample => sample != 0f));
    }

    [TestMethod]
    public void Mixer_ClickExpiresAfterDuration()
    {
        var mixer = new RecordingClickSoundMixer();
        var samples = new float[PcmAudioConverter.OutputFramesPerChunk * PcmAudioConverter.OutputChannels];

        mixer.QueueClick();
        int chunks = (RecordingClickSound.DurationFrames / PcmAudioConverter.OutputFramesPerChunk) + 2;
        for (int i = 0; i < chunks; i++)
        {
            Array.Clear(samples);
            mixer.MixInto(samples);
        }

        Assert.IsFalse(mixer.HasActiveClicks);
    }

    private static float PeakBetween(int startFrame, int endFrame)
    {
        float peak = 0f;
        for (int frame = startFrame; frame <= endFrame; frame++)
            peak = Math.Max(peak, Math.Abs(RecordingClickSound.SampleAt(frame)));
        return peak;
    }
}
