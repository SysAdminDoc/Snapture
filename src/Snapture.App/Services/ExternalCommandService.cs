using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;
using Snapture.Plugin;

namespace Snapture.App.Services;

public static class ExternalCommandInputModes
{
    public const string Stdin = "stdin";
    public const string FileArgument = "file";

    public static string Normalize(string? value) =>
        string.Equals(value, Stdin, StringComparison.OrdinalIgnoreCase) ? Stdin : FileArgument;
}

/// <summary>One explicitly configured local CLI destination.</summary>
public sealed class ExternalCommandProfile
{
    public string Name { get; set; } = "External command";
    public string ExecutablePath { get; set; } = string.Empty;
    public string Arguments { get; set; } = "{file}";
    public string InputMode { get; set; } = ExternalCommandInputModes.FileArgument;
    public int TimeoutSeconds { get; set; } = 30;

    public ExternalCommandProfile Clone() => new()
    {
        Name = Name,
        ExecutablePath = ExecutablePath,
        Arguments = Arguments,
        InputMode = InputMode,
        TimeoutSeconds = TimeoutSeconds
    };

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? "External command" : Name;
}

public sealed record ExternalCommandRequest(
    byte[]? PngBytes,
    string? ExistingFilePath,
    string Source,
    int Width,
    int Height,
    DateTime CapturedAtUtc)
{
    public static ExternalCommandRequest FromFile(
        string filePath,
        string source = "File",
        int width = 0,
        int height = 0,
        DateTime? capturedAtUtc = null) =>
        new(null, filePath, source, width, height, capturedAtUtc ?? DateTime.UtcNow);
}

public sealed record ExternalCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);

/// <summary>
/// Runs a user-selected executable directly, without a shell. Arguments are tokenized into
/// <see cref="ProcessStartInfo.ArgumentList"/> entries so paths and source metadata cannot become
/// accidental command syntax. The profile is always invoked by an explicit user action.
/// </summary>
public static class ExternalCommandService
{
    public const int MaxInputBytes = 100 * 1024 * 1024;
    public const int MaxOutputCharacters = 128 * 1024;
    public const int MaxTimeoutSeconds = 300;

    private static readonly string[] SupportedPlaceholders =
        ["file", "source", "width", "height", "timestamp"];

