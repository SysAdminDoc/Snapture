using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class DirtyRegionFrameFilterTests
{
    [TestMethod]
    public void ShouldEncode_WritesEveryFrame_WhenReportingIsUnavailable()
    {
        var filter = new DirtyRegionFrameFilter();
        filter.Reset(reportingEnabled: false);

        Assert.IsTrue(filter.ShouldEncode(0));
        Assert.IsTrue(filter.ShouldEncode(0));
        Assert.AreEqual(0, filter.SkippedFrameCount);
    }

    [TestMethod]
    public void ShouldEncode_WritesFirstFrame_EvenWhenItHasNoDirtyRegions()
    {
        var filter = new DirtyRegionFrameFilter();
        filter.Reset(reportingEnabled: true);

        Assert.IsTrue(filter.ShouldEncode(0));
        Assert.AreEqual(0, filter.SkippedFrameCount);
    }

    [TestMethod]
    public void ShouldEncode_SkipsCleanFramesAfterReferenceFrame()
    {
        var filter = new DirtyRegionFrameFilter();
        filter.Reset(reportingEnabled: true);

        Assert.IsTrue(filter.ShouldEncode(1));
        Assert.IsFalse(filter.ShouldEncode(0));
        Assert.IsFalse(filter.ShouldEncode(0));
        Assert.AreEqual(2, filter.SkippedFrameCount);
    }

    [TestMethod]
    public void ShouldEncode_WritesChangedFramesAfterSkippedCleanFrames()
    {
        var filter = new DirtyRegionFrameFilter();
        filter.Reset(reportingEnabled: true);

        Assert.IsTrue(filter.ShouldEncode(1));
        Assert.IsFalse(filter.ShouldEncode(0));
        Assert.IsTrue(filter.ShouldEncode(2));
        Assert.AreEqual(1, filter.SkippedFrameCount);
    }

    [TestMethod]
    public void ForceNextFrame_WritesNextFrameAfterPauseResume()
    {
        var filter = new DirtyRegionFrameFilter();
        filter.Reset(reportingEnabled: true);

        Assert.IsTrue(filter.ShouldEncode(1));
        Assert.IsFalse(filter.ShouldEncode(0));
        filter.ForceNextFrame();

        Assert.IsTrue(filter.ShouldEncode(0));
        Assert.AreEqual(1, filter.SkippedFrameCount);
    }
}
