using System.Drawing;
using Snapture.Capture;

namespace Snapture.Capture.Tests;

public sealed class MagnificationCaptureTests
{
    [Fact]
    public void Protocol_ParsesNegativeVirtualScreenCoordinates()
    {
        string[] args =
        [
            MagnificationCaptureProtocol.HelperArgument,
            "--x", "-1920",
            "--y", "-120",
            "--width", "1920",
            "--height", "1080"
        ];

        Assert.True(MagnificationCaptureProtocol.TryParseBounds(args, out Rectangle bounds));
        Assert.Equal(new Rectangle(-1920, -120, 1920, 1080), bounds);
    }

    [Fact]
    public void Protocol_RejectsMissingOrOversizedBounds()
    {
        Assert.False(MagnificationCaptureProtocol.TryParseBounds(
            [MagnificationCaptureProtocol.HelperArgument, "--x", "0"], out _));
        Assert.False(MagnificationCaptureProtocol.TryParseBounds(
            [MagnificationCaptureProtocol.HelperArgument,
             "--x", "0", "--y", "0", "--width", "16385", "--height", "1"], out _));
    }
}
