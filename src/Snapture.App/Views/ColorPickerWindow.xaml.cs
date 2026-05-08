using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Snapture.App.Views;

public partial class ColorPickerWindow : Window
{
    private readonly DispatcherTimer _timer;
    private System.Windows.Media.Color _color = Colors.Black;

    public ColorPickerWindow()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => UpdateFromCursor();
        _timer.Start();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        // Listen for global mouse-down by hooking the window-level click — the user must
        // be hovering this window. To capture clicks anywhere on screen, we set a
        // low-level mouse hook for the duration the window is open.
        InstallHook();
        Closed += (_, _) => UninstallHook();
    }

    private void UpdateFromCursor()
    {
        try
        {
            GetCursorPos(out var pt);
            var rgb = SamplePixelArgb(pt.X, pt.Y);
            _color = System.Windows.Media.Color.FromRgb(rgb.R, rgb.G, rgb.B);
            SwatchBorder.Background = new SolidColorBrush(_color);
            HexText.Text = $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
            RgbText.Text = $"{rgb.R}, {rgb.G}, {rgb.B}";

            var (h, s, l) = ToHsl(rgb.R, rgb.G, rgb.B);
            HslText.Text = $"{(int)Math.Round(h)}°, {(int)Math.Round(s * 100)}%, {(int)Math.Round(l * 100)}%";

            // APCA-lite: contrast vs white
            double lcWhite = ApcaLc(rgb.R, rgb.G, rgb.B, 255, 255, 255);
            double lcBlack = ApcaLc(rgb.R, rgb.G, rgb.B, 0, 0, 0);
            ApcaText.Text = $"vs #FFFFFF: {lcWhite:F0} Lc · vs #000: {lcBlack:F0} Lc";
        }
        catch { /* swallow transient sampling errors */ }
    }

    private static (byte R, byte G, byte B, byte A) SamplePixelArgb(int x, int y)
    {
        using var bmp = new Bitmap(1, 1, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1));
        }
        var p = bmp.GetPixel(0, 0);
        return (p.R, p.G, p.B, p.A);
    }

    private static (double H, double S, double L) ToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double l = (max + min) / 2.0;
        if (max == min) return (0, 0, l);
        double d = max - min;
        double s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        double h;
        if (max == rd) h = (gd - bd) / d + (gd < bd ? 6 : 0);
        else if (max == gd) h = (bd - rd) / d + 2;
        else h = (rd - gd) / d + 4;
        h *= 60;
        return (h, s, l);
    }

    /// <summary>
    /// Lightweight APCA approximation. Real APCA needs a polarity-aware calc; this is
    /// the standard Lc readout used in designer tooling and is good enough for a
    /// designer to make a snap call. Source: the public APCA algorithm.
    /// </summary>
    private static double ApcaLc(byte tr, byte tg, byte tb, byte br, byte bg, byte bb)
    {
        const double mainTrc = 2.4;
        double txtY = SrgbToY(tr, tg, tb, mainTrc);
        double bgY  = SrgbToY(br, bg, bb, mainTrc);
        bool blackOnWhite = bgY > txtY;
        double Sapc;
        if (blackOnWhite)
        {
            double diff = Math.Pow(bgY, 0.56) - Math.Pow(txtY, 0.57);
            Sapc = diff * 1.14;
        }
        else
        {
            double diff = Math.Pow(bgY, 0.65) - Math.Pow(txtY, 0.62);
            Sapc = diff * 1.14;
        }
        double Lc;
        if (Math.Abs(Sapc) < 0.1) Lc = 0;
        else if (Sapc > 0) Lc = (Sapc - 0.027) * 100;
        else Lc = (Sapc + 0.027) * 100;
        return Lc;
    }

    private static double SrgbToY(byte r, byte g, byte b, double trc)
    {
        double rd = Math.Pow(r / 255.0, trc);
        double gd = Math.Pow(g / 255.0, trc);
        double bd = Math.Pow(b / 255.0, trc);
        return 0.2126 * rd + 0.7152 * gd + 0.0722 * bd;
    }

    private void OnCopyHexClicked(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(HexText.Text); } catch { }
        Close();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    // ---- Low-level mouse hook for global click capture -----------------------

    private nint _hookHandle;
    private LowLevelMouseProc? _proc;

    private void InstallHook()
    {
        _proc = HookCallback;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private void UninstallHook()
    {
        if (_hookHandle != 0) UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
        {
            // Copy the locked colour and close on the dispatcher thread.
            Dispatcher.BeginInvoke((Action)(() =>
            {
                try { Clipboard.SetText(HexText.Text); } catch { }
                Close();
            }));
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    private delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
