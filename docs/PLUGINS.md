# Plugin SDK

Snapture plugins are .NET assemblies that implement one or more contracts from `Snapture.Plugin.Abstractions`. Each plugin runs in its own collectible `AssemblyLoadContext` — no shared state, clean unload.

## Quick start

1. Create a class library targeting `netstandard2.0` or `net10.0`.
2. Reference `Snapture.Plugin.Abstractions` (NuGet or project reference).
3. Annotate your entry-point class with `[SnapturePlugin]` and implement at least one contract.
4. Build, then drop the DLL into `%APPDATA%\Snapture\Plugins\`.
5. Open Snapture → Tray → Tools → Plugins to verify it loaded.

## Contracts

### `IDestination`

Where a capture can be sent after the user clicks a "Send to" option.

```csharp
public interface IDestination
{
    string Id { get; }
    string DisplayName { get; }
    Task SendAsync(PluginCapture capture, IPluginHost host, CancellationToken ct);
}
```

### `ICaptureProcessor`

Runs after a capture is taken and before it lands in the editor. Return value replaces the original capture. May resize the image.

```csharp
public interface ICaptureProcessor
{
    string Id { get; }
    string DisplayName { get; }
    bool RunsByDefault { get; }
    Task<PluginCapture> ProcessAsync(PluginCapture capture, IPluginHost host, CancellationToken ct);
}
```

### `IEditorEffect`

A raster effect invoked from the editor. The host wires up a button or menu entry.

```csharp
public interface IEditorEffect
{
    string Id { get; }
    string DisplayName { get; }
    Task<PluginCapture> ApplyAsync(PluginCapture capture, IPluginHost host, CancellationToken ct);
}
```

## Capability manifest

Every plugin class must declare its capabilities via the `[SnapturePlugin]` attribute:

```csharp
[SnapturePlugin(
    name: "My Uploader",
    author: "You",
    version: "1.0.0",
    description: "Uploads captures to my server.",
    capabilities: PluginCapability.Network)]
public class MyUploader : IDestination { ... }
```

Available flags:

| Flag | Meaning |
|------|---------|
| `None` | Pure in-process work, no external side effects |
| `Network` | Outbound HTTP / socket connections |
| `FilesystemWrite` | Writes outside the plugin scratch directory |
| `Clipboard` | Reads or writes the system clipboard |
| `LaunchProcess` | Starts external executables |
| `InteractWithApp` | Opens windows or drives host UI |

The Plugins window surfaces each plugin's declared capabilities. Users see at a glance what each plugin can do before they trust it.

## `IPluginHost`

The host provides helpers to every plugin:

```csharp
public interface IPluginHost
{
    string ScratchDirectory { get; }
    void ShowToast(string title, string message);
    void Log(string message);
}
```

- `ScratchDirectory` — a per-session temp folder for intermediate files. Cleaned when Snapture exits.
- `ShowToast` — surfaces a non-modal balloon notification in the system tray.
- `Log` — appends to Snapture's log under the plugin's namespace.

## `PluginCapture`

The data object passed to every contract method:

| Property | Type | Description |
|----------|------|-------------|
| `PixelsBgra` | `byte[]` | Raw pixels, BGRA8, top-left origin |
| `Width` | `int` | Image width in pixels |
| `Height` | `int` | Image height in pixels |
| `Stride` | `int` | Bytes per row (may include padding) |
| `Source` | `string` | Capture source description |
| `CapturedAtUtc` | `DateTime` | UTC timestamp |
| `FilePathOnDisk` | `string?` | Path if already saved, null otherwise |

## External processor responses

The host exposes PluginLoader.InvokeProcessorAsync for external adapters such as the
CLI, URL handler, or localhost MCP server. It invokes an ICaptureProcessor by stable ID and
returns a PluginCaptureResponse. MetadataOnly is the default response mode: callers receive
dimensions, stride, a SHA-256 hash of the processed BGRA buffer, capture source, timestamp, and
optional saved path without receiving pixel bytes. A caller must explicitly request
PluginCaptureResponseMode.IncludePixels to receive a defensive copy of the processed buffer.

This default keeps daemon-style responses small and avoids accidentally returning full image
payloads when an external integration only needs capture identity or dimensions.

## Plugin compliance

A plugin's compliance posture is the responsibility of the plugin author. If your plugin declares `Network` and transmits data to a remote endpoint, your plugin is operating outside Snapture's privacy boundary. Document your own data handling.

## Credential storage

Plugins that store credentials (API keys, tokens, passwords) for uploader destinations must encrypt them at rest. Snapture exposes the `IPluginSecretStore` contract through `PluginHostBridge`; its built-in implementation stores each plugin's values in a per-plugin current-user data root and protects them with Windows DPAPI (`ProtectedData.Protect`). Secrets are not serialized into `settings.json`. A plugin may still use the Windows Credential Manager when it needs a separate operating-system credential boundary.
