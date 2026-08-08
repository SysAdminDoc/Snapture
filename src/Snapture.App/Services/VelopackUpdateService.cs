using System.Reflection;
using System.Runtime.InteropServices;
using Serilog;
using Velopack;

namespace Snapture.App.Services;

/// <summary>Velopack update lifecycle for installed packages.</summary>
public static class VelopackUpdateService
{
    public static readonly string FeedUrl = "https://github.com/SysAdminDoc/Snapture/releases/latest/download";
    public static string Channel => RuntimeInformation.ProcessArchitecture == Architecture.Arm64
        ? "win-arm64-stable"
        : "win-x64-stable";

    private static readonly object Gate = new();
    private static UpdateManager? _manager;
    private static UpdateInfo? _pendingInfo;

    public sealed record UpdateStatus(
        bool Supported,
        bool Available,
        string CurrentVersion,
        string LatestVersion,
        string? Error = null);

    public static async Task<UpdateStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        string fallbackVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        try
        {
            var manager = new UpdateManager(
                FeedUrl,
                new UpdateOptions { ExplicitChannel = Channel });
            if (!manager.IsInstalled)
            {
                lock (Gate)
                {
                    _manager = null;
                    _pendingInfo = null;
                }
                return new UpdateStatus(false, false, fallbackVersion, fallbackVersion);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var info = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            string current = manager.CurrentVersion?.ToString() ?? fallbackVersion;
            string latest = info?.TargetFullRelease.Version.ToString() ?? current;
            lock (Gate)
            {
                _manager = manager;
                _pendingInfo = info;
            }
            Log.Information(
                "Velopack.UpdateCheck {Current} {Latest} {Available}",
                current,
                latest,
                info is not null);
            return new UpdateStatus(true, info is not null, current, latest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            string error = OutboundDataFlowAudit.RedactSensitive(ex.Message);
            Log.Warning("Velopack.UpdateCheckFailed {Error}", error);
            return new UpdateStatus(true, false, fallbackVersion, fallbackVersion, error);
        }
    }

    public static async Task<bool> DownloadPendingAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        UpdateManager? manager;
        UpdateInfo? info;
        lock (Gate)
        {
            manager = _manager;
            info = _pendingInfo;
        }

        if (manager is null || info is null || !manager.IsInstalled)
            return false;

        await manager.DownloadUpdatesAsync(info, progress, cancellationToken).ConfigureAwait(false);
        return manager.UpdatePendingRestart is not null;
    }

    /// <summary>Applies the downloaded update and exits immediately through Velopack's updater.</summary>
    public static bool ApplyPendingAndRestart()
    {
        UpdateManager? manager;
        lock (Gate) manager = _manager;
        var pending = manager?.UpdatePendingRestart;
        if (manager is null || pending is null)
            return false;

        manager.ApplyUpdatesAndRestart(pending, Array.Empty<string>());
        return true;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _manager = null;
            _pendingInfo = null;
        }
    }
}
