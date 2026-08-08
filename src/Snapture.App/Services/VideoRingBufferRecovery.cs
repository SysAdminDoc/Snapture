using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;

namespace Snapture.App.Services;

internal enum RingBufferSessionState
{
    Starting,
    Recording,
    Rotating,
    Saving,
    RecoveryRequired,
    Recovered,
    CleanStop,
    Discarded
}

internal sealed class RingBufferSessionManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string SessionId { get; set; } = string.Empty;
    public RingBufferSessionState State { get; set; }
    public string SourceMode { get; set; } = string.Empty;
    public long SourceHandle { get; set; }
    public int Fps { get; set; }
    public int BitrateMbps { get; set; }
    public int OutputWidth { get; set; }
    public int OutputHeight { get; set; }
    public bool AutoTighten { get; set; }
    public string ToneMapOperator { get; set; } = string.Empty;
    public bool HdrColorCorrection { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public string? ActiveFileName { get; set; }
    public string? LastError { get; set; }
    public List<RingBufferSegmentManifest> Segments { get; set; } = new();
}

internal sealed class RingBufferSegmentManifest
{
    public string FileName { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public double DurationSeconds { get; set; }
    public long SizeBytes { get; set; }
    public string State { get; set; } = "active";
}

internal sealed record RingBufferRecoveryLimits(
    TimeSpan MaximumSessionAge,
    long MaximumTotalBytes,
    int MaximumRetainedSessions)
{
    public static RingBufferRecoveryLimits Default { get; } = new(
        TimeSpan.FromHours(2),
        256L * 1024 * 1024,
        2);
}

internal sealed record RingBufferRecoveryResult(
    int RetainedCount,
    int DiscardedCount,
    long DiscardedBytes,
    string Message,
    IReadOnlyList<string> RecoveredDirectories)
{
    public bool HasAction => RetainedCount > 0 || DiscardedCount > 0;
}

/// <summary>
/// Owns the durable, non-user-visible state around rolling video sessions. A
/// recovered session is quarantined for explicit review; it is never opened or
/// copied into a user folder automatically.
/// </summary>
internal static class VideoRingBufferRecovery
{
    public const string ManifestFileName = "session.json";
    public const string SessionDirectoryPrefix = "session-";
    public const int ManifestSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultBufferDirectory
        => Path.Combine(Path.GetTempPath(), "Snapture", "ring-buffer");

    public static string CreateSessionDirectory(string rootDirectory)
    {
        Directory.CreateDirectory(rootDirectory);
        string directory;
        do
        {
            directory = Path.Combine(rootDirectory, $"{SessionDirectoryPrefix}{Guid.NewGuid():N}");
        }
        while (Directory.Exists(directory));

        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetManifestPath(string sessionDirectory)
        => Path.Combine(sessionDirectory, ManifestFileName);

    public static void WriteManifest(string sessionDirectory, RingBufferSessionManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(sessionDirectory);

        manifest.SchemaVersion = ManifestSchemaVersion;
        manifest.LastUpdatedUtc = DateTime.UtcNow;
        string path = GetManifestPath(sessionDirectory);
        string temporary = path + ".tmp";
        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(temporary, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    public static RingBufferSessionManifest? TryReadManifest(string sessionDirectory)
    {
        try
        {
            string path = GetManifestPath(sessionDirectory);
            if (!File.Exists(path))
                return null;

            var manifest = JsonSerializer.Deserialize<RingBufferSessionManifest>(File.ReadAllText(path), JsonOptions);
            if (manifest is null
                || manifest.SchemaVersion != ManifestSchemaVersion
                || string.IsNullOrWhiteSpace(manifest.SessionId)
                || manifest.Segments.Any(segment => !IsSafeFileName(segment.FileName)))
            {
                return null;
            }

            return manifest;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "VideoRingBuffer.ManifestReadFailed {Directory}", sessionDirectory);
            return null;
        }
    }

    public static bool IsRecoverableFragmentedMp4(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 256)
                return false;

            bool hasFileType = false;
            bool hasMovieFragment = false;
            uint rolling = 0;
            byte[] buffer = new byte[64 * 1024];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    rolling = (rolling << 8) | buffer[i];
                    if (rolling == 0x66747970) // ftyp
                        hasFileType = true;
                    else if (rolling == 0x6D6F6F66) // moof
                        hasMovieFragment = true;

                    if (hasFileType && hasMovieFragment)
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "VideoRingBuffer.RecoveryProbeFailed {Path}", path);
        }

        return false;
    }

    public static IReadOnlyList<RingBufferSegmentManifest> PruneSegments(
        string sessionDirectory,
        RingBufferSessionManifest manifest,
        int maximumSegments,
        long maximumBytes,
        DateTime nowUtc)
    {
        if (maximumSegments < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        if (maximumBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes));

        var removed = new List<RingBufferSegmentManifest>();
        long totalBytes = manifest.Segments.Sum(segment => Math.Max(0, segment.SizeBytes));
        var ordered = manifest.Segments
            .OrderBy(segment => segment.CompletedUtc ?? segment.StartedUtc)
            .ToList();

        while (ordered.Count > maximumSegments || totalBytes > maximumBytes)
        {
            var candidate = ordered[0];
            ordered.RemoveAt(0);
            manifest.Segments.Remove(candidate);
            totalBytes -= Math.Max(0, candidate.SizeBytes);
            removed.Add(candidate);

            TryDelete(Path.Combine(sessionDirectory, candidate.FileName));
        }

        foreach (var candidate in ordered.ToArray())
        {
            if (candidate.CompletedUtc is not { } completed
                || nowUtc - completed <= TimeSpan.FromMinutes(2))
            {
                continue;
            }

            ordered.Remove(candidate);
            manifest.Segments.Remove(candidate);
            totalBytes -= Math.Max(0, candidate.SizeBytes);
            removed.Add(candidate);
            TryDelete(Path.Combine(sessionDirectory, candidate.FileName));
        }

        return removed;
    }

