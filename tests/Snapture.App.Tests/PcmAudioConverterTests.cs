using System.Buffers.Binary;
using NAudio.Wave;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PcmAudioConverterTests
{
    [TestMethod]
    public void ConvertToStereo48_DuplicatesMonoPcm16()
    {
        var data = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(0, 2), 16384);
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(2, 2), -16384);

        var converted = PcmAudioConverter.ConvertToStereo48(data, new WaveFormat(48_000, 16, 1));

        Assert.IsTrue(converted.Samples.Length == 4);
        Assert.AreEqual(0.5f, converted.Samples[0], 0.0001f);
        Assert.AreEqual(0.5f, converted.Samples[1], 0.0001f);
        Assert.AreEqual(-0.5f, converted.Samples[2], 0.0001f);
        Assert.AreEqual(-0.5f, converted.Samples[3], 0.0001f);
        Assert.AreEqual(0.5f, converted.Peak, 0.0001f);
    }

    [TestMethod]
    public void ConvertToStereo48_ResamplesToOutputRate()
    {
        var data = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(data, 32767);

        var converted = PcmAudioConverter.ConvertToStereo48(data, new WaveFormat(24_000, 16, 1));

        Assert.IsTrue(converted.Samples.Length == 4);
    }

    [TestMethod]
    public void FloatStereoToPcm16_ClipsAndWritesLittleEndianSamples()
    {
        byte[] pcm = PcmAudioConverter.FloatStereoToPcm16(new[] { -2f, 0f, 0.5f, 2f });

        Assert.AreEqual(short.MinValue, BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(0, 2)));
        Assert.AreEqual(0, BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(2, 2)));
        Assert.AreEqual(16384, BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(4, 2)));
        Assert.AreEqual(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(6, 2)));
    }
}
