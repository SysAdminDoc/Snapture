using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Snapture.App.Services;

public enum CliCommandKind
{
    Capture,
    Open,
    Convert,
    Uri,
    Interactive,
    Help,
    Version
}

public sealed record CliCaptureOptions(
    Rectangle? Region,
    bool Fullscreen,
    string? OutputPath,
    bool CopyToClipboard,
    bool Hold,
    int BlockSeconds,
    bool LanShare,
    string? Profile,
    string? Engine);

public sealed record CliOpenOptions(string Path);

public sealed record CliConvertOptions(
    string InputPath,
    string? Format,
    int ResizePercent,
    string? OutputPath);

public sealed record CliUriOptions(CaptureUriRequest Request);

public enum InteractiveCaptureKind
{
    Region,
    Window,
    Fullscreen
}

public sealed record CliInteractiveOptions(InteractiveCaptureKind CaptureKind);

public sealed record CliCommand(
    CliCommandKind Kind,
    CliCaptureOptions? Capture = null,
    CliOpenOptions? Open = null,
    CliConvertOptions? Convert = null,
    CliUriOptions? Uri = null,
    CliInteractiveOptions? Interactive = null);

/// <summary>Strict parser for the non-interactive command-line capture surface.</summary>
public static class CliCommandLine
{
    public const string Usage =
        "snapture --region x,y,width,height --out file.png [--engine auto|winrt|gdi] [--copy] [--hold] [--block seconds] [--lan-share] [--profile name] [--portable]\n" +
        "snapture --fullscreen [--out file.png] [--engine auto|winrt|gdi] [--copy] [--hold] [--block seconds] [--lan-share] [--portable]\n" +
        "snapture --open image.png [--portable]\n" +
        "snapture --convert image.png [--format png|jpg|bmp|webp] [--resize percent] [--out file] [--portable]\n" +
        "snapture --uri \"snapture://capture?mode=region&dest=clipboard\" [--portable]";

