using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Snapture.Capture;

/// <summary>Command-line contract shared by the parent and STA helper process.</summary>
public static class MagnificationCaptureProtocol
{
    public const string HelperArgument = "--magnification-helper";

    public static bool IsHelperRequest(IReadOnlyList<string> args)
        => args.Any(arg => string.Equals(arg, HelperArgument, StringComparison.OrdinalIgnoreCase));

    public static bool TryParseBounds(IReadOnlyList<string> args, out Rectangle bounds)
    {
        bounds = default;
        if (!IsHelperRequest(args)
            || !TryGetInt(args, "--x", out int x)
            || !TryGetInt(args, "--y", out int y)
            || !TryGetInt(args, "--width", out int width)
            || !TryGetInt(args, "--height", out int height)
            || width <= 0
            || height <= 0
            || width > 16_384
            || height > 16_384
            || (long)width * height > 64_000_000)
            return false;

        bounds = new Rectangle(x, y, width, height);
        return true;
    }

    private static bool TryGetInt(IReadOnlyList<string> args, string key, out int value)
    {
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (!string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) continue;
            return int.TryParse(
                args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        value = 0;
        return false;
    }
}

/// <summary>
/// Captures a desktop rectangle through a short-lived STA WPF helper. The helper
/// hosts the Windows Magnification control, which samples the composed desktop and
/// can include layered/topmost overlays that WGC omits from a monitor frame.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MagnificationCapture
{
    private const long MaximumOutputBytes = 256L * 1024 * 1024;

    public static Bitmap? TryCapture(Rectangle bounds, CancellationToken ct = default)
    {
        try
        {
            return CaptureAsync(bounds, ct).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<Bitmap?> CaptureAsync(Rectangle bounds, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows() || !IsValidBounds(bounds))
            return null;

        var startInfo = CreateHelperStartInfo(bounds);
        if (startInfo is null)
            return null;

        using var process = Process.Start(startInfo);
        if (process is null)
            return null;

        using var output = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));

        Task copyOutput = CopyBoundedAsync(
            process.StandardOutput.BaseStream, output, MaximumOutputBytes, timeout.Token);
        Task<string> readError = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await Task.WhenAll(
                process.WaitForExitAsync(timeout.Token), copyOutput, readError)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StopHelper(process);
            if (ct.IsCancellationRequested) throw;
            return null;
        }
        catch
        {
            StopHelper(process);
            return null;
        }

        if (process.ExitCode != 0 || output.Length == 0)
            return null;

        try
        {
            output.Position = 0;
            using var source = new Bitmap(output);
            return new Bitmap(source);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsValidBounds(Rectangle bounds)
        => bounds.Width > 0
            && bounds.Height > 0
            && bounds.Width <= 16_384
            && bounds.Height <= 16_384
            && (long)bounds.Width * bounds.Height <= 64_000_000;

    private static ProcessStartInfo? CreateHelperStartInfo(Rectangle bounds)
    {
        string? executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) return null;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };

        if (string.Equals(
                Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            string? entryAssembly = Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(entryAssembly)) return null;
            startInfo.ArgumentList.Add(entryAssembly);
        }

        startInfo.ArgumentList.Add(MagnificationCaptureProtocol.HelperArgument);
        AddArgument(startInfo, "--x", bounds.X);
        AddArgument(startInfo, "--y", bounds.Y);
        AddArgument(startInfo, "--width", bounds.Width);
        AddArgument(startInfo, "--height", bounds.Height);
        return startInfo;
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, int value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value.ToString(CultureInfo.InvariantCulture));
    }

    private static async Task CopyBoundedAsync(
        Stream source,
        MemoryStream destination,
        long maximumBytes,
        CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        while (true)
        {
            int count = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (count == 0) return;
            if (destination.Length + count > maximumBytes)
                throw new InvalidDataException("Magnification helper returned an oversized image.");
            await destination.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
        }
    }

    private static void StopHelper(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }
}

/// <summary>Runs the hidden one-shot helper branch from the WPF application.</summary>
[SupportedOSPlatform("windows")]
public static class MagnificationHelperHost
{
    public static bool IsHelperRequest(IReadOnlyList<string> args)
        => MagnificationCaptureProtocol.IsHelperRequest(args);

    public static int Run(IReadOnlyList<string> args)
    {
        if (!MagnificationCaptureProtocol.TryParseBounds(args, out var bounds))
            return 2;

        try
        {
            using var bitmap = MagnificationNative.Capture(bounds);
            if (bitmap is null) return 3;

            using var stdout = Console.OpenStandardOutput();
            bitmap.Save(stdout, ImageFormat.Png);
            stdout.Flush();
            return 0;
        }
        catch (Exception ex)
        {
            try { Console.Error.WriteLine($"Magnification helper failed: {ex.Message}"); }
            catch { }
            return 1;
        }
    }

