namespace Snapture.App.Services;

public enum StepCaptureClickButton
{
    Left,
    Right,
    Middle
}

public sealed record StepCaptureKeyStroke(
    string Key,
    DateTime TimestampUtc);

public sealed record StepCaptureClick(
    int X,
    int Y,
    StepCaptureClickButton Button,
    DateTime TimestampUtc);

internal static class StepCaptureInputFormatter
{
    public static string? FormatTrack(
        IReadOnlyList<StepCaptureKeyStroke>? keystrokes,
        IReadOnlyList<StepCaptureClick>? clicks)
    {
        var parts = new List<string>(2);
        if (keystrokes is { Count: > 0 })
            parts.Add($"Keys: {string.Join(", ", keystrokes.Select(static key => key.Key))}");

        if (clicks is { Count: > 0 })
        {
            var formattedClicks = clicks.Select(static click =>
                $"{FormatButton(click.Button)} ({click.X}, {click.Y})");
            parts.Add($"Clicks: {string.Join(", ", formattedClicks)}");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    public static string? FormatMarkdown(
        IReadOnlyList<StepCaptureKeyStroke>? keystrokes,
        IReadOnlyList<StepCaptureClick>? clicks)
    {
        var track = FormatTrack(keystrokes, clicks);
        return track is null ? null : $"_Input track: {track}_";
    }

    public static string? FormatOfficeCaption(
        IReadOnlyList<StepCaptureKeyStroke>? keystrokes,
        IReadOnlyList<StepCaptureClick>? clicks)
    {
        var track = FormatTrack(keystrokes, clicks);
        return track is null ? null : $"Input track: {track}";
    }

    private static string FormatButton(StepCaptureClickButton button) =>
        button switch
        {
            StepCaptureClickButton.Right => "right",
            StepCaptureClickButton.Middle => "middle",
            _ => "left"
        };
}
