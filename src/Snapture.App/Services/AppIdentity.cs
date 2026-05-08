using System.Runtime.InteropServices;

namespace Snapture.App.Services;

/// <summary>
/// Sets the explicit AppUserModelID on process start. Reason: Win11 22H2+ pins the
/// borderless-capture access consent against this AUMID, so a stable AUMID is what
/// makes the consent persist across reinstalls.
/// </summary>
internal static class AppIdentity
{
    public const string AppUserModelId = "SysAdminDoc.Snapture";

    public static void SetAumid()
    {
        try { _ = SetCurrentProcessExplicitAppUserModelID(AppUserModelId); } catch { /* non-fatal */ }
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string AppID);
}
