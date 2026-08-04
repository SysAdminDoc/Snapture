using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Snapture.App.Services;

public enum CliCommandKind
{
    Capture,
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

public sealed record CliCommand(CliCommandKind Kind, CliCaptureOptions? Capture = null);

/// <summary>Strict parser for the non-interactive command-line capture surface.</summary>
public static class CliCommandLine
{
    public const string Usage = "snapture --region x,y,width,height --out file.png [--engine auto|winrt|gdi] [--copy] [--hold] [--block seconds] [--lan-share] [--profile name]";

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
            || arg.StartsWith("--region", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--out", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--engine", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--block", StringComparison.OrdinalIgnoreCase)
            || arg.StartsWith("--profile", StringComparison.OrdinalIgnoreCase));

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

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
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
