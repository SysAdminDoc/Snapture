using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class VideoSegmentServiceTests
{
    [TestMethod]
    public void BuildSplitRanges_SortsDeduplicatesAndIgnoresOutsideMarkers()
    {
        var ranges = VideoSegmentService.BuildSplitRanges(
            TimeSpan.FromSeconds(30),
            new[] { TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10), TimeSpan.Zero, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(40) });

        Assert.HasCount(3, ranges);
        Assert.AreEqual(TimeSpan.Zero, ranges[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(10), ranges[0].End);
        Assert.AreEqual(TimeSpan.FromSeconds(10), ranges[1].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(20), ranges[1].End);
        Assert.AreEqual(TimeSpan.FromSeconds(20), ranges[2].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ranges[2].End);
    }

    [TestMethod]
    public void NormalizeRange_DefaultsEndToDuration()
    {
        var range = VideoSegmentService.NormalizeRange(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(2),
            end: null);

        Assert.AreEqual(TimeSpan.FromSeconds(2), range.Start);
        Assert.AreEqual(TimeSpan.FromSeconds(10), range.Duration);
    }

    [TestMethod]
    public void NormalizeRange_RejectsEndBeforeStart()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VideoSegmentService.NormalizeRange(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(7)));
    }

    [TestMethod]
    public void BuildSplitRanges_DropsSubFrameTinySegments()
    {
        var ranges = VideoSegmentService.BuildSplitRanges(
            TimeSpan.FromSeconds(5),
            new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(2) });

        Assert.HasCount(2, ranges);
        Assert.AreEqual(TimeSpan.Zero, ranges[0].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(2), ranges[0].End);
        Assert.AreEqual(TimeSpan.FromSeconds(2), ranges[1].Start);
        Assert.AreEqual(TimeSpan.FromSeconds(5), ranges[1].End);
    }
}
