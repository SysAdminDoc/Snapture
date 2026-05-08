using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Resolves the capture engine the user wants. <c>auto</c> picks WinRT when supported,
/// GDI otherwise. Engine names are lower-case, English, persisted in settings.json so
/// fleet configs travel cleanly.
/// </summary>
public static class CaptureEngineFactory
{
    public const string Auto = "auto";
    public const string WinRt = "winrt";
    public const string Gdi = "gdi";

    public static (ICaptureEngine Engine, string ActualName) Create(string preferred)
    {
        var key = (preferred ?? Auto).Trim().ToLowerInvariant();
        switch (key)
        {
            case WinRt:
                if (WinRtCaptureEngine.IsSupported)
                    return (new WinRtCaptureEngine(), WinRt);
                return (new GdiCaptureEngine(), Gdi);
            case Gdi:
                return (new GdiCaptureEngine(), Gdi);
            case Auto:
            default:
                if (WinRtCaptureEngine.IsSupported)
                    return (new WinRtCaptureEngine(), WinRt);
                return (new GdiCaptureEngine(), Gdi);
        }
    }
}
