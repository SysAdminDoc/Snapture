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
}
