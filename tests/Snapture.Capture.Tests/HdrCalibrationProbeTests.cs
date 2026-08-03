using Snapture.Capture;

namespace Snapture.Capture.Tests;

public sealed class HdrCalibrationProbeTests
{
    [Theory]
    [InlineData(0f, true)]
    [InlineData(99.9f, true)]
    [InlineData(100f, false)]
    [InlineData(400f, false)]
    public void IsSuspicious_UsesThePeakLuminanceFloor(float maxLuminance, bool expected)
    {
        Assert.Equal(expected, HdrCalibrationProbe.IsSuspicious(maxLuminance));
    }

    [Fact]
    public void SdrOutput_IsNotSuspiciousEvenWhenMetadataIsLow()
    {
        var info = new HdrCalibrationInfo("DISPLAY1", false, 0, 0);

        Assert.False(info.IsSuspicious);
    }

    [Fact]
    public void HdrOutput_WithLowPeak_IsSuspicious()
    {
        var info = new HdrCalibrationInfo("DISPLAY2", true, 80, 65);

        Assert.True(info.IsSuspicious);
    }
}
