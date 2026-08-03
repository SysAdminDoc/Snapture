using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snapture.App.Services;

internal sealed class RecordingZoomSuggestionEngine
{
    private static readonly TimeSpan LeadIn = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan SegmentDuration = TimeSpan.FromMilliseconds(1_800);
    private static readonly TimeSpan MergeWindow = TimeSpan.FromMilliseconds(1_250);
    private const double MergeDistancePixels = 96.0;

    private readonly object _lock = new();
    private readonly List<CursorTelemetrySample> _samples = new();
    private readonly List<CursorTelemetrySample> _clicks = new();

    public int ClickCount
    {
        get
        {
            lock (_lock)
            {
                return _clicks.Count;
            }
        }
    }

    public void AddCursorSample(TimeSpan timestamp, Point position)
    {
        lock (_lock)
        {
            if (_samples.Count > 0 && _samples[^1].Position == position)
                return;

            _samples.Add(new CursorTelemetrySample(timestamp, position));
        }
    }

    public void AddClick(TimeSpan timestamp, Point position)
    {
        lock (_lock)
        {
            _clicks.Add(new CursorTelemetrySample(timestamp, position));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
            _clicks.Clear();
        }
    }

    public IReadOnlyList<RecordingZoomSuggestion> BuildSuggestions(int frameWidth, int frameHeight)
    {
        if (frameWidth <= 0 || frameHeight <= 0)
            return Array.Empty<RecordingZoomSuggestion>();

        List<CursorTelemetrySample> clicks;
        List<CursorTelemetrySample> samples;
        lock (_lock)
        {
            clicks = _clicks.ToList();
            samples = _samples.ToList();
        }

        if (clicks.Count == 0)
            return Array.Empty<RecordingZoomSuggestion>();

        clicks.Sort(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));
        List<RecordingZoomSuggestion> suggestions = new();
        foreach (var click in clicks)
        {
            var target = EstimateTarget(click, samples);
            var next = CreateSuggestion(click.Timestamp, target, frameWidth, frameHeight, clickCount: 1);
            if (suggestions.Count > 0 && ShouldMerge(suggestions[^1], next))
                suggestions[^1] = Merge(suggestions[^1], next, frameWidth, frameHeight);
            else
                suggestions.Add(next);
        }

