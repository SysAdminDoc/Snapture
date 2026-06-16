using System.Drawing;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CursorOverlayRendererTests
{
    [TestMethod]
    public void ToLocalPoint_ReturnsRelativePointInsideCaptureBounds()
    {
        var local = RecordingPointerTracker.ToLocalPoint(
            new Point(125, 240),
            new Rectangle(100, 200, 300, 250));

        Assert.IsNotNull(local);
        Assert.AreEqual(new Point(25, 40), local.Value);
    }

    [TestMethod]
    public void ToLocalPoint_ReturnsNullOutsideCaptureBounds()
    {
        var local = RecordingPointerTracker.ToLocalPoint(
            new Point(99, 240),
            new Rectangle(100, 200, 300, 250));

        Assert.IsNull(local);
    }

    [TestMethod]
    public void RenderBgra_DrawsCursorAndClickEffects()
    {
        var pixels = new byte[80 * 80 * 4];
        var frame = new RecordingPointerFrame(
            new Point(40, 40),
            new[]
            {
                new RecordingPointerEffect(new Point(24, 24), RecordingPointerButton.Left, 80)
            });

        CursorOverlayRenderer.RenderBgra(pixels, 80, 80, 80 * 4, frame);

        Assert.IsTrue(pixels.Any(static b => b != 0));
    }

    [TestMethod]
    public void RenderBgra_ClipsEffectsAtFrameEdges()
    {
        var pixels = new byte[16 * 16 * 4];
        var frame = new RecordingPointerFrame(
            new Point(0, 0),
            new[]
            {
                new RecordingPointerEffect(new Point(15, 15), RecordingPointerButton.Right, 20)
            });

        CursorOverlayRenderer.RenderBgra(pixels, 16, 16, 16 * 4, frame);

        Assert.IsTrue(pixels.Any(static b => b != 0));
    }
}
