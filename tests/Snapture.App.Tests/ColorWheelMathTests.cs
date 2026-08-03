using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ColorWheelMathTests
{
    [TestMethod]
    public void FromHsvProducesPrimaryColours()
    {
        Assert.AreEqual(0xFFFF0000u, ColorWheelMath.FromHsv(0, 1, 1));
        Assert.AreEqual(0xFF00FF00u, ColorWheelMath.FromHsv(120, 1, 1));
        Assert.AreEqual(0xFF0000FFu, ColorWheelMath.FromHsv(240, 1, 1));
    }

    [TestMethod]
    public void WheelPointPreservesAlphaAndMapsCenterToWhite()
    {
        Assert.IsTrue(ColorWheelMath.TryFromPoint(0, 0, 100, 0x7A, out var center));
        Assert.AreEqual(0x7AFFFFFFu, center);

        Assert.IsTrue(ColorWheelMath.TryFromPoint(100, 0, 100, 0x7A, out var edge));
        Assert.AreEqual(0x7AFF0000u, edge);
    }

    [TestMethod]
    public void WheelPointOutsideRadiusIsRejected()
    {
        Assert.IsFalse(ColorWheelMath.TryFromPoint(101, 0, 100, 255, out _));
    }
}
