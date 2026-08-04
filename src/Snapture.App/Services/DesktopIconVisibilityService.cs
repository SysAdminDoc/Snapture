using System.Runtime.InteropServices;

namespace Snapture.App.Services;

/// <summary>Temporarily hides the shell desktop icon list and restores its prior state.</summary>
public static class DesktopIconVisibilityService
{
    internal static IDisposable? TryHide(IDesktopIconController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);

        nint list = controller.FindDesktopIconList();
        if (list == nint.Zero || !controller.IsWindow(list) || !controller.IsVisible(list))
            return null;

        try
        {
            controller.SetVisible(list, false);
            if (controller.IsVisible(list))
            {
                controller.SetVisible(list, true);
                return null;
            }

            return new RestoreScope(controller, list);
        }
        catch
        {
            try { controller.SetVisible(list, true); } catch { }
            return null;
        }
    }

    public static IDisposable? TryHide()
    {
        try
        {
            return TryHide(new NativeDesktopIconController());
        }
        catch
        {
            return null;
        }
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly IDesktopIconController _controller;
        private readonly nint _list;
        private int _disposed;

        public RestoreScope(IDesktopIconController controller, nint list)
        {
            _controller = controller;
            _list = list;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;
            try
            {
                if (_controller.IsWindow(_list))
                    _controller.SetVisible(_list, true);
            }
            catch { }
        }
    }

    internal interface IDesktopIconController
    {
        nint FindDesktopIconList();
        bool IsWindow(nint handle);
        bool IsVisible(nint handle);
        void SetVisible(nint handle, bool visible);
    }

    private sealed class NativeDesktopIconController : IDesktopIconController
    {
        private const int SwHide = 0;
        private const int SwShow = 5;

        public nint FindDesktopIconList()
        {
            var progman = FindWindow("Progman", null);
            var defView = FindWindowEx(progman, nint.Zero, "SHELLDLL_DefView", null);
            if (defView != nint.Zero)
            {
                var candidate = FindWindowEx(defView, nint.Zero, "SysListView32", null);
                if (candidate != nint.Zero)
                    return candidate;
            }

            nint list = nint.Zero;
            EnumWindows((topLevel, _) =>
            {
                var workerDefView = FindWindowEx(topLevel, nint.Zero, "SHELLDLL_DefView", null);
                if (workerDefView == nint.Zero)
                    return true;

                list = FindWindowEx(workerDefView, nint.Zero, "SysListView32", null);
                return list == nint.Zero;
            }, nint.Zero);
            return list;
        }

        public bool IsWindow(nint handle) => IsWindowNative(handle);
        public bool IsVisible(nint handle) => IsWindowVisible(handle);
        public void SetVisible(nint handle, bool visible) =>
            ShowWindow(handle, visible ? SwShow : SwHide);

        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint FindWindowEx(
            nint hWndParent,
            nint hWndChildAfter,
            string? lpszClass,
            string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

        [DllImport("user32.dll", EntryPoint = "IsWindow")]
        private static extern bool IsWindowNative(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(nint hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    }
}
