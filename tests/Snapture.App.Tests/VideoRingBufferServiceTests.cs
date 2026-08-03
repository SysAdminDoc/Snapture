using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class VideoRingBufferServiceTests
{
    [TestMethod]
    public void SelectRecentStart_ReturnsZeroWhenBufferIsShorterThanRequest()
    {
        var start = VideoRingBufferService.SelectRecentStart(
            TimeSpan.FromSeconds(12),
            TimeSpan.FromSeconds(30));

        Assert.AreEqual(TimeSpan.Zero, start);
    }

    [TestMethod]
    public void SelectRecentStart_LeavesRequestedTailDuration()
    {
        var start = VideoRingBufferService.SelectRecentStart(
            TimeSpan.FromSeconds(88),
            TimeSpan.FromSeconds(60));

        Assert.AreEqual(TimeSpan.FromSeconds(28), start);
    }
}
