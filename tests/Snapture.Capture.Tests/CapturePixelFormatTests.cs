using System.Buffers.Binary;

namespace Snapture.Capture.Tests;

public class CapturePixelFormatTests
{
    [Fact]
    public void Resolve_UsesFp16OnlyForHdrOutput()
    {
        var hdr = CapturePixelFormatPolicy.Resolve(
            isHdrSupported: true,
            D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
        var sdr = CapturePixelFormatPolicy.Resolve(
            isHdrSupported: false,
            D3D11Interop.DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020);
        var wrongSpace = CapturePixelFormatPolicy.Resolve(isHdrSupported: true, 0);

        Assert.True(hdr.UsesFp16);
        Assert.Equal(CapturePixelFormat.Rgba16Float, hdr.PixelFormat);
        Assert.False(sdr.UsesFp16);
        Assert.False(wrongSpace.UsesFp16);
    }

    [Fact]
    public void ConvertRgba16FloatToBgra_ToneMapsHdrValues()
    {
        Span<byte> source = stackalloc byte[8];
        WriteHalf(source, 0, 2f);
        WriteHalf(source, 2, 1f);
        WriteHalf(source, 4, 0.5f);
        WriteHalf(source, 6, 1f);
        Span<byte> destination = stackalloc byte[4];

        HdrFrameConverter.ConvertRgba16FloatToBgra(source, 8, destination, 4, 0, 0, 1, 1);

        Assert.Equal(255, destination[3]);
        Assert.True(destination[2] > destination[1]);
        Assert.True(destination[1] > destination[0]);
        Assert.True(destination[2] < 255);
    }

    [Fact]
    public void ResolveForMonitor_ReturnsAValidDecisionWithoutRequiringHdrHardware()
    {
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            var decision = CapturePixelFormatPolicy.ResolveForMonitor(monitor.Handle);
            Assert.Contains(decision.PixelFormat, new[]
            {
                CapturePixelFormat.Bgra8,
                CapturePixelFormat.Rgba16Float
            });
        }
    }

    private static void WriteHalf(Span<byte> destination, int offset, float value)
        => BinaryPrimitives.WriteUInt16LittleEndian(
            destination.Slice(offset, sizeof(ushort)),
            BitConverter.HalfToUInt16Bits((Half)value));
}
