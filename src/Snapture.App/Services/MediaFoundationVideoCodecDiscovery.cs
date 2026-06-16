using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Serilog;
using Snapture.Capture;

namespace Snapture.App.Services;

[SupportedOSPlatform("windows10.0.17763.0")]
public static class MediaFoundationVideoCodecDiscovery
{
    public sealed record EncoderInfo(
        string Name,
        string? VendorId,
        string VendorName,
        Guid TransformClsid,
        bool IsHardware);

    public sealed record EncoderCandidate(
        string CodecName,
        Guid Subtype,
        bool UseHardwareTransforms,
        IReadOnlyList<EncoderInfo> Encoders)
    {
        public string DisplayName =>
            UseHardwareTransforms ? $"{CodecName} hardware" : $"{CodecName} software";

        public string EncoderSummary
        {
            get
            {
                if (Encoders.Count == 0)
                    return DisplayName;

                var first = Encoders[0];
                return string.IsNullOrWhiteSpace(first.VendorId)
                    ? $"{DisplayName} ({first.Name})"
                    : $"{DisplayName} ({first.VendorName}, {first.Name})";
            }
        }
    }

    public static IReadOnlyList<EncoderCandidate> GetPreferredEncodingCandidates()
    {
        var av1Hardware = EnumerateEncoders(MFInterop.MFVideoFormat_AV1, hardwareOnly: true);
        var hevcHardware = EnumerateEncoders(MFInterop.MFVideoFormat_HEVC, hardwareOnly: true);
        var hevcSoftware = EnumerateEncoders(MFInterop.MFVideoFormat_HEVC, hardwareOnly: false);
        var h264Hardware = EnumerateEncoders(MFInterop.MFVideoFormat_H264, hardwareOnly: true);
        var h264Software = EnumerateEncoders(MFInterop.MFVideoFormat_H264, hardwareOnly: false);

        var candidates = new List<EncoderCandidate>(3);

        // AV1 software encoding is intentionally not considered. It is too slow for screen capture.
        if (av1Hardware.Count > 0)
            candidates.Add(new EncoderCandidate("AV1", MFInterop.MFVideoFormat_AV1, true, av1Hardware));

        if (hevcHardware.Count > 0)
            candidates.Add(new EncoderCandidate("HEVC", MFInterop.MFVideoFormat_HEVC, true, hevcHardware));
        if (hevcSoftware.Count > 0)
            candidates.Add(new EncoderCandidate("HEVC", MFInterop.MFVideoFormat_HEVC, false, hevcSoftware));

        if (h264Hardware.Count > 0)
            candidates.Add(new EncoderCandidate("H.264", MFInterop.MFVideoFormat_H264, true, h264Hardware));
        if (h264Software.Count > 0)
            candidates.Add(new EncoderCandidate("H.264", MFInterop.MFVideoFormat_H264, false, h264Software));

        Log.Information(
            "VideoRecorder.CodecDiscovery AV1Hardware={Av1Hardware} HEVCHardware={HevcHardware} HEVCSoftware={HevcSoftware} H264Hardware={H264Hardware} H264Software={H264Software}",
            FormatEncoders(av1Hardware),
            FormatEncoders(hevcHardware),
            FormatEncoders(hevcSoftware),
            FormatEncoders(h264Hardware),
            FormatEncoders(h264Software));

        return candidates;
    }

