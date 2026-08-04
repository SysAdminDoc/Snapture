using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Snapture.Plugin;

/// <summary>One captured frame as the plugin sees it. Pixel data is BGRA8, top-left origin.</summary>
public sealed class PluginCapture
{
    public byte[] PixelsBgra { get; }
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public string Source { get; }
    public DateTime CapturedAtUtc { get; }
    public string? FilePathOnDisk { get; }

    public PluginCapture(byte[] pixelsBgra, int width, int height, int stride,
                         string source, DateTime capturedAtUtc, string? filePathOnDisk)
    {
        PixelsBgra = pixelsBgra;
        Width = width;
        Height = height;
        Stride = stride;
        Source = source;
        CapturedAtUtc = capturedAtUtc;
        FilePathOnDisk = filePathOnDisk;
    }
}

/// <summary>Controls how much of a processed capture an external caller receives back.</summary>
public enum PluginCaptureResponseMode
{
    /// <summary>Return dimensions, hash, source, and timestamp without pixel bytes.</summary>
    MetadataOnly,

    /// <summary>Return metadata plus a copy of the processed BGRA8 pixel buffer.</summary>
    IncludePixels
}

/// <summary>Small, serialization-friendly identity for a processed capture.</summary>
public sealed class PluginCaptureMetadata
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public string Sha256 { get; }
    public string Source { get; }
    public DateTime CapturedAtUtc { get; }
    public string? FilePathOnDisk { get; }

    public PluginCaptureMetadata(
        int width,
        int height,
        int stride,
        string sha256,
        string source,
        DateTime capturedAtUtc,
        string? filePathOnDisk)
    {
        Width = width;
        Height = height;
        Stride = stride;
        Sha256 = sha256;
        Source = source;
        CapturedAtUtc = capturedAtUtc;
        FilePathOnDisk = filePathOnDisk;
    }
}

/// <summary>
/// External-caller response for a processed capture. Metadata is always returned; pixel bytes
/// are omitted unless the caller explicitly selects <see cref="PluginCaptureResponseMode.IncludePixels" />.
/// </summary>
public sealed class PluginCaptureResponse
{
    public PluginCaptureMetadata Metadata { get; }
    public byte[]? PixelsBgra { get; }
    public bool IncludesPixels => PixelsBgra is not null;

    private PluginCaptureResponse(PluginCaptureMetadata metadata, byte[]? pixelsBgra)
    {
        Metadata = metadata;
        PixelsBgra = pixelsBgra;
    }

    public static PluginCaptureResponse FromCapture(
        PluginCapture capture,
        PluginCaptureResponseMode mode = PluginCaptureResponseMode.MetadataOnly)
    {
        if (capture is null) throw new ArgumentNullException(nameof(capture));

        var metadata = new PluginCaptureMetadata(
            capture.Width,
            capture.Height,
            capture.Stride,
            ComputeSha256(capture.PixelsBgra),
            capture.Source,
            capture.CapturedAtUtc,
            capture.FilePathOnDisk);
        var pixels = mode == PluginCaptureResponseMode.IncludePixels
            ? (byte[])capture.PixelsBgra.Clone()
            : null;
        return new PluginCaptureResponse(metadata, pixels);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var value in hash)
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        return builder.ToString();
    }
}

/// <summary>Host-supplied helpers a plugin may use during its lifetime.</summary>
public interface IPluginHost
{
    /// <summary>Where the plugin can write temp files. Cleaned up when Snapture exits.</summary>
    string ScratchDirectory { get; }

    /// <summary>Surface a non-modal toast in the host UI.</summary>
    void ShowToast(string title, string message);

    /// <summary>Append one line to the host's log under the plugin's namespace.</summary>
    void Log(string message);
}

/// <summary>
/// Where a capture can be sent. First-party destinations: clipboard, file, LAN-share.
/// Plugins typically add: HTTP upload, Slack webhook, Jira attachment, paste-into-app.
/// </summary>
public interface IDestination
{
    /// <summary>Stable identifier. Lower-case, hyphenated.</summary>
    string Id { get; }

    /// <summary>Display name shown in the editor's Send-to menu.</summary>
    string DisplayName { get; }

    Task SendAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default);
}

/// <summary>
/// Runs after a capture is taken, before it lands in the editor. Use to auto-tag, redact,
/// resize, watermark, etc. Return value replaces the original capture.
/// </summary>
public interface ICaptureProcessor
{
    string Id { get; }
    string DisplayName { get; }
    bool RunsByDefault { get; }
    Task<PluginCapture> ProcessAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default);
}

/// <summary>Editor-side raster effect. Applied via a button/menu the host wires up.</summary>
public interface IEditorEffect
{
    string Id { get; }
    string DisplayName { get; }
    Task<PluginCapture> ApplyAsync(PluginCapture capture, IPluginHost host, CancellationToken ct = default);
}

/// <summary>
/// Optional host-rendered configuration surface. The payload is JSON so a plugin can keep its
/// own schema and persistence policy while Snapture provides a safe local editor and validates
/// that the submitted document is well-formed before handing it back.
/// </summary>
public interface IPluginConfigurable
{
    string ConfigurationTitle { get; }
    string ConfigurationJson { get; }
    void ApplyConfigurationJson(string configurationJson);
}

/// <summary>Collected loader-level result for one plugin assembly.</summary>
public sealed class LoadedPluginInfo
{
    public string AssemblyPath { get; }
    public string Name { get; }
    public string Author { get; }
    public string Version { get; }
    public string Description { get; }
    public PluginCapability Capabilities { get; }
    public IReadOnlyList<string> ContractTypes { get; }
    public string? MinHostVersion { get; }
    public string? MaxHostVersion { get; }

    public LoadedPluginInfo(string assemblyPath, string name, string author, string version,
                             string description, PluginCapability capabilities,
                             IReadOnlyList<string> contractTypes,
                             string? minHostVersion = null, string? maxHostVersion = null)
    {
        AssemblyPath = assemblyPath;
        Name = name;
        Author = author;
        Version = version;
        Description = description;
        Capabilities = capabilities;
        ContractTypes = contractTypes;
        MinHostVersion = minHostVersion;
        MaxHostVersion = maxHostVersion;
    }
}
