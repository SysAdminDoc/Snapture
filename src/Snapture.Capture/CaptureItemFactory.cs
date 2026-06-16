using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Windows.Graphics.Capture;

namespace Snapture.Capture;

/// <summary>
/// Picker bypass for <see cref="GraphicsCaptureItem"/>. The WinRT projection only exposes the
/// system picker UI; programmatic capture goes through the Win32 interop interface
/// <c>IGraphicsCaptureItemInterop</c> on the activation factory.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public static class CaptureItemFactory
{
    private static readonly Guid IID_IGraphicsCaptureItemInterop =
        new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");

    /// <summary>Canonical WinRT IID for <see cref="GraphicsCaptureItem"/>.</summary>
    private static readonly Guid IID_GraphicsCaptureItem =
        new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    public static GraphicsCaptureItem? CreateForWindow(nint hwnd)
    {
        if (hwnd == 0) return null;
        var interop = GetInterop();
        if (interop is null) return null;
        var iid = IID_GraphicsCaptureItem;
        int hr = interop.CreateForWindow(hwnd, in iid, out nint pItem);
        if (hr < 0 || pItem == 0) return null;
        try
        {
            return MarshalToCaptureItem(pItem);
        }
        finally
        {
            Marshal.Release(pItem);
        }
    }

    public static GraphicsCaptureItem? CreateForMonitor(nint hMonitor)
    {
        if (hMonitor == 0) return null;
        var interop = GetInterop();
        if (interop is null) return null;
        var iid = IID_GraphicsCaptureItem;
        int hr = interop.CreateForMonitor(hMonitor, in iid, out nint pItem);
        if (hr < 0 || pItem == 0) return null;
        try
        {
            return MarshalToCaptureItem(pItem);
        }
        finally
        {
            Marshal.Release(pItem);
        }
    }

    private static IGraphicsCaptureItemInterop? GetInterop()
    {
        try
        {
            // Acquire the activation factory. Because IGraphicsCaptureItemInterop is an
            // IUnknown-derived interface (not IInspectable), this works with Marshal.GetObjectForIUnknown.
            nint factoryPtr = RoGetActivationFactory(
                "Windows.Graphics.Capture.GraphicsCaptureItem",
                IID_IGraphicsCaptureItemInterop);
            try
            {
                return (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }
        }
        catch
        {
            return null;
        }
    }

    private static GraphicsCaptureItem? MarshalToCaptureItem(nint pItem)
    {
        // CsWinRT marshals projected types from raw pointers via Marshal.GetObjectForIUnknown
        // because the projected type is an RCW-aware proxy. If that fails, return null and
        // let the caller fall back.
        try
        {
            return (GraphicsCaptureItem)Marshal.GetObjectForIUnknown(pItem);
        }
        catch
        {
            return null;
        }
    }

    [DllImport("api-ms-win-core-winrt-l1-1-0.dll", PreserveSig = false)]
    private static extern nint RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] in Guid iid);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(nint window, [In] in Guid iid, out nint result);
        [PreserveSig] int CreateForMonitor(nint monitor, [In] in Guid iid, out nint result);
    }
}
