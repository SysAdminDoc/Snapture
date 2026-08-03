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
    public void ToneMapOperatorParser_NormalizesUnknownValuesToReinhard()
    {
        Assert.Equal(HdrToneMapOperator.Reinhard, HdrToneMapOperators.Parse(null));
        Assert.Equal(HdrToneMapOperator.Aces, HdrToneMapOperators.Parse(" ACES "));
        Assert.Equal(HdrToneMapOperator.Hable, HdrToneMapOperators.Parse("hable"));
        Assert.Equal(HdrToneMapOperator.Reinhard, HdrToneMapOperators.Parse("future-operator"));
        Assert.Equal("reinhard", HdrToneMapOperators.ToKey(HdrToneMapOperator.Reinhard));
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
    public void ToneMapOperators_ProduceBoundedDistinctCurves()
    {
        Span<byte> source = stackalloc byte[8];
        WriteHalf(source, 0, 4f);
        WriteHalf(source, 2, 4f);
        WriteHalf(source, 4, 4f);
        WriteHalf(source, 6, 1f);
        Span<byte> reinhard = stackalloc byte[4];
        Span<byte> aces = stackalloc byte[4];
        Span<byte> hable = stackalloc byte[4];

        HdrFrameConverter.ConvertRgba16FloatToBgra(source, 8, reinhard, 4, 0, 0, 1, 1);
        HdrFrameConverter.ConvertRgba16FloatToBgra(source, 8, aces, 4, 0, 0, 1, 1, HdrToneMapOperator.Aces);
        HdrFrameConverter.ConvertRgba16FloatToBgra(source, 8, hable, 4, 0, 0, 1, 1, HdrToneMapOperator.Hable);

        Assert.All(new[] { reinhard[0], reinhard[1], reinhard[2], aces[0], aces[1], aces[2], hable[0], hable[1], hable[2] },
            value => Assert.InRange(value, (byte)0, (byte)255));
        Assert.NotEqual(reinhard[0], aces[0]);
        Assert.NotEqual(reinhard[0], hable[0]);
    }

    [Fact]
    public void ColorCorrectorToggle_UsesToneMapOrDirectScRgbClamp()
    {
        Span<byte> source = stackalloc byte[8];
        WriteHalf(source, 0, 4f);
        WriteHalf(source, 2, 4f);
        WriteHalf(source, 4, 4f);
        WriteHalf(source, 6, 1f);
        Span<byte> corrected = stackalloc byte[4];
        Span<byte> uncorrected = stackalloc byte[4];

        HdrFrameConverter.ConvertRgba16FloatToBgra(
            source, 8, corrected, 4, 0, 0, 1, 1,
            HdrToneMapOperator.Reinhard, applyColorCorrection: true);
        HdrFrameConverter.ConvertRgba16FloatToBgra(
            source, 8, uncorrected, 4, 0, 0, 1, 1,
            HdrToneMapOperator.Reinhard, applyColorCorrection: false);

        Assert.True(corrected[2] < 255);
        Assert.Equal(255, uncorrected[2]);
        Assert.Equal(255, uncorrected[3]);
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