    private static IReadOnlyList<EncoderInfo> EnumerateEncoders(Guid subtype, bool hardwareOnly)
    {
        var category = MFInterop.MFT_CATEGORY_VIDEO_ENCODER;
        var outputType = new MFInterop.MFT_REGISTER_TYPE_INFO
        {
            guidMajorType = MFInterop.MFMediaType_Video,
            guidSubtype = subtype
        };

        uint flags = hardwareOnly
            ? MFInterop.MFT_ENUM_FLAG_HARDWARE |
              MFInterop.MFT_ENUM_FLAG_TRANSCODE_ONLY |
              MFInterop.MFT_ENUM_FLAG_SORTANDFILTER
            : MFInterop.MFT_ENUM_FLAG_SYNCMFT |
              MFInterop.MFT_ENUM_FLAG_ASYNCMFT |
              MFInterop.MFT_ENUM_FLAG_LOCALMFT |
              MFInterop.MFT_ENUM_FLAG_TRANSCODE_ONLY |
              MFInterop.MFT_ENUM_FLAG_SORTANDFILTER;

        int hr = MFInterop.MFTEnumEx(
            ref category,
            flags,
            0,
            ref outputType,
            out nint activateArray,
            out uint activateCount);

        if (hr < 0)
        {
            Log.Debug("VideoRecorder.CodecDiscovery.MFTEnumExFailed {Subtype} {HardwareOnly} {HResult:X8}",
                subtype, hardwareOnly, hr);
            return [];
        }

        if (activateArray == 0 || activateCount == 0)
            return [];

        var encoders = new List<EncoderInfo>((int)activateCount);
        try
        {
            for (int i = 0; i < activateCount; i++)
            {
                nint activatePtr = Marshal.ReadIntPtr(activateArray, i * IntPtr.Size);
                if (activatePtr == 0)
                    continue;

                object? rcw = null;
                try
                {
                    rcw = Marshal.GetObjectForIUnknown(activatePtr);
                    if (rcw is not MFInterop.IMFAttributes attributes)
                        continue;

                    string name = ReadString(attributes, MFInterop.MFT_FRIENDLY_NAME_Attribute)
                        ?? "Media Foundation encoder";
                    string? vendorId = ReadString(attributes, MFInterop.MFT_ENUM_HARDWARE_VENDOR_ID_Attribute);
                    Guid clsid = ReadGuid(attributes, MFInterop.MFT_TRANSFORM_CLSID_Attribute);

                    encoders.Add(new EncoderInfo(
                        name,
                        vendorId,
                        ClassifyVendor(vendorId, name),
                        clsid,
                        hardwareOnly));
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "VideoRecorder.CodecDiscovery.ReadEncoderFailed");
                }
                finally
                {
                    if (rcw is not null)
                        Marshal.ReleaseComObject(rcw);
                    Marshal.Release(activatePtr);
                }
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(activateArray);
        }

        return encoders;
    }

    private static string? ReadString(MFInterop.IMFAttributes attributes, Guid key)
    {
        nint value = 0;
        try
        {
            attributes.GetAllocatedString(ref key, out value, out uint length);
            return value == 0 || length == 0 ? null : Marshal.PtrToStringUni(value, (int)length);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (value != 0)
                Marshal.FreeCoTaskMem(value);
        }
    }

    private static Guid ReadGuid(MFInterop.IMFAttributes attributes, Guid key)
    {
        try
        {
            attributes.GetGUID(ref key, out var value);
            return value;
        }
        catch (COMException)
        {
            return Guid.Empty;
        }
    }

    private static string ClassifyVendor(string? vendorId, string name)
    {
        string probe = $"{vendorId} {name}".ToUpperInvariant();

        if (probe.Contains("VEN_10DE") || probe.Contains("NVIDIA") || probe.Contains("NVENC"))
            return "NVIDIA NVENC";
        if (probe.Contains("VEN_8086") || probe.Contains("INTEL") || probe.Contains("QSV"))
            return "Intel QSV";
        if (probe.Contains("VEN_1002") || probe.Contains("AMD") || probe.Contains("VCN"))
            return "AMD VCN";
        if (probe.Contains("VEN_17CB") || probe.Contains("VEN_5143") || probe.Contains("QUALCOMM"))
            return "Qualcomm MFT";

        return string.IsNullOrWhiteSpace(vendorId) ? "Media Foundation" : vendorId;
    }

    private static string FormatEncoders(IReadOnlyList<EncoderInfo> encoders)
    {
        if (encoders.Count == 0)
            return "none";

        return string.Join(", ", encoders.Select(e =>
            string.IsNullOrWhiteSpace(e.VendorId)
                ? e.Name
                : $"{e.VendorName}/{e.VendorId}/{e.Name}"));
    }
}