        return suggestions;
    }

    public string? ExportSidecar(string videoPath, int frameWidth, int frameHeight)
    {
        var suggestions = BuildSuggestions(frameWidth, frameHeight);
        if (suggestions.Count == 0)
            return null;

        var payload = RecordingZoomSuggestionDocument.Create(videoPath, frameWidth, frameHeight, suggestions);
        string path = Path.ChangeExtension(videoPath, ".snapture-zoom.json");
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, options));
        return path;
    }

    private static PointF EstimateTarget(CursorTelemetrySample click, IReadOnlyList<CursorTelemetrySample> samples)
    {
        var start = click.Timestamp - TimeSpan.FromMilliseconds(500);
        var end = click.Timestamp + TimeSpan.FromMilliseconds(250);
        double x = click.Position.X * 3.0;
        double y = click.Position.Y * 3.0;
        double weight = 3.0;

        foreach (var sample in samples)
        {
            if (sample.Timestamp < start || sample.Timestamp > end)
                continue;

            double age = Math.Abs((sample.Timestamp - click.Timestamp).TotalMilliseconds);
            double sampleWeight = Math.Max(0.2, 1.0 - (age / 750.0));
            x += sample.Position.X * sampleWeight;
            y += sample.Position.Y * sampleWeight;
            weight += sampleWeight;
        }

        return new PointF((float)(x / weight), (float)(y / weight));
    }

    private static RecordingZoomSuggestion CreateSuggestion(
        TimeSpan clickTime,
        PointF center,
        int frameWidth,
        int frameHeight,
        int clickCount)
    {
        double scale = frameWidth >= 1600 && frameHeight >= 900 ? 1.65 : 1.45;
        var crop = BuildCrop(center, frameWidth, frameHeight, scale);
        var start = clickTime > LeadIn ? clickTime - LeadIn : TimeSpan.Zero;
        return new RecordingZoomSuggestion(start, SegmentDuration, center, crop, scale, clickCount);
    }

    private static RectangleF BuildCrop(PointF center, int frameWidth, int frameHeight, double scale)
    {
        float cropWidth = Math.Max(1f, (float)(frameWidth / scale));
        float cropHeight = Math.Max(1f, (float)(frameHeight / scale));
        float x = Math.Clamp(center.X - (cropWidth / 2f), 0f, Math.Max(0f, frameWidth - cropWidth));
        float y = Math.Clamp(center.Y - (cropHeight / 2f), 0f, Math.Max(0f, frameHeight - cropHeight));
        return new RectangleF(x, y, cropWidth, cropHeight);
    }

    private static bool ShouldMerge(RecordingZoomSuggestion current, RecordingZoomSuggestion next)
    {
        var currentEnd = current.Start + current.Duration;
        if (next.Start - currentEnd > MergeWindow)
            return false;

        double dx = current.Center.X - next.Center.X;
        double dy = current.Center.Y - next.Center.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) <= MergeDistancePixels;
    }

    private static RecordingZoomSuggestion Merge(
        RecordingZoomSuggestion current,
        RecordingZoomSuggestion next,
        int frameWidth,
        int frameHeight)
    {
        int clickCount = current.ClickCount + next.ClickCount;
        float x = ((current.Center.X * current.ClickCount) + next.Center.X) / clickCount;
        float y = ((current.Center.Y * current.ClickCount) + next.Center.Y) / clickCount;
        var center = new PointF(x, y);
        var start = current.Start <= next.Start ? current.Start : next.Start;
        var end = Max(current.Start + current.Duration, next.Start + next.Duration);
        var duration = end - start;
        return new RecordingZoomSuggestion(start, duration, center, BuildCrop(center, frameWidth, frameHeight, current.Scale), current.Scale, clickCount);
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right)
        => left >= right ? left : right;

    private readonly record struct CursorTelemetrySample(TimeSpan Timestamp, Point Position);
}

internal readonly record struct RecordingZoomSuggestion(
    TimeSpan Start,
    TimeSpan Duration,
    PointF Center,
    RectangleF Crop,
    double Scale,
    int ClickCount);

internal sealed record RecordingZoomSuggestionDocument(
    int SchemaVersion,
    string SourceVideo,
    int FrameWidth,
    int FrameHeight,
    IReadOnlyList<RecordingZoomSuggestionDto> Suggestions)
{
    public static RecordingZoomSuggestionDocument Create(
        string videoPath,
        int frameWidth,
        int frameHeight,
        IReadOnlyList<RecordingZoomSuggestion> suggestions)
    {
        return new RecordingZoomSuggestionDocument(
            1,
            Path.GetFileName(videoPath),
            frameWidth,
            frameHeight,
            suggestions.Select(RecordingZoomSuggestionDto.FromSuggestion).ToList());
    }
}

internal sealed record RecordingZoomSuggestionDto(
    double StartSeconds,
    double DurationSeconds,
    double CenterX,
    double CenterY,
    double Scale,
    int ClickCount,
    RecordingZoomCropDto Crop)
{
    public static RecordingZoomSuggestionDto FromSuggestion(RecordingZoomSuggestion suggestion)
    {
        return new RecordingZoomSuggestionDto(
            Math.Round(suggestion.Start.TotalSeconds, 3),
            Math.Round(suggestion.Duration.TotalSeconds, 3),
            Math.Round(suggestion.Center.X, 1),
            Math.Round(suggestion.Center.Y, 1),
            Math.Round(suggestion.Scale, 2),
            suggestion.ClickCount,
            new RecordingZoomCropDto(
                Math.Round(suggestion.Crop.X, 1),
                Math.Round(suggestion.Crop.Y, 1),
                Math.Round(suggestion.Crop.Width, 1),
                Math.Round(suggestion.Crop.Height, 1)));
    }
}

internal sealed record RecordingZoomCropDto(double X, double Y, double Width, double Height);
