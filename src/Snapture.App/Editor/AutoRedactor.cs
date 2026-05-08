using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using SkiaSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace Snapture.App.Editor;

/// <summary>
/// Pairs <see cref="Windows.Media.Ocr"/> with <see cref="SecretDetector"/> to find secrets in
/// a rendered capture and produce <see cref="RedactShape"/>s aligned to OCR word boxes.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed record RedactionFinding(string RuleId, string Description, SKRect Box, string MatchedText);

[SupportedOSPlatform("windows10.0.17763.0")]
public static class AutoRedactor
{
    public static async Task<IReadOnlyList<RedactionFinding>> ScanAsync(SKBitmap bitmap)
    {
        var ocr = OcrEngine.TryCreateFromUserProfileLanguages();
        if (ocr is null) return Array.Empty<RedactionFinding>();

        // SKBitmap → SoftwareBitmap (BGRA8 premul) via PNG.
        using var ms = new MemoryStream();
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(ms);
        }
        ms.Position = 0;
        var ras = new InMemoryRandomAccessStream();
        using (var w = new DataWriter(ras))
        {
            w.WriteBytes(ms.ToArray());
            await w.StoreAsync();
            await w.FlushAsync();
            w.DetachStream();
        }
        ras.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var sw = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await ocr.RecognizeAsync(sw);

        var findings = new List<RedactionFinding>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                foreach (var hit in SecretDetector.Scan(word.Text))
                {
                    var r = word.BoundingRect;
                    // Pad the box slightly so character ascenders/descenders aren't clipped.
                    findings.Add(new RedactionFinding(
                        hit.RuleId, hit.Description,
                        new SKRect(
                            (float)Math.Max(0, r.Left - 2),
                            (float)Math.Max(0, r.Top - 2),
                            (float)(r.Right + 2),
                            (float)(r.Bottom + 2)),
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
