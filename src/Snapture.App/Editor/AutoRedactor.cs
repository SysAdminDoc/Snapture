using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Editor;

/// <summary>
/// Pairs the preferred local OCR engine with <see cref="SecretDetector"/> to find secrets in a
/// rendered capture and produce <see cref="RedactShape"/>s aligned to OCR word boxes.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed record RedactionFinding(string RuleId, string Description, SKRect Box, string MatchedText);

[SupportedOSPlatform("windows10.0.17763.0")]
public static class AutoRedactor
{
    public static async Task<IReadOnlyList<RedactionFinding>> ScanAsync(SKBitmap bitmap, ISet<string>? disabledRuleIds = null)
    {
        var result = await OcrService.RecognizeAsync(bitmap);
        if (result is null) return Array.Empty<RedactionFinding>();

        var findings = new List<RedactionFinding>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                foreach (var hit in SecretDetector.Scan(word.Text, disabledRuleIds))
                {
                    var r = word.BoundingBox;
                    // Pad the box slightly so character ascenders/descenders aren't clipped.
                    findings.Add(new RedactionFinding(
                        hit.RuleId, hit.Description,
                        new SKRect(
                            Math.Max(0, r.Left - 2),
                            Math.Max(0, r.Top - 2),
                            r.Right + 2,
                            r.Bottom + 2),
                        hit.Match));
                }
            }
        }
        return findings;
    }

    public static int ApplyToDocument(AnnotationDocument doc, IReadOnlyList<RedactionFinding> findings)
    {
        int added = 0;
        foreach (var f in findings)
        {
            doc.Shapes.Add(new RedactShape
            {
                X = f.Box.Left, Y = f.Box.Top,
                Width = f.Box.Width, Height = f.Box.Height,
                StrokeColorArgb = 0xFF111111
            });
            added++;
        }
        return added;
    }
}
