using System.IO;
using Serilog;

namespace Snapture.App.Services;

/// <summary>
/// Sweeps <c>%LOCALAPPDATA%\Snapture\</c> on app start for orphaned files left
/// by a previous crash: abandoned step-capture sessions, in-flight GIF temp frames,
/// stale autosave drafts older than 7 days. Reports totals via Serilog.
/// </summary>
public static class OrphanFileDetector
{
    private static string LocalAppData => PortableMode.LocalDataDirectory;
    internal static RingBufferRecoveryResult? LastRingBufferRecovery { get; private set; }

    public static int Sweep()
    {
        int cleaned = 0;
        cleaned += CleanStepSessions();
        cleaned += CleanGifTempFrames();
        cleaned += CleanStaleAutosaves();
        try
        {
            LastRingBufferRecovery = VideoRingBufferRecovery.RecoverOrphans(
                VideoRingBufferRecovery.DefaultBufferDirectory);
            cleaned += LastRingBufferRecovery.DiscardedCount;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "OrphanDetector.RingBufferRecoveryFailed");
        }
        return cleaned;
    }

    private static int CleanStepSessions()
    {
        var dir = Path.Combine(LocalAppData, "step-sessions");
        if (!Directory.Exists(dir)) return 0;

        int count = 0;
        try
        {
            foreach (var session in Directory.EnumerateDirectories(dir))
            {
                var created = Directory.GetCreationTimeUtc(session);
                if (DateTime.UtcNow - created > TimeSpan.FromDays(1))
                {
                    try
                    {
                        Directory.Delete(session, recursive: true);
                        count++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "OrphanDetector.StepSession.DeleteFailed {Path}", session);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "OrphanDetector.StepSessions.ScanFailed");
        }
        return count;
    }

    private static int CleanGifTempFrames()
    {
        int count = 0;
        try
        {
            var tempDir = Path.GetTempPath();
            foreach (var file in Directory.EnumerateFiles(tempDir, "Snapture_rec_*.mp4"))
            {
                var created = File.GetCreationTimeUtc(file);
                if (DateTime.UtcNow - created > TimeSpan.FromHours(2))
                {
                    try { File.Delete(file); count++; }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "OrphanDetector.TempFrames.ScanFailed");
        }
        return count;
    }

    private static int CleanStaleAutosaves()
    {
        var dir = Path.Combine(LocalAppData, "autosave");
        if (!Directory.Exists(dir)) return 0;

        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.snapture-autosave"))
            {
                var modified = File.GetLastWriteTimeUtc(file);
                if (DateTime.UtcNow - modified > TimeSpan.FromDays(7))
                {
                    try { File.Delete(file); count++; }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "OrphanDetector.Autosaves.ScanFailed");
        }
        return count;
    }
}
