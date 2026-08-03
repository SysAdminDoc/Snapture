using System.Drawing;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class RecordingAutoTightenEngineTests
{
    [TestMethod]
    public void BuildPlan_RemovesFullWidthTopTabsAndBottomTaskbar()
    {
        var capture = new Rectangle(0, 0, 1920, 1080);
        var plan = RecordingAutoTightenEngine.BuildPlan(capture, new[]
        {
            new RecordingChromeRegion(new Rectangle(0, 0, 1920, 48), RecordingChromeKind.Tab, "tabs"),
            new RecordingChromeRegion(new Rectangle(0, 1040, 1920, 40), RecordingChromeKind.Taskbar, "taskbar")
        });

        Assert.IsTrue(plan.IsApplied);
        Assert.AreEqual(new Rectangle(0, 48, 1920, 992), plan.Crop);
        Assert.IsTrue(plan.RemovedRegions.Any(region => region.Kind == RecordingChromeKind.Tab));
        Assert.IsTrue(plan.RemovedRegions.Any(region => region.Kind == RecordingChromeKind.Taskbar));
        StringAssert.Contains(plan.Description, "tabs");
        StringAssert.Contains(plan.Description, "taskbar");
    }

    [TestMethod]
    public void BuildPlan_RemovesEdgeDockWhenContentRemainsWide()
    {
        var capture = new Rectangle(100, 40, 1280, 720);
        var plan = RecordingAutoTightenEngine.BuildPlan(capture, new[]
        {
            new RecordingChromeRegion(new Rectangle(100, 140, 80, 520), RecordingChromeKind.Dock, "dock")
        });

        Assert.IsTrue(plan.IsApplied);
        Assert.AreEqual(180, plan.Crop.Left);
        Assert.AreEqual(capture.Right, plan.Crop.Right);
        Assert.AreEqual(capture.Top, plan.Crop.Top);
        Assert.AreEqual(capture.Bottom, plan.Crop.Bottom);
    }

    [TestMethod]
    public void BuildPlan_LeavesCaptureUntouchedWhenChromeDoesNotSpanAnEdge()
    {
        var capture = new Rectangle(0, 0, 1280, 720);
        var plan = RecordingAutoTightenEngine.BuildPlan(capture, new[]
        {
            new RecordingChromeRegion(new Rectangle(100, 100, 320, 40), RecordingChromeKind.Toolbar, "floating toolbar")
        });

        Assert.IsFalse(plan.IsApplied);
        Assert.AreEqual(capture, plan.Crop);
        Assert.HasCount(0, plan.RemovedRegions);
    }
}