    public static void AttachParentConsole()
    {
        try
        {
            bool attached = GetConsoleWindow() != nint.Zero
                || AttachConsole(AttachParentProcess);
            if (!attached && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
                return;

            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
        }
        catch
        {
            // A CLI caller without a console can still use the exit code and output file.
        }
    }

    public static bool IsCliRequest(IReadOnlyList<string> args) =>
        args.Any(arg =>
            arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--version", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--copy", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--clipboard", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--hold", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--lan-share", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--open", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--convert", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--uri", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--new-region", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--new-window", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("--new-fullscreen", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--region", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--out", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--engine", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--block", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--profile", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--format", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--resize", StringComparison.OrdinalIgnoreCase));

    public static bool TryParse(IReadOnlyList<string> args, out CliCommand command, out string error)
    {
        command = new CliCommand(CliCommandKind.Capture);
        error = string.Empty;

        if (args.Count == 0)
        {
            error = Usage;
            return false;
        }

        if (args.Any(arg => arg.Equals("--help", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            command = new CliCommand(CliCommandKind.Help);
            return true;
        }

        if (args.Any(arg => arg.Equals("--version", StringComparison.OrdinalIgnoreCase)))
        {
            command = new CliCommand(CliCommandKind.Version);
            return true;
        }

        Rectangle? region = null;
        bool fullscreen = false;
        string? outputPath = null;
        bool copy = false;
        bool hold = false;
        int blockSeconds = 0;
        bool lanShare = false;
        string? profile = null;
        string? engine = null;
        string? openPath = null;
        string? convertPath = null;
        string? format = null;
        int resizePercent = 0;
        bool resizeSpecified = false;
        string? rawUri = null;
        InteractiveCaptureKind? interactive = null;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg.Equals(PortableMode.Flag, StringComparison.OrdinalIgnoreCase))
                continue;

            if (arg.Equals("--new-region", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--new-window", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--new-fullscreen", StringComparison.OrdinalIgnoreCase))
            {
                if (interactive is not null)
                {
                    error = "Only one interactive capture verb may be specified.";
                    return false;
                }
                interactive = arg.Equals("--new-region", StringComparison.OrdinalIgnoreCase)
                    ? InteractiveCaptureKind.Region
                    : arg.Equals("--new-window", StringComparison.OrdinalIgnoreCase)
                        ? InteractiveCaptureKind.Window
                        : InteractiveCaptureKind.Fullscreen;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--uri", out var uriValue))
            {
                if (rawUri is not null || string.IsNullOrWhiteSpace(uriValue))
                {
                    error = "--uri requires one non-empty snapture:// URI and may only be specified once.";
                    return false;
                }
                rawUri = uriValue;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--open", out var openValue))
            {
                if (openPath is not null || string.IsNullOrWhiteSpace(openValue))
                {
                    error = "--open requires one non-empty image path and may only be specified once.";
                    return false;
                }
                openPath = openValue;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--convert", out var convertValue))
            {
                if (convertPath is not null || string.IsNullOrWhiteSpace(convertValue))
                {
                    error = "--convert requires one non-empty image path and may only be specified once.";
                    return false;
                }
                convertPath = convertValue;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--format", out var formatValue))
            {
                if (format is not null
                    || !ImageConversionService.TryNormalizeFormat(formatValue, out format))
                {
                    error = "--format must be png, jpg, bmp, or webp and may only be specified once.";
                    return false;
                }
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--resize", out var resizeValue))
            {
                if (resizeSpecified
                    || !int.TryParse(resizeValue, NumberStyles.None, CultureInfo.InvariantCulture, out resizePercent)
                    || resizePercent < 1
                    || resizePercent > 1000)
                {
                    error = "--resize requires a percentage from 1 to 1000 and may only be specified once.";
                    return false;
                }
                resizeSpecified = true;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--region", out var regionValue))
            {
                if (region is not null || fullscreen || !TryParseRegion(regionValue, out var parsedRegion))
                {
                    error = "--region requires x,y,width,height with a positive width and height.";
                    return false;
                }
                region = parsedRegion;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--out", out var pathValue))
            {
                if (string.IsNullOrWhiteSpace(pathValue) || outputPath is not null)
                {
                    error = "--out requires one non-empty path and may only be specified once.";
                    return false;
                }
                outputPath = pathValue;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--engine", out var engineValue))
            {
                if (engine is not null
                    || !engineValue.Equals("auto", StringComparison.OrdinalIgnoreCase)
                    && !engineValue.Equals("winrt", StringComparison.OrdinalIgnoreCase)
                    && !engineValue.Equals("gdi", StringComparison.OrdinalIgnoreCase))
                {
                    error = "--engine must be auto, winrt, or gdi and may only be specified once.";
                    return false;
                }
                engine = engineValue.ToLowerInvariant();
                continue;
            }

            if (arg.Equals("--fullscreen", StringComparison.OrdinalIgnoreCase))
            {
                if (region is not null || fullscreen)
                {
                    error = "Choose either --region or --fullscreen, not both.";
                    return false;
                }
                fullscreen = true;
                continue;
            }

            if (arg.Equals("--copy", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--clipboard", StringComparison.OrdinalIgnoreCase))
            {
                copy = true;
                continue;
            }

            if (arg.Equals("--hold", StringComparison.OrdinalIgnoreCase))
            {
                hold = true;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--block", out var blockValue))
            {
                if (blockSeconds != 0
                    || !int.TryParse(blockValue, NumberStyles.None, CultureInfo.InvariantCulture, out blockSeconds)
                    || blockSeconds < 1
                    || blockSeconds > 86_400)
                {
                    error = "--block requires a number of seconds from 1 to 86400.";
                    return false;
                }
                hold = true;
                continue;
            }

            if (TryReadOptionValue(args, ref i, arg, "--profile", out var profileValue))
            {
                if (string.IsNullOrWhiteSpace(profileValue) || profile is not null)
                {
                    error = "--profile requires one non-empty preset key and may only be specified once.";
                    return false;
                }
                profile = profileValue;
                continue;
            }

            if (arg.Equals("--lan-share", StringComparison.OrdinalIgnoreCase))
            {
                lanShare = true;
                continue;
            }

            error = $"Unknown CLI option: {arg}\n\n{Usage}";
            return false;
        }

        if (interactive is not null)
        {
            if (rawUri is not null || openPath is not null || convertPath is not null || format is not null || resizeSpecified
                || region is not null || fullscreen || outputPath is not null || copy || hold || blockSeconds != 0
                || lanShare || profile is not null || engine is not null)
            {
                error = "Interactive capture verbs cannot be combined with other options.";
                return false;
            }

            command = new CliCommand(
                CliCommandKind.Interactive,
                Interactive: new CliInteractiveOptions(interactive.Value));
            return true;
        }

        if (rawUri is not null)
        {
            if (openPath is not null || convertPath is not null || format is not null || resizeSpecified
                || region is not null || fullscreen || outputPath is not null || copy || hold || blockSeconds != 0
                || lanShare || profile is not null || engine is not null)
            {
                error = "--uri cannot be combined with capture, conversion, or delivery options.";
                return false;
            }

            if (!UrlSchemeIntegrationService.TryParse(rawUri, out var request, out var uriError) || request is null)
            {
                error = $"Invalid --uri: {uriError}";
                return false;
            }

            command = new CliCommand(CliCommandKind.Uri, Uri: new CliUriOptions(request));
            return true;
        }

        if (openPath is not null || convertPath is not null || format is not null || resizeSpecified)
        {
            if (openPath is not null && convertPath is not null)
            {
                error = "Choose either --open or --convert, not both.";
                return false;
            }

            if (openPath is not null)
            {
                if (region is not null || fullscreen || outputPath is not null || copy || hold || blockSeconds != 0
                    || lanShare || profile is not null || engine is not null || format is not null || resizeSpecified)
                {
                    error = "--open cannot be combined with capture, conversion, or delivery options.";
                    return false;
                }

                command = new CliCommand(CliCommandKind.Open, Open: new CliOpenOptions(openPath));
                return true;
            }

            if (convertPath is null)
            {
                error = "--format and --resize require --convert image.png.";
                return false;
            }

            if (region is not null || fullscreen || copy || hold || blockSeconds != 0 || lanShare
                || profile is not null || engine is not null)
            {
                error = "--convert cannot be combined with capture or delivery options.";
                return false;
            }

            command = new CliCommand(
                CliCommandKind.Convert,
                Convert: new CliConvertOptions(convertPath, format, resizePercent, outputPath));
            return true;
        }

        if (region is null && !fullscreen)
        {
            error = "A capture source is required: use --region x,y,width,height or --fullscreen.";
            return false;
        }

        if (lanShare)
            hold = true;

        command = new CliCommand(
            CliCommandKind.Capture,
            new CliCaptureOptions(region, fullscreen, outputPath, copy, hold, blockSeconds, lanShare, profile, engine));
        return true;
    }

    private static bool TryReadOptionValue(
        IReadOnlyList<string> args,
        ref int index,
        string arg,
        string option,
        out string value)
    {
        if (arg.Equals(option, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = string.Empty;
                return true;
            }

            value = args[++index];
            return true;
        }

        string prefix = option + "=";
        if (!arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = string.Empty;
            return false;
        }

        value = arg[prefix.Length..];
        return true;
    }

    private static bool TryParseRegion(string value, out Rectangle region)
    {
        region = default;
        var parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height)
            || width <= 0
            || height <= 0)
            return false;

        region = new Rectangle(x, y, width, height);
        return true;
    }

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll")]
    private static extern nint GetConsoleWindow();
}
