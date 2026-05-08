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

    public LoadedPluginInfo(string assemblyPath, string name, string author, string version,
                             string description, PluginCapability capabilities,
                             IReadOnlyList<string> contractTypes)
    {
        AssemblyPath = assemblyPath;
        Name = name;
        Author = author;
        Version = version;
        Description = description;
        Capabilities = capabilities;
        ContractTypes = contractTypes;
    }
}