    public static void ValidateProfile(ExternalCommandProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("A command name is required.", nameof(profile));
        if (profile.Name.Trim().Length > 80)
            throw new ArgumentException("Command names must be 80 characters or fewer.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.ExecutablePath))
            throw new ArgumentException("An executable path or command name is required.", nameof(profile));
        if (profile.ExecutablePath.IndexOf('\0') >= 0)
            throw new ArgumentException("The executable name contains an invalid character.", nameof(profile));
        if (profile.TimeoutSeconds is < 1 or > MaxTimeoutSeconds)
            throw new ArgumentException($"Timeout must be between 1 and {MaxTimeoutSeconds} seconds.", nameof(profile));

        string mode = ExternalCommandInputModes.Normalize(profile.InputMode);
        if (mode == ExternalCommandInputModes.FileArgument
            && !profile.Arguments.Contains("{file}", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("File-argument commands must include the {file} placeholder.", nameof(profile));
        }

        _ = TokenizeArguments(profile.Arguments);
        foreach (var placeholder in FindPlaceholders(profile.Arguments))
        {
            if (!SupportedPlaceholders.Contains(placeholder, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unsupported command placeholder '{{{placeholder}}}'.", nameof(profile));
        }
    }

    public static IReadOnlyList<string> ExpandArguments(
        string template,
        string? filePath,
        string source,
        int width,
        int height,
        DateTime capturedAtUtc)
    {
        var tokens = TokenizeArguments(template);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = filePath ?? string.Empty,
            ["source"] = source ?? string.Empty,
            ["width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timestamp"] = capturedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)
        };

        var expanded = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            string value = token;
            foreach (var pair in values)
                value = value.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
            if (value.Contains('{') || value.Contains('}'))
                throw new FormatException($"Unsupported command placeholder in argument '{token}'.");
            expanded.Add(value);
        }
        return expanded;
    }

    public static async Task<ExternalCommandResult> RunAsync(
        ExternalCommandProfile profile,
        ExternalCommandRequest request,
        CancellationToken ct = default)
    {
        ValidateProfile(profile);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Width < 0 || request.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Capture dimensions cannot be negative.");

        string mode = ExternalCommandInputModes.Normalize(profile.InputMode);
        string? existingPath = ResolveExistingPath(request.ExistingFilePath);
        byte[]? inputBytes = request.PngBytes;
        if (inputBytes is not null && inputBytes.Length > MaxInputBytes)
            throw new InvalidDataException($"The capture is larger than the {MaxInputBytes / (1024 * 1024)} MB command limit.");

        if (mode == ExternalCommandInputModes.Stdin && inputBytes is null && existingPath is not null)
        {
            inputBytes = await File.ReadAllBytesAsync(existingPath, ct).ConfigureAwait(false);
            if (inputBytes.Length > MaxInputBytes)
                throw new InvalidDataException($"The capture is larger than the {MaxInputBytes / (1024 * 1024)} MB command limit.");
        }

        string? temporaryPath = null;
        try
        {
            bool needsPath = profile.Arguments.Contains("{file}", StringComparison.OrdinalIgnoreCase);
            if (needsPath && existingPath is null)
            {
                if (inputBytes is null)
                    throw new InvalidOperationException("The command needs {file}, but no capture bytes or file path were supplied.");
                string directory = Path.Combine(PortableMode.LocalDataDirectory, "external-command");
                Directory.CreateDirectory(directory);
                temporaryPath = Path.Combine(directory, $"capture_{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(temporaryPath, inputBytes, ct).ConfigureAwait(false);
                existingPath = temporaryPath;
            }

            if (mode == ExternalCommandInputModes.Stdin && inputBytes is null)
                throw new InvalidOperationException("The stdin command has no capture bytes to pipe.");

            var arguments = ExpandArguments(
                profile.Arguments,
                existingPath,
                request.Source,
                request.Width,
                request.Height,
                request.CapturedAtUtc);
            var startInfo = new ProcessStartInfo
            {
                FileName = profile.ExecutablePath.Trim(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = mode == ExternalCommandInputModes.Stdin,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            if (Path.IsPathRooted(startInfo.FileName))
            {
                if (!File.Exists(startInfo.FileName))
                    throw new FileNotFoundException("The configured executable was not found.", startInfo.FileName);
                startInfo.WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(startInfo.FileName))!;
            }
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
                throw new InvalidOperationException("The external command could not be started.");

            var stopwatch = Stopwatch.StartNew();
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (mode == ExternalCommandInputModes.Stdin)
            {
                await process.StandardInput.BaseStream.WriteAsync(inputBytes!, ct).ConfigureAwait(false);
                await process.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(
                    TimeSpan.FromSeconds(profile.TimeoutSeconds), ct).ConfigureAwait(false);
            }
            catch
            {
                TryKill(process);
                throw;
            }

            string stdout = LimitOutput(await stdoutTask.ConfigureAwait(false));
            string stderr = LimitOutput(await stderrTask.ConfigureAwait(false));
            stopwatch.Stop();
            return new ExternalCommandResult(process.ExitCode, stdout, stderr, stopwatch.Elapsed);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { File.Delete(temporaryPath); }
                catch { /* A child process may still have the file open after a failed launch. */ }
            }
        }
    }

    /// <summary>Creates a first-party SDK destination for a configured command profile.</summary>
    public static IDestination CreateDestination(ExternalCommandProfile profile) =>
        new ExternalCommandDestination(profile.Clone());

    private static string? ResolveExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The capture file was not found.", fullPath);
        var info = new FileInfo(fullPath);
        if (info.Length > MaxInputBytes)
            throw new InvalidDataException($"The capture is larger than the {MaxInputBytes / (1024 * 1024)} MB command limit.");
        return fullPath;
    }

    private static string LimitOutput(string value) => value.Length <= MaxOutputCharacters
        ? value
        : value[..MaxOutputCharacters] + Environment.NewLine + "[output truncated]";

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    private static IEnumerable<string> FindPlaceholders(string template)
    {
        int start = -1;
        for (int i = 0; i < template.Length; i++)
        {
            if (template[i] == '{') start = i;
            else if (template[i] == '}' && start >= 0)
            {
                yield return template[(start + 1)..i];
                start = -1;
            }
        }
        if (start >= 0)
            throw new FormatException("An external command argument contains an unclosed placeholder.");
    }

    private static List<string> TokenizeArguments(string template)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        bool started = false;

        for (int i = 0; i < template.Length; i++)
        {
            char ch = template[i];
            if (ch == '"')
            {
                quoted = !quoted;
                started = true;
                continue;
            }
            if (char.IsWhiteSpace(ch) && !quoted)
            {
                if (started)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
                continue;
            }
            current.Append(ch);
            started = true;
        }

        if (quoted)
            throw new FormatException("An external command argument has an unclosed quote.");
        if (started)
            result.Add(current.ToString());
        return result;
    }

    private sealed class ExternalCommandDestination(ExternalCommandProfile profile) : IDestination
    {
        public string Id => "external-command:" + profile.Name.Trim().ToLowerInvariant().Replace(' ', '-');
        public string DisplayName => profile.Name;

        public async Task SendAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(capture);
            ArgumentNullException.ThrowIfNull(host);
            byte[]? png = capture.PixelsBgra is null ? null : EncodePng(capture);
            var result = await RunAsync(
                profile,
                new ExternalCommandRequest(
                    png,
                    capture.FilePathOnDisk,
                    capture.Source,
                    capture.Width,
                    capture.Height,
                    capture.CapturedAtUtc),
                ct).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"External command exited with code {result.ExitCode}: {result.StandardError.Trim()}");
            host.Log($"External command completed: {profile.Name}");
        }

        private static byte[] EncodePng(PluginCapture capture)
        {
            int expected = checked(capture.Stride * capture.Height);
            if (capture.Width <= 0 || capture.Height <= 0 || capture.Stride < capture.Width * 4
                || capture.PixelsBgra!.Length < expected)
                throw new ArgumentException("The plugin capture has an invalid BGRA buffer.", nameof(capture));

            using var bitmap = new SKBitmap(new SKImageInfo(capture.Width, capture.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            IntPtr destination = bitmap.GetPixels();
            for (int y = 0; y < capture.Height; y++)
            {
                Marshal.Copy(
                    capture.PixelsBgra,
                    y * capture.Stride,
                    IntPtr.Add(destination, y * bitmap.RowBytes),
                    capture.Width * 4);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }
}
