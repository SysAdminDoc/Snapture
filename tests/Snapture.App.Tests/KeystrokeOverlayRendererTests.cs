using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class KeystrokeOverlayRendererTests
{
    [TestMethod]
    public void FormatKeyChord_CombinesModifiersWithKey()
    {
        var keys = new HashSet<int> { 0x11, 0x10, 0x53 };

        string? chord = RecordingKeyboardTracker.FormatKeyChord(0x53, keys);

        Assert.AreEqual("CTRL+SHIFT+S", chord);
    }

    [TestMethod]
    public void FormatKeyChord_IgnoresModifierOnlyPresses()
    {
        var keys = new HashSet<int> { 0x11 };

        string? chord = RecordingKeyboardTracker.FormatKeyChord(0x11, keys);

        Assert.IsNull(chord);
    }

    [TestMethod]
    public void RenderBgra_DrawsKeystrokeOverlay()
    {
        var pixels = new byte[240 * 120 * 4];
        var frame = new RecordingKeystrokeFrame(new[]
        {
            new RecordingKeystrokeEffect("CTRL+S", 1, 200)
        });

        KeystrokeOverlayRenderer.RenderBgra(pixels, 240, 120, 240 * 4, frame);

        Assert.IsTrue(pixels.Any(static b => b != 0));
    }

    [TestMethod]
    public void RenderBgra_HandlesRepeatedKeystrokeLabel()
    {
        var pixels = new byte[240 * 120 * 4];
        var frame = new RecordingKeystrokeFrame(new[]
        {
            new RecordingKeystrokeEffect("SPACE", 3, 200)
        });

        KeystrokeOverlayRenderer.RenderBgra(pixels, 240, 120, 240 * 4, frame);

        Assert.IsGreaterThan(0, KeystrokeOverlayRenderer.MeasureText("SPACEX3", 2));
        Assert.IsTrue(pixels.Any(static b => b != 0));
    }
}
