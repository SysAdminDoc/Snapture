using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>
/// Media Foundation COM interop for video encoding via IMFSinkWriter.
/// Mirrors the D3D11Interop pattern: thin P/Invokes + COM interfaces,
/// no NuGet dependency on Vortice / SharpDX / DirectN.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class MFInterop
{
    // ---- MF startup / shutdown ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFStartup(uint version, uint flags);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFShutdown();

    public const uint MF_VERSION = 0x00020070; // MF 2.0 (Win7+)

    // ---- Media type creation ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMediaType(out IMFMediaType ppMFType);

    // ---- Sink writer creation ----

    [DllImport("mfreadwrite.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    public static extern int MFCreateSinkWriterFromURL(
        string pwszOutputURL,
        nint pByteStream,
        IMFAttributes? pAttributes,
        out IMFSinkWriter ppSinkWriter);

    // ---- Sample + buffer creation ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateSample(out IMFSample ppIMFSample);

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateMemoryBuffer(uint cbMaxLength, out IMFMediaBuffer ppBuffer);

    // ---- Attributes creation (for sink writer config) ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFCreateAttributes(out IMFAttributes ppMFAttributes, uint cInitialSize);

    // ---- Transform enumeration ----

    [DllImport("mfplat.dll", ExactSpelling = true)]
    public static extern int MFTEnumEx(
        [In] ref Guid guidCategory,
        uint flags,
        nint pInputType,
        [In] ref MFT_REGISTER_TYPE_INFO pOutputType,
        out nint pppMFTActivate,
        out uint pcMFTActivate);

    [StructLayout(LayoutKind.Sequential)]
    public struct MFT_REGISTER_TYPE_INFO
    {
        public Guid guidMajorType;
        public Guid guidSubtype;
    }

    // ---- Well-known GUIDs ----

    // Major types
    public static readonly Guid MFMediaType_Video = new("73646976-0000-0010-8000-00AA00389B71");

    // Subtypes (output codecs)
    public static readonly Guid MFVideoFormat_AV1 = new("31305641-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_H264 = new("34363248-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_HEVC = new("43564548-0000-0010-8000-00AA00389B71");

    // Subtypes (input formats)
    public static readonly Guid MFVideoFormat_ARGB32 = new("00000015-0000-0010-8000-00AA00389B71");
    public static readonly Guid MFVideoFormat_RGB32 = new("00000016-0000-0010-8000-00AA00389B71");

    // Attribute keys
    public static readonly Guid MF_MT_MAJOR_TYPE = new("48EBA18E-F8C9-4687-BF11-0A74C9F96A8F");
    public static readonly Guid MF_MT_SUBTYPE = new("F7E34C9A-42E8-4714-B74B-CB29D72C35E5");
    public static readonly Guid MF_MT_AVG_BITRATE = new("20332624-FB0D-4D9E-BD0D-CBF6786C102E");
    public static readonly Guid MF_MT_INTERLACE_MODE = new("E2724BB8-E676-4806-B4B2-A8D6EFB44CCD");
    public static readonly Guid MF_MT_FRAME_SIZE = new("1652C33D-D6B2-4012-B834-72030849A37D");
    public static readonly Guid MF_MT_FRAME_RATE = new("C459A2E8-3D2C-4E44-B132-FEE5156C7BB0");
    public static readonly Guid MF_MT_PIXEL_ASPECT_RATIO = new("C6376A1E-8D0A-4027-BE45-6D9A0AD39BB6");

    // Sink writer attributes
    public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("A634A91C-822B-41B9-A494-4DE4643612B0");
    public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08B845D8-2B74-4AFE-9D53-BE16D2D5AE4F");

    // Transform categories and activation attributes
    public static readonly Guid MFT_CATEGORY_VIDEO_ENCODER = new("F79EAC7D-E545-4387-BDEE-D647D7BDE42A");
    public static readonly Guid MFT_FRIENDLY_NAME_Attribute = new("314FFBAE-5B41-4C95-9C19-4E7D586FACE3");
    public static readonly Guid MFT_ENUM_HARDWARE_VENDOR_ID_Attribute = new("3AECB0CC-035B-4BCC-8185-2B8D551EF3AF");
    public static readonly Guid MFT_TRANSFORM_CLSID_Attribute = new("6821C42B-65A4-4E82-99BC-9A88205ECD0C");

    public const uint MFT_ENUM_FLAG_SYNCMFT = 0x00000001;
    public const uint MFT_ENUM_FLAG_ASYNCMFT = 0x00000002;
    public const uint MFT_ENUM_FLAG_HARDWARE = 0x00000004;
    public const uint MFT_ENUM_FLAG_LOCALMFT = 0x00000010;
    public const uint MFT_ENUM_FLAG_TRANSCODE_ONLY = 0x00000020;
    public const uint MFT_ENUM_FLAG_SORTANDFILTER = 0x00000040;

    // Interlace mode
    public const uint MFVideoInterlace_Progressive = 2;

    // ---- COM interfaces ----

    [ComImport, Guid("2CD2D921-C447-44A7-A13C-4ADABFC247E3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFAttributes
    {
        // Slots 0-2: IUnknown
        // IMFAttributes begins at slot 3
        void GetItem([In] ref Guid guidKey, nint pValue);
        void GetItemType([In] ref Guid guidKey, out uint pType);
        void CompareItem([In] ref Guid guidKey, nint Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        void CompareAllItems(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        void GetUINT32([In] ref Guid guidKey, out uint punValue);
        void GetUINT64([In] ref Guid guidKey, out ulong punValue);
        void GetDouble([In] ref Guid guidKey, out double pfValue);
        void GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        void GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        void GetString([In] ref Guid guidKey, nint pwszValue, uint cchBufSize, nint pcchLength);
        void GetAllocatedString([In] ref Guid guidKey, out nint ppwszValue, out uint pcchLength);
        void GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        void GetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize);
        void GetAllocatedBlob([In] ref Guid guidKey, out nint ppBuf, out uint pcbSize);
        void GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out nint ppv);
        void SetItem([In] ref Guid guidKey, nint Value);
        void DeleteItem([In] ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32([In] ref Guid guidKey, uint unValue);
        void SetUINT64([In] ref Guid guidKey, ulong unValue);
        void SetDouble([In] ref Guid guidKey, double fValue);
        void SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        void SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize);
        void SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue);
        void CopyAllItems(IMFAttributes pDest);
    }

    [ComImport, Guid("44AE0FA8-EA31-4109-8D2E-4CAE4997C555"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaType : IMFAttributes
    {
        // Inherits all IMFAttributes slots, adds:
        new void GetItem([In] ref Guid guidKey, nint pValue);
        new void GetItemType([In] ref Guid guidKey, out uint pType);
        new void CompareItem([In] ref Guid guidKey, nint Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new void CompareAllItems(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        new void GetUINT32([In] ref Guid guidKey, out uint punValue);
        new void GetUINT64([In] ref Guid guidKey, out ulong punValue);
        new void GetDouble([In] ref Guid guidKey, out double pfValue);
        new void GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        new void GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        new void GetString([In] ref Guid guidKey, nint pwszValue, uint cchBufSize, nint pcchLength);
        new void GetAllocatedString([In] ref Guid guidKey, out nint ppwszValue, out uint pcchLength);
        new void GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        new void GetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize);
        new void GetAllocatedBlob([In] ref Guid guidKey, out nint ppBuf, out uint pcbSize);
        new void GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out nint ppv);
        new void SetItem([In] ref Guid guidKey, nint Value);
        new void DeleteItem([In] ref Guid guidKey);
        new void DeleteAllItems();
        new void SetUINT32([In] ref Guid guidKey, uint unValue);
        new void SetUINT64([In] ref Guid guidKey, ulong unValue);
        new void SetDouble([In] ref Guid guidKey, double fValue);
        new void SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        new void SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        new void SetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize);
        new void SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        new void LockStore();
        new void UnlockStore();
        new void GetCount(out uint pcItems);
        new void GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue);
        new void CopyAllItems(IMFAttributes pDest);
        // IMFMediaType-specific
        void GetMajorType(out Guid pguidMajorType);
        [PreserveSig] int IsCompressedFormat([MarshalAs(UnmanagedType.Bool)] out bool pfCompressed);
        [PreserveSig] int IsEqual(IMFMediaType pIMediaType, out uint pdwFlags);
        void GetRepresentation(Guid guidRepresentation, out nint ppvRepresentation);
        void FreeRepresentation(Guid guidRepresentation, nint pvRepresentation);
    }

    [ComImport, Guid("C40A00F2-B93A-4D80-AE8C-5A1C634F58E4"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSample
    {
        // IMFAttributes (inherited via IMFSample : IMFAttributes)
        void GetItem([In] ref Guid guidKey, nint pValue);
        void GetItemType([In] ref Guid guidKey, out uint pType);
        void CompareItem([In] ref Guid guidKey, nint Value, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        void CompareAllItems(IMFAttributes pTheirs, uint matchType, [MarshalAs(UnmanagedType.Bool)] out bool pbResult);
        void GetUINT32([In] ref Guid guidKey, out uint punValue);
        void GetUINT64([In] ref Guid guidKey, out ulong punValue);
        void GetDouble([In] ref Guid guidKey, out double pfValue);
        void GetGUID([In] ref Guid guidKey, out Guid pguidValue);
        void GetStringLength([In] ref Guid guidKey, out uint pcchLength);
        void GetString([In] ref Guid guidKey, nint pwszValue, uint cchBufSize, nint pcchLength);
        void GetAllocatedString([In] ref Guid guidKey, out nint ppwszValue, out uint pcchLength);
        void GetBlobSize([In] ref Guid guidKey, out uint pcbBlobSize);
        void GetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize, nint pcbBlobSize);
        void GetAllocatedBlob([In] ref Guid guidKey, out nint ppBuf, out uint pcbSize);
        void GetUnknown([In] ref Guid guidKey, [In] ref Guid riid, out nint ppv);
        void SetItem([In] ref Guid guidKey, nint Value);
        void DeleteItem([In] ref Guid guidKey);
        void DeleteAllItems();
        void SetUINT32([In] ref Guid guidKey, uint unValue);
        void SetUINT64([In] ref Guid guidKey, ulong unValue);
        void SetDouble([In] ref Guid guidKey, double fValue);
        void SetGUID([In] ref Guid guidKey, [In] ref Guid guidValue);
        void SetString([In] ref Guid guidKey, [MarshalAs(UnmanagedType.LPWStr)] string wszValue);
        void SetBlob([In] ref Guid guidKey, nint pBuf, uint cbBufSize);
        void SetUnknown([In] ref Guid guidKey, [MarshalAs(UnmanagedType.IUnknown)] object pUnknown);
        void LockStore();
        void UnlockStore();
        void GetCount(out uint pcItems);
        void GetItemByIndex(uint unIndex, out Guid pguidKey, nint pValue);
        void CopyAllItems(IMFAttributes pDest);
        // IMFSample-specific
        void GetSampleFlags(out uint pdwSampleFlags);
        void SetSampleFlags(uint dwSampleFlags);
        void GetSampleTime(out long phnsSampleTime);
        void SetSampleTime(long hnsSampleTime);
        void GetSampleDuration(out long phnsSampleDuration);
        void SetSampleDuration(long hnsSampleDuration);
        void GetBufferCount(out uint pdwBufferCount);
        void GetBufferByIndex(uint dwIndex, out IMFMediaBuffer ppBuffer);
        void ConvertToContiguousBuffer(out IMFMediaBuffer ppBuffer);
        void AddBuffer(IMFMediaBuffer pBuffer);
        void RemoveBufferByIndex(uint dwIndex);
        void RemoveAllBuffers();
        void GetTotalLength(out uint pcbTotalLength);
        void CopyToBuffer(IMFMediaBuffer pBuffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFMediaBuffer
    {
        void Lock(out nint ppbBuffer, out uint pcbMaxLength, out uint pcbCurrentLength);
        void Unlock();
        void GetCurrentLength(out uint pcbCurrentLength);
        void SetCurrentLength(uint cbCurrentLength);
        void GetMaxLength(out uint pcbMaxLength);
    }

    [ComImport, Guid("3137F1CD-FE28-4DC2-A6DC-ACF8E9B4B3B4"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFSinkWriter
    {
        void AddStream(IMFMediaType pTargetMediaType, out uint pdwStreamIndex);
        void SetInputMediaType(uint dwStreamIndex, IMFMediaType pInputMediaType, IMFAttributes? pEncodingParameters);
        void BeginWriting();
        void WriteSample(uint dwStreamIndex, IMFSample pSample);
        void SendStreamTick(uint dwStreamIndex, long hnsTimestamp);
        void PlaceMarker(uint dwStreamIndex, nint pvContext);
        void NotifyEndOfSegment(uint dwStreamIndex);
        void Flush(uint dwStreamIndex);
        [PreserveSig] int Finalize_();
        void GetServiceForStream(uint dwStreamIndex, [In] ref Guid guidService, [In] ref Guid riid, out nint ppvObject);
        void GetStatistics(uint dwStreamIndex, out nint pStats);
    }

    // ---- Helpers ----

    public static ulong Pack2x32(uint hi, uint lo) => ((ulong)hi << 32) | lo;
}
