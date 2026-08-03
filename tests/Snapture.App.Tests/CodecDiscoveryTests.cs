using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CodecDiscoveryTests
{
    [TestMethod]
    public void BuildCandidates_PrefersHardwareAndFallsBackThroughSoftwareCodecs()
    {
        var av1 = new[] { Encoder("AV1 hardware") };
        var hevcHardware = new[] { Encoder("HEVC hardware") };
        var hevcSoftware = new[] { Encoder("HEVC software") };
        var h264Hardware = Array.Empty<MediaFoundationVideoCodecDiscovery.EncoderInfo>();
        var h264Software = new[] { Encoder("H.264 software") };

        var candidates = MediaFoundationVideoCodecDiscovery.BuildCandidates(
            av1, hevcHardware, hevcSoftware, h264Hardware, h264Software);

        Assert.HasCount(4, candidates);
        Assert.AreEqual("AV1", candidates[0].CodecName);
        Assert.IsTrue(candidates[0].UseHardwareTransforms);
        Assert.AreEqual("HEVC", candidates[1].CodecName);
        Assert.IsTrue(candidates[1].UseHardwareTransforms);
        Assert.AreEqual("HEVC", candidates[2].CodecName);
        Assert.IsFalse(candidates[2].UseHardwareTransforms);
        Assert.AreEqual("H.264", candidates[3].CodecName);
    }

    [TestMethod]
    public void CodecAvailability_ExplainsMissingStoreExtensions()
    {
        var availability = new MediaFoundationVideoCodecDiscovery.CodecAvailability(
            Av1EncoderAvailable: false,
            HevcEncoderAvailable: true,
            H264EncoderAvailable: true,
            HeifEncoderAvailable: false);

        CollectionAssert.AreEqual(
            new[] { "AV1 Video Extension", "HEIF Image Extension" },
            availability.MissingStoreExtensions.ToArray());
        StringAssert.Contains(availability.Description, "recording will fall back");
        StringAssert.Contains(availability.Description, "AV1 Video Extension");
    }

    [TestMethod]
    public void CodecAvailability_WithAllExtensions_IsHealthy()
    {
        var availability = new MediaFoundationVideoCodecDiscovery.CodecAvailability(
            Av1EncoderAvailable: true,
            HevcEncoderAvailable: true,
            H264EncoderAvailable: true,
            HeifEncoderAvailable: true);

        Assert.AreEqual("AV1, HEVC, and HEIF extensions detected.", availability.Description);
        Assert.IsEmpty(availability.MissingStoreExtensions);
    }

    [TestMethod]
    public void Discover_ReturnsAvailabilityAfterMediaFoundationStartup()
    {
        int hr = MFInterop.MFStartup(MFInterop.MF_VERSION, 0);
        Assert.IsGreaterThanOrEqualTo(0, hr);
        try
        {
            var result = MediaFoundationVideoCodecDiscovery.Discover();

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Availability);
            Assert.IsNotNull(result.Candidates);
        }
        finally
        {
            MFInterop.MFShutdown();
        }
    }

    private static MediaFoundationVideoCodecDiscovery.EncoderInfo Encoder(string name)
        => new(name, null, "Test", Guid.NewGuid(), IsHardware: name.Contains("hardware"));
}
