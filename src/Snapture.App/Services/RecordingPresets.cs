namespace Snapture.App.Services;

public static class RecordingPresets
{
    public const string DefaultQuality = "high";
    public const string NativeResolution = "native";

    public sealed record QualityPreset(string Key, string Label, int Fps, int BitrateMbps);
    public sealed record ResolutionPreset(string Key, string Label, int Width, int Height);

    public static readonly QualityPreset[] Qualities =
    {
        new("low",    "Low (2 Mbps, 20 fps)",    20, 2),
        new("medium", "Medium (5 Mbps, 30 fps)",  30, 5),
        new("high",   "High (8 Mbps, 30 fps)",    30, 8),
        new("ultra",  "Ultra (16 Mbps, 60 fps)",   60, 16),
    };

    public static readonly ResolutionPreset[] Resolutions =
    {
        new("native", "Native (source size)", 0, 0),
        new("720p",   "720p (1280x720)",      1280, 720),
        new("1080p",  "1080p (1920x1080)",    1920, 1080),
        new("1440p",  "1440p (2560x1440)",    2560, 1440),
        new("4k",     "4K (3840x2160)",       3840, 2160),
        new("9:16",   "9:16 (1080x1920)",     1080, 1920),
        new("1:1",    "1:1 (1080x1080)",      1080, 1080),
    };

    public static QualityPreset GetQuality(string key)
        => Array.Find(Qualities, q => q.Key == key) ?? Qualities[2];

    public static ResolutionPreset GetResolution(string key)
        => Array.Find(Resolutions, r => r.Key == key) ?? Resolutions[0];

    public static (int width, int height) ResolveOutputSize(string resolutionKey, int sourceWidth, int sourceHeight)
    {
        var preset = GetResolution(resolutionKey);
        if (preset.Width <= 0 || preset.Height <= 0)
            return (sourceWidth, sourceHeight);

        return (preset.Width, preset.Height);
    }
}
