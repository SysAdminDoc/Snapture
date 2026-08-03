using System.IO;
using System.Runtime.Versioning;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;

namespace Snapture.App.Services;

internal readonly record struct VideoSegmentRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

/// <summary>
/// Non-destructive MP4 trim and split operations backed by Windows Media Composition.
/// The source is never overwritten; every operation renders a new file.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal static class VideoSegmentService
{
    public static IReadOnlyList<VideoSegmentRange> BuildSplitRanges(
        TimeSpan duration,
        IEnumerable<TimeSpan> cutPoints)
    {
        if (duration <= TimeSpan.Zero)
            return Array.Empty<VideoSegmentRange>();

        var candidates = cutPoints
            .Where(point => point > TimeSpan.Zero && point < duration)
            .Distinct()
            .OrderBy(point => point)
            .ToList();

        var minimumSegment = TimeSpan.FromMilliseconds(100);
        List<TimeSpan> points = new() { TimeSpan.Zero };
        foreach (var candidate in candidates)
        {
            if (candidate - points[^1] >= minimumSegment
                && duration - candidate >= minimumSegment)
                points.Add(candidate);
        }
        points.Add(duration);

        List<VideoSegmentRange> ranges = new();
        for (int i = 1; i < points.Count; i++)
        {
            var range = new VideoSegmentRange(points[i - 1], points[i]);
            ranges.Add(range);
        }

        return ranges;
    }

    public static async Task<TimeSpan> TrimAsync(
        string inputPath,
        string outputPath,
        TimeSpan start,
        TimeSpan? end = null,
        CancellationToken cancellationToken = default)
    {
        EnsureDistinctPaths(inputPath, outputPath);
        var input = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));
        var clip = await MediaClip.CreateFromFileAsync(input);
        var range = NormalizeRange(clip.OriginalDuration, start, end);
        cancellationToken.ThrowIfCancellationRequested();
        await RenderRangeAsync(input, outputPath, range, cancellationToken);
        return range.Duration;
    }

    public static async Task<TimeSpan> GetDurationAsync(string inputPath)
    {
        var input = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(inputPath));
        var clip = await MediaClip.CreateFromFileAsync(input);
        return clip.OriginalDuration;
    }

    public static async Task<IReadOnlyList<string>> SplitAsync(
        string inputPath,
        string outputDirectory,
        IEnumerable<TimeSpan> cutPoints,
        CancellationToken cancellationToken = default)
    {
        var inputPathFull = Path.GetFullPath(inputPath);
        var input = await StorageFile.GetFileFromPathAsync(inputPathFull);
        var sourceClip = await MediaClip.CreateFromFileAsync(input);
        var ranges = BuildSplitRanges(sourceClip.OriginalDuration, cutPoints);
        if (ranges.Count == 0)
            throw new ArgumentException("At least one segment boundary is required.", nameof(cutPoints));

        Directory.CreateDirectory(outputDirectory);
        string stem = Path.GetFileNameWithoutExtension(inputPathFull);
        List<string> outputs = new(ranges.Count);
        for (int i = 0; i < ranges.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string outputPath = Path.Combine(outputDirectory, $"{stem}_part-{i + 1:00}.mp4");
            EnsureDistinctPaths(inputPathFull, outputPath);
            await RenderRangeAsync(input, outputPath, ranges[i], cancellationToken);
            outputs.Add(outputPath);
        }

        return outputs;
    }

    internal static VideoSegmentRange NormalizeRange(TimeSpan duration, TimeSpan start, TimeSpan? end)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration), "The source video has no duration.");
        if (start < TimeSpan.Zero || start >= duration)
            throw new ArgumentOutOfRangeException(nameof(start), "Trim start must be inside the source duration.");

        var resolvedEnd = end ?? duration;
        if (resolvedEnd <= start || resolvedEnd > duration)
            throw new ArgumentOutOfRangeException(nameof(end), "Trim end must be after start and inside the source duration.");

        return new VideoSegmentRange(start, resolvedEnd);
    }

    private static async Task RenderRangeAsync(
        StorageFile input,
        string outputPath,
        VideoSegmentRange range,
        CancellationToken cancellationToken)
    {
        string outputFullPath = Path.GetFullPath(outputPath);
        string? outputDirectory = Path.GetDirectoryName(outputFullPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Output path must include a directory.", nameof(outputPath));

        Directory.CreateDirectory(outputDirectory);
        var folder = await StorageFolder.GetFolderFromPathAsync(outputDirectory);
        var output = await folder.CreateFileAsync(
            Path.GetFileName(outputFullPath),
            CreationCollisionOption.ReplaceExisting);

        var clip = await MediaClip.CreateFromFileAsync(input);
        clip.TrimTimeFromStart = range.Start;
        clip.TrimTimeFromEnd = clip.OriginalDuration - range.End;

        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        cancellationToken.ThrowIfCancellationRequested();
        var profile = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.Auto);
        await composition.RenderToFileAsync(output, MediaTrimmingPreference.Precise, profile);
    }

    private static void EnsureDistinctPaths(string inputPath, string outputPath)
    {
        string inputFull = Path.GetFullPath(inputPath);
        string outputFull = Path.GetFullPath(outputPath);
        if (string.Equals(inputFull, outputFull, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The source recording cannot be overwritten.", nameof(outputPath));
    }
}