    private static class MagnificationNative
    {
        private const string MagnifierClass = "Magnifier";
        private const uint WS_POPUP = 0x8000_0000;
        private const uint WS_CHILD = 0x4000_0000;
        private const uint WS_VISIBLE = 0x1000_0000;
        private const uint WS_CLIPCHILDREN = 0x0200_0000;
        private const uint WS_EX_LAYERED = 0x0008_0000;
        private const uint WS_EX_TOOLWINDOW = 0x0000_0080;
        private const uint WS_EX_NOACTIVATE = 0x0800_0000;
        private const uint LWA_ALPHA = 0x0000_0002;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SRCCOPY = 0x00CC_0020;
        private const uint PM_REMOVE = 0x0001;
        private const nint DpiAwarenessContextPerMonitorV2 = -4;

        public static Bitmap? Capture(Rectangle bounds)
        {
            if (!SetPerMonitorDpiAwareness()) return null;
            if (!MagInitialize()) return null;

            nint host = 0;
            nint magnifier = 0;
            try
            {
                host = CreateWindowEx(
                    WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                    "STATIC", "Snapture Magnification Helper",
                    WS_POPUP | WS_CLIPCHILDREN,
                    -32_000, -32_000, bounds.Width, bounds.Height,
                    0, 0, 0, 0);
                if (host == 0) return null;
                if (!SetLayeredWindowAttributes(host, 0, 255, LWA_ALPHA)) return null;

                magnifier = CreateWindowEx(
                    0, MagnifierClass, "MagnifierControl",
                    WS_CHILD | WS_VISIBLE,
                    0, 0, bounds.Width, bounds.Height,
                    host, 0, 0, 0);
                if (magnifier == 0) return null;

                var transform = new MAGTRANSFORM
                {
                    V = [1, 0, 0, 0, 1, 0, 0, 0, 1]
                };
                if (!MagSetWindowTransform(magnifier, ref transform)) return null;

                var source = new RECT
                {
                    Left = bounds.Left,
                    Top = bounds.Top,
                    Right = bounds.Right,
                    Bottom = bounds.Bottom
                };
                if (!MagSetWindowSource(magnifier, source)) return null;

                ShowWindow(host, SW_SHOWNOACTIVATE);
                UpdateWindow(host);
                InvalidateRect(magnifier, 0, true);
                UpdateWindow(magnifier);
                PumpMessages(150);

                var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                nint sourceDc = GetDC(magnifier);
                if (sourceDc == 0)
                {
                    bitmap.Dispose();
                    return null;
                }

                try
                {
                    using var graphics = Graphics.FromImage(bitmap);
                    nint targetDc = graphics.GetHdc();
                    try
                    {
                        if (!BitBlt(targetDc, 0, 0, bounds.Width, bounds.Height,
                                sourceDc, 0, 0, SRCCOPY))
                        {
                            bitmap.Dispose();
                            return null;
                        }
                    }
                    finally { graphics.ReleaseHdc(targetDc); }
                }
                finally { ReleaseDC(magnifier, sourceDc); }

                return bitmap;
            }
            finally
            {
                if (magnifier != 0) DestroyWindow(magnifier);
                if (host != 0) DestroyWindow(host);
                MagUninitialize();
            }
        }

        private static bool SetPerMonitorDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(DpiAwarenessContextPerMonitorV2))
                    return true;
                return SetProcessDPIAware();
            }
            catch { return false; }
        }

        private static void PumpMessages(int milliseconds)
        {
            long deadline = Environment.TickCount64 + milliseconds;
            while (Environment.TickCount64 < deadline)
            {
                while (PeekMessage(out var message, 0, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                Thread.Sleep(10);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MAGTRANSFORM
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)]
            public float[] V;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public nint Hwnd;
            public uint Message;
            public nuint WParam;
            public nint LParam;
            public uint Time;
            public POINT Point;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(
            uint exStyle, string className, string windowName, uint style,
            int x, int y, int width, int height,
            nint parent, nint menu, nint instance, nint param);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(nint hwnd, int command);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(nint hwnd);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);

        [DllImport("user32.dll")]
        private static extern nint GetDC(nint hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(nint hwnd, nint hdc);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out MSG message, nint hwnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG message);

        [DllImport("user32.dll")]
        private static extern nint DispatchMessage(ref MSG message);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(
            nint destination, int x, int y, int width, int height,
            nint source, int sourceX, int sourceY, uint operation);

        [DllImport("user32.dll", EntryPoint = "SetProcessDpiAwarenessContext")]
        private static extern bool SetProcessDpiAwarenessContext(nint dpiAwarenessContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("Magnification.dll", ExactSpelling = true)]
        private static extern bool MagInitialize();

        [DllImport("Magnification.dll", ExactSpelling = true)]
        private static extern bool MagUninitialize();

        [DllImport("Magnification.dll", ExactSpelling = true)]
        private static extern bool MagSetWindowSource(nint hwnd, RECT source);

        [DllImport("Magnification.dll", ExactSpelling = true)]
        private static extern bool MagSetWindowTransform(nint hwnd, ref MAGTRANSFORM transform);
    }
}
