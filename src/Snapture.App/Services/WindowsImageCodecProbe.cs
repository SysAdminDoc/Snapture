using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.App.Services;

/// <summary>
/// Minimal WIC probe for the optional HEIF Image Extension. Creating the encoder
/// is enough to distinguish the installed extension from ImageMagick's bundled
/// delegates, which are intentionally not treated as Windows codec availability.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class WindowsImageCodecProbe
{
    private const uint ClsctxInprocServer = 0x1;
    private static readonly Guid ClsidWicImagingFactory = new("CACAF262-9370-4615-A13B-9F5539DA4C0A");
    private static readonly Guid IidWicImagingFactory = new("EC5EC8A9-C395-4314-9C77-54D7A935FF70");
    private static readonly Guid GuidContainerFormatHeif = new("E1E62521-6787-405B-A339-500715B5763F");

    public static unsafe bool IsHeifEncoderAvailable()
    {
        if (!OperatingSystem.IsWindows()) return false;

        nint factory = 0;
        nint encoder = 0;
        try
        {
            int hr = CoCreateInstance(
                in ClsidWicImagingFactory,
                0,
                ClsctxInprocServer,
                in IidWicImagingFactory,
                out factory);
            if (hr < 0 || factory == 0) return false;

            // IWICImagingFactory::CreateEncoder is the fourth interface method,
            // therefore vtable slot 6 after QueryInterface/AddRef/Release.
            var vtable = *(nint**)factory;
            var createEncoder = (delegate* unmanaged[Stdcall]<nint, in Guid, nint, out nint, int>)vtable[6];
            hr = createEncoder(factory, in GuidContainerFormatHeif, 0, out encoder);
            return hr >= 0 && encoder != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (encoder != 0) Marshal.Release(encoder);
            if (factory != 0) Marshal.Release(factory);
        }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        nint pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out nint ppv);
}
