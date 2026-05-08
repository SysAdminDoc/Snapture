using System.Runtime.Versioning;
using Windows.Graphics.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Snapture.App.Services;

/// <summary>
/// One-time prompt for <see cref="GraphicsCaptureAccessKind.Borderless"/>. Without it, Win11
/// 22H2+ paints a yellow border around every captured monitor/window. With it, captures look
/// the way users expect.
/// </summary>
[SupportedOSPlatform("windows10.0.22621.0")]
public static class BorderlessConsent
{
    public static async Task<bool> RequestAsync()
    {
        try
        {
            var status = await GraphicsCaptureAccess.RequestAccessAsync(GraphicsCaptureAccessKind.Borderless);
            return status == AppCapabilityAccessStatus.Allowed;
        }
        catch
        {
            return false;
        }
    }
}
