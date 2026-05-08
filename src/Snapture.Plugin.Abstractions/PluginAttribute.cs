namespace Snapture.Plugin;

/// <summary>
/// Capabilities a plugin declares. Snapture surfaces these at install time and refuses to
/// load anything that exceeds the host's allow-list. The wire-level set is intentionally
/// small — a plugin should not need to claim more than two or three.
/// </summary>
[Flags]
public enum PluginCapability
{
    None             = 0,
    Network          = 1 << 0, // outbound HTTP / sockets
    FilesystemWrite  = 1 << 1, // anything outside the temp scratch dir
    Clipboard        = 1 << 2, // read or write the system clipboard
    LaunchProcess    = 1 << 3, // start external executables
    InteractWithApp  = 1 << 4, // open windows, drive UI in the host
}

/// <summary>
/// Annotates a plugin entry-point class. The host's loader scans referenced types for this
/// attribute; the metadata block is the authoritative source for the install dialog.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class SnapturePluginAttribute : Attribute
{
    public string Name { get; }
    public string Author { get; }
    public string Version { get; }
    public string Description { get; }
    public PluginCapability Capabilities { get; }

    public SnapturePluginAttribute(string name, string author, string version,
                                   string description, PluginCapability capabilities)
    {
        Name = name;
        Author = author;
        Version = version;
        Description = description;
        Capabilities = capabilities;
    }
}