    public static bool HasSufficientDiskSpace(long availableBytes, long requiredBytes)
        => availableBytes >= requiredBytes && availableBytes > 0;

    public static bool HasRecoveries(string rootDirectory)
        => EnumerateSessionDirectories(rootDirectory)
            .Select(directory => (Directory: directory, Manifest: TryReadManifest(directory)))
            .Any(item => item.Manifest?.State == RingBufferSessionState.Recovered
                && Directory.EnumerateFiles(item.Directory, "*.mp4", SearchOption.TopDirectoryOnly).Any());

    public static RingBufferRecoveryResult RecoverOrphans(
        string rootDirectory,
        DateTime? nowUtc = null,
        RingBufferRecoveryLimits? limits = null)
    {
        DateTime now = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
        var policy = limits ?? RingBufferRecoveryLimits.Default;
        Directory.CreateDirectory(rootDirectory);

        int retained = 0;
        int discarded = 0;
        long discardedBytes = 0;
        var newlyRecovered = new List<string>();

        foreach (string directory in EnumerateSessionDirectories(rootDirectory).ToArray())
        {
            var manifest = TryReadManifest(directory);
            if (manifest is null)
            {
                discardedBytes += GetMediaBytes(directory);
                if (TryDeleteDirectory(directory))
                    discarded++;
                continue;
            }

            if (manifest.State is RingBufferSessionState.CleanStop or RingBufferSessionState.Discarded)
            {
                discardedBytes += GetMediaBytes(directory);
                if (TryDeleteDirectory(directory))
                    discarded++;
                continue;
            }

            if (now - manifest.LastUpdatedUtc.ToUniversalTime() > policy.MaximumSessionAge)
            {
                discardedBytes += GetMediaBytes(directory);
                if (TryDeleteDirectory(directory))
                    discarded++;
                continue;
            }

            string[] mediaFiles = Directory.EnumerateFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly).ToArray();
            if (!mediaFiles.Any(IsRecoverableFragmentedMp4))
            {
                discardedBytes += GetMediaBytes(directory);
                if (TryDeleteDirectory(directory))
                    discarded++;
                continue;
            }

            bool wasAlreadyRecovered = manifest.State == RingBufferSessionState.Recovered;
            manifest.State = RingBufferSessionState.Recovered;
            manifest.ActiveFileName = null;
            manifest.LastError ??= "The previous process ended before the ring-buffer session was stopped cleanly.";
            WriteManifest(directory, manifest);
            if (!wasAlreadyRecovered)
            {
                retained++;
                newlyRecovered.Add(directory);
            }
        }

        foreach (string directory in EnumerateSessionDirectories(rootDirectory).ToArray())
        {
            var manifest = TryReadManifest(directory);
            if (manifest?.State != RingBufferSessionState.Recovered)
                continue;

            long bytes = GetMediaBytes(directory);
            if (newlyRecovered.Contains(directory, StringComparer.OrdinalIgnoreCase))
                continue;

            // Existing quarantined sessions are subject to the same budget below.
            if (bytes == 0 && TryDeleteDirectory(directory))
                discarded++;
        }

        var recovered = EnumerateSessionDirectories(rootDirectory)
            .Select(directory =>
            {
                var manifest = TryReadManifest(directory);
                return new
                {
                    Directory = directory,
                    Manifest = manifest,
                    Bytes = GetMediaBytes(directory)
                };
            })
            .Where(item => item.Manifest?.State == RingBufferSessionState.Recovered)
            .OrderBy(item => item.Manifest!.LastUpdatedUtc)
            .ToList();

        long totalBytes = recovered.Sum(item => item.Bytes);
        while (recovered.Count > policy.MaximumRetainedSessions || totalBytes > policy.MaximumTotalBytes)
        {
            var oldest = recovered[0];
            recovered.RemoveAt(0);
            totalBytes -= oldest.Bytes;
            if (TryDeleteDirectory(oldest.Directory))
            {
                discarded++;
                discardedBytes += oldest.Bytes;
            }
        }

        string message = BuildMessage(retained, discarded);
        return new RingBufferRecoveryResult(
            retained,
            discarded,
            discardedBytes,
            message,
            newlyRecovered.Where(Directory.Exists).ToArray());
    }

    private static IEnumerable<string> EnumerateSessionDirectories(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(rootDirectory)
            .Where(path => Path.GetFileName(path).StartsWith(SessionDirectoryPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildMessage(int retained, int discarded)
    {
        if (retained > 0 && discarded > 0)
            return $"Kept {retained} interrupted ring-buffer recording(s) for manual review; discarded {discarded} stale, corrupt, or over-budget item(s). Nothing was opened automatically.";
        if (retained > 0)
            return $"Kept {retained} interrupted ring-buffer recording(s) for manual review. Nothing was opened automatically.";
        if (discarded > 0)
            return $"Discarded {discarded} stale, corrupt, or over-budget ring-buffer item(s); no recording was opened automatically.";
        return string.Empty;
    }

    private static bool IsSafeFileName(string fileName)
        => !string.IsNullOrWhiteSpace(fileName)
            && string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            && fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    private static long GetMediaBytes(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.mp4", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path).Length)
                .Sum();
        }
        catch { return 0; }
    }

    private static bool TryDeleteDirectory(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
                return true;
            Directory.Delete(directory, recursive: true);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "VideoRingBuffer.RecoveryDeleteFailed {Directory}", directory);
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "VideoRingBuffer.SegmentDeleteFailed {Path}", path);
        }
    }
}
