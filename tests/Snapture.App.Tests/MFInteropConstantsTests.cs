using Snapture.Capture;

namespace Snapture.App.Tests;

[TestClass]
public sealed class MFInteropConstantsTests
{
    [TestMethod]
    public void Mpeg4FragmentAttributes_MatchWindowsSdkGuids()
    {
        Assert.AreEqual(new Guid("150FF23F-4ABC-478B-AC4F-E1916FBA1CCA"), MFInterop.MF_TRANSCODE_CONTAINERTYPE);
        Assert.AreEqual(new Guid("DC6CD05D-B9D0-40EF-BD35-FA622C1AB28A"), MFInterop.MFTranscodeContainerType_MPEG4);
        Assert.AreEqual(new Guid("9BA876F1-419F-4B77-A1E0-35959D9D4004"), MFInterop.MFTranscodeContainerType_FMPEG4);
        Assert.AreEqual(new Guid("A30B570C-8EFD-45E8-94FE-27C84B5BDFF6"), MFInterop.MF_MPEG4SINK_MIN_FRAGMENT_DURATION);
    }

    [TestMethod]
    public void SinkWriterInterface_MatchesWindowsSdkIid()
    {
        Assert.AreEqual(new Guid("3137F1CD-FE5E-4805-A5D8-FB477448CB3D"), typeof(MFInterop.IMFSinkWriter).GUID);
    }

    [TestMethod]
    public void AudioMediaTypesAndAttributes_MatchWindowsSdkGuids()
    {
        Assert.AreEqual(new Guid("73647561-0000-0010-8000-00AA00389B71"), MFInterop.MFMediaType_Audio);
        Assert.AreEqual(new Guid("00001610-0000-0010-8000-00AA00389B71"), MFInterop.MFAudioFormat_AAC);
        Assert.AreEqual(new Guid("00000001-0000-0010-8000-00AA00389B71"), MFInterop.MFAudioFormat_PCM);
        Assert.AreEqual(new Guid("00000003-0000-0010-8000-00AA00389B71"), MFInterop.MFAudioFormat_Float);
        Assert.AreEqual(new Guid("C9173739-5E56-461C-B713-46FB995CB95F"), MFInterop.MF_MT_ALL_SAMPLES_INDEPENDENT);
        Assert.AreEqual(new Guid("37E48BF5-645E-4C5B-89DE-ADA9E29B696A"), MFInterop.MF_MT_AUDIO_NUM_CHANNELS);
        Assert.AreEqual(new Guid("5FAEEAE7-0290-4C31-9E8A-C534F68D9DBA"), MFInterop.MF_MT_AUDIO_SAMPLES_PER_SECOND);
        Assert.AreEqual(new Guid("1AAB75C8-CFEF-451C-AB95-AC034B8E1731"), MFInterop.MF_MT_AUDIO_AVG_BYTES_PER_SECOND);
        Assert.AreEqual(new Guid("322DE230-9EEB-43BD-AB7A-FF412251541D"), MFInterop.MF_MT_AUDIO_BLOCK_ALIGNMENT);
        Assert.AreEqual(new Guid("F2DEB57F-40FA-4764-AA33-ED4F2D1FF669"), MFInterop.MF_MT_AUDIO_BITS_PER_SAMPLE);
        Assert.AreEqual(new Guid("55FB5765-644A-4CAF-8479-938983BB1588"), MFInterop.MF_MT_AUDIO_CHANNEL_MASK);
        Assert.AreEqual(new Guid("BFBABE79-7434-4D1C-94F0-72A3B9E17188"), MFInterop.MF_MT_AAC_PAYLOAD_TYPE);
        Assert.AreEqual(new Guid("7632F0E6-9538-4D61-ACDA-EA29C8C14456"), MFInterop.MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION);
    }
}
