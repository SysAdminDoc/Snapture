using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using ImageMagick;
using SkiaSharp;

namespace Snapture.App.Services;

public sealed record CodeBlockAnalysis(
    bool IsLikelyCode,
    int Score,
    int CodeLineCount,
    int LineCount,
    bool MonospaceLike);

public sealed record CodeAwareCaptureResult(
    string OutputPath,
    CodeBlockAnalysis Analysis,
    int Width,
    int Height);

/// <summary>
/// Turns an OCR-readable code screenshot into a polished local code card. The original capture is
/// never replaced; the result is a new PNG/JPG/BMP/WebP export with a gradient surround and code
/// window chrome.
/// </summary>
public static class CodeAwareCaptureService
{
    private const long MaxInputBytes = 100L * 1024 * 1024;
    private const int MaxInputDimension = 16_384;
    private const int MaxLines = 400;
    private const int MaxLineLength = 2_000;
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff"
    };
    private static readonly Regex CodeTokenPattern = new(
        "//.*|#.*|\"(?:\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*'|\\b(?:using|namespace|class|public|private|protected|internal|static|void|return|if|else|for|foreach|while|new|var|const|async|await|def|function|import|from|SELECT|FROM|WHERE|let|interface)\\b|\\b\\d+(?:\\.\\d+)?\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CodeSignalPattern = new(
        "[{};]|=>|//|#include|\\b(?:using|namespace|class|def|function|return|var|const|SELECT|FROM|WHERE|import|interface)\\b|\\w+\\s*=\\s*[^=]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static async Task<CodeAwareCaptureResult> CreateFromImageAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string input = Path.GetFullPath(inputPath);
        ValidateInput(input);

        using var image = LoadBitmap(input);
        cancellationToken.ThrowIfCancellationRequested();
        var recognition = await OcrService.RecognizeAsync(image).ConfigureAwait(false);
        if (recognition is null || recognition.Lines.Count == 0)
            throw new InvalidOperationException("No OCR text was found in the capture.");

        var analysis = Analyze(recognition);
        if (!analysis.IsLikelyCode)
            throw new InvalidOperationException($"The capture does not look like a code block (score {analysis.Score}/100).");

        var lines = NormalizeLines(recognition.Lines.Select(line => line.Text));
        using var card = RenderCodeCard(lines);
        cancellationToken.ThrowIfCancellationRequested();
        string output = ResolveOutputPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output) ?? ".");
        WriteImage(card, output);
        return new CodeAwareCaptureResult(output, analysis, card.Width, card.Height);
    }

    /// <summary>Scores OCR output without invoking an OCR engine, which keeps the detector testable.</summary>
    public static CodeBlockAnalysis Analyze(OcrRecognitionResult recognition)
    {
        ArgumentNullException.ThrowIfNull(recognition);
        var lines = recognition.Lines
            .Select(line => line.Text?.TrimEnd() ?? string.Empty)
            .Where(line => line.Length > 0)
            .Take(MaxLines)
            .ToArray();
        if (lines.Length == 0)
            return new CodeBlockAnalysis(false, 0, 0, 0, false);

        int codeLines = lines.Count(line => CodeSignalPattern.IsMatch(line));
        bool monospace = IsMonospaceLike(recognition);
        int score = 0;
        score += Math.Min(55, codeLines * 18);
        if (lines.Length >= 3) score += 15;
        if (lines.Any(line => line.Contains("    ", StringComparison.Ordinal) || line.StartsWith('\t'))) score += 10;
        if (monospace) score += 25;
        if (recognition.Text.Count(character => character == '\n') >= 2) score += 5;
        return new CodeBlockAnalysis(score >= 45, Math.Min(100, score), codeLines, lines.Length, monospace);
    }

    /// <summary>Renders normalized OCR lines to the same deterministic code-card surface used by export.</summary>
    internal static Bitmap RenderCodeCard(IEnumerable<string> sourceLines)
    {
        var lines = NormalizeLines(sourceLines).ToArray();
        if (lines.Length == 0)
            throw new ArgumentException("At least one non-empty code line is required.", nameof(sourceLines));

        using var measureBitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var measureGraphics = Graphics.FromImage(measureBitmap);
        using var font = new Font("Cascadia Code", 18, FontStyle.Regular, GraphicsUnit.Pixel);
        int maxTextWidth = lines.Max(line => checked((int)Math.Ceiling(measureGraphics.MeasureString(line, font).Width)));
        int lineHeight = Math.Max(24, (int)Math.Ceiling(font.GetHeight(measureGraphics)) + 6);
        int contentWidth = Math.Clamp(maxTextWidth + 44, 360, 2_800);
        int contentHeight = checked(lines.Length * lineHeight + 32);
        int width = checked(contentWidth + 56);
        int height = checked(contentHeight + 92);
        using var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(output);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var backdrop = new LinearGradientBrush(
            new Rectangle(0, 0, width, height),
            Color.FromArgb(255, 47, 28, 85),
            Color.FromArgb(255, 28, 86, 112),
            35f);
        graphics.FillRectangle(backdrop, 0, 0, width, height);

        var card = new Rectangle(28, 28, contentWidth, contentHeight + 36);
        using var shadow = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
        graphics.FillRectangle(shadow, new Rectangle(card.X + 6, card.Y + 8, card.Width, card.Height));
        using var cardBrush = new SolidBrush(Color.FromArgb(255, 30, 30, 46));
        graphics.FillRectangle(cardBrush, card);
        using var chromeBrush = new SolidBrush(Color.FromArgb(255, 42, 42, 60));
        graphics.FillRectangle(chromeBrush, card.X, card.Y, card.Width, 36);
        DrawChrome(graphics, card);

        float y = card.Y + 36 + 16;
        foreach (string line in lines)
        {
            DrawHighlightedLine(graphics, line, font, card.X + 22, y);
            y += lineHeight;
        }
        return new Bitmap(output);
    }

    private static bool IsMonospaceLike(OcrRecognitionResult recognition)
    {
        var widths = recognition.Lines
            .SelectMany(line => line.Words)
            .Where(word => word.Text.Length >= 2 && word.BoundingBox.Width > 0)
            .Select(word => word.BoundingBox.Width / word.Text.Length)
            .OrderBy(width => width)
            .ToArray();
        if (widths.Length < 4)
            return false;
        float median = widths[widths.Length / 2];
        if (median <= 0)
            return false;
        float deviation = widths.Max(width => Math.Abs(width - median)) / median;
        return deviation <= 0.45f;
    }

    private static IReadOnlyList<string> NormalizeLines(IEnumerable<string> sourceLines)
        => sourceLines
            .Select(line => (line ?? string.Empty).TrimEnd())
            .Take(MaxLines)
            .Select(line => line.Length > MaxLineLength ? line[..MaxLineLength] : line)
            .Where(line => line.Length > 0)
            .ToArray();

    private static void DrawChrome(Graphics graphics, Rectangle card)
    {
        using var red = new SolidBrush(Color.FromArgb(255, 255, 95, 86));
        using var yellow = new SolidBrush(Color.FromArgb(255, 255, 189, 46));
        using var green = new SolidBrush(Color.FromArgb(255, 39, 201, 63));
        graphics.FillEllipse(red, card.X + 16, card.Y + 13, 10, 10);
        graphics.FillEllipse(yellow, card.X + 32, card.Y + 13, 10, 10);
        graphics.FillEllipse(green, card.X + 48, card.Y + 13, 10, 10);
        using var titleFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(Color.FromArgb(190, 214, 214, 230));
        graphics.DrawString("code capture", titleFont, titleBrush, card.X + 76, card.Y + 10);
    }

    private static void DrawHighlightedLine(Graphics graphics, string line, Font font, float x, float y)
    {
        using var plain = new SolidBrush(Color.FromArgb(232, 224, 226, 240));
        float cursor = x;
        int position = 0;
        foreach (Match match in CodeTokenPattern.Matches(line))
        {
            if (match.Index > position)
                cursor += DrawText(graphics, line[position..match.Index], font, plain, cursor, y);
            using var brush = new SolidBrush(TokenColor(match.Value));
            cursor += DrawText(graphics, match.Value, font, brush, cursor, y);
            position = match.Index + match.Length;
        }
        if (position < line.Length)
            DrawText(graphics, line[position..], font, plain, cursor, y);
    }

    private static float DrawText(Graphics graphics, string text, Font font, Brush brush, float x, float y)
    {
        graphics.DrawString(text, font, brush, x, y);
        return graphics.MeasureString(text, font).Width;
    }

    private static Color TokenColor(string token)
    {
        if (token.StartsWith("//", StringComparison.Ordinal) || token.StartsWith('#'))
            return Color.FromArgb(255, 137, 180, 121);
        if (token.StartsWith('"') || token.StartsWith('\''))
            return Color.FromArgb(255, 249, 226, 175);
        if (char.IsDigit(token[0]))
            return Color.FromArgb(255, 137, 220, 235);
        return Color.FromArgb(255, 203, 166, 247);
    }

    private static void ValidateInput(string input)
    {
        if (!File.Exists(input))
            throw new FileNotFoundException("The source image does not exist.", input);
        if (!ImageExtensions.Contains(Path.GetExtension(input)))
            throw new ArgumentException("The source must be a supported still image.", nameof(input));
        if (new FileInfo(input).Length > MaxInputBytes)
            throw new InvalidDataException("The source image exceeds the 100 MB safety limit.");
    }

    private static SKBitmap LoadBitmap(string path)
    {
        using var image = new MagickImage(path);
        if (image.Width is 0 or > MaxInputDimension || image.Height is 0 or > MaxInputDimension)
            throw new InvalidDataException($"The source image dimensions exceed {MaxInputDimension} pixels.");
        image.Format = MagickFormat.Png;
        using var png = new MemoryStream();
        image.Write(png);
        return SKBitmap.Decode(png.ToArray()) ?? throw new InvalidDataException("The source image could not be decoded.");
    }

    private static string ResolveOutputPath(string outputPath)
    {
        string output = Path.GetFullPath(outputPath);
        string extension = Path.GetExtension(output);
        if (string.IsNullOrEmpty(extension))
            return output + ".png";
        if (!ImageConversionService.TryNormalizeFormat(extension, out _))
            throw new ArgumentException("The output must use png, jpg, bmp, or webp.", nameof(outputPath));
        return output;
    }

    private static void WriteImage(Bitmap image, string output)
    {
        string extension = Path.GetExtension(output);
        string format = ImageConversionService.TryNormalizeFormat(extension, out var normalized)
            ? normalized
            : "png";
        using var png = new MemoryStream();
        image.Save(png, ImageFormat.Png);
        png.Position = 0;
        using var encoded = new MagickImage(png);
        if (format == "jpg")
        {
            encoded.BackgroundColor = MagickColors.White;
            encoded.Alpha(AlphaOption.Remove);
            encoded.Quality = 92;
        }
        encoded.Write(output, format switch
        {
            "png" => MagickFormat.Png,
            "jpg" => MagickFormat.Jpeg,
            "bmp" => MagickFormat.Bmp,
            "webp" => MagickFormat.WebP,
            _ => throw new ArgumentException("Unsupported output format.", nameof(output))
        });
    }
}
