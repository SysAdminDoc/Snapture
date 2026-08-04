using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Serilog;
using Snapture.Plugin;

namespace Snapture.App.Services;

/// <summary>
/// Discovers and loads plugin assemblies from Snapture's user-data <c>Plugins\*.dll</c> folder. Each
/// plugin is hosted in its own collectible <see cref="AssemblyLoadContext"/> so it can be
/// hot-reloaded without restarting Snapture.
/// </summary>
public sealed class PluginLoader : IDisposable
{
    public static string PluginsDirectory => Path.Combine(PortableMode.LocalDataDirectory, "Plugins");

    public sealed record LoadedPlugin(
        LoadedPluginInfo Info,
        IReadOnlyList<IDestination> Destinations,
        IReadOnlyList<ICaptureProcessor> CaptureProcessors,
        IReadOnlyList<IEditorEffect> EditorEffects,
        AssemblyLoadContext Context,
        IPluginHost Host,
        IReadOnlyList<IPluginConfigurable> Configurables);

    private readonly List<LoadedPlugin> _plugins = new();
    private readonly IPluginHost _host;

    public IReadOnlyList<LoadedPlugin> All => _plugins;

    public PluginLoader(IPluginHost host)
    {
        _host = host;
        Directory.CreateDirectory(PluginsDirectory);
    }

    public void LoadAll()
    {
        UnloadAll();
        foreach (var dll in Directory.EnumerateFiles(PluginsDirectory, "*.dll", SearchOption.AllDirectories))
        {
            try { LoadOne(dll); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Plugin.Load.Failed {FileName}", Path.GetFileName(dll));
                _host.Log($"Plugin load failed: {Path.GetFileName(dll)} — {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Invoke a processor by its stable ID for an external caller. The default response is
    /// metadata-only; explicit pixel requests are opt-in at the call site.
    /// </summary>
    public async Task<PluginCaptureResponse?> InvokeProcessorAsync(
        string processorId,
        PluginCapture capture,
        PluginCaptureResponseMode responseMode = PluginCaptureResponseMode.MetadataOnly,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(processorId))
            throw new ArgumentException("A processor ID is required.", nameof(processorId));
        ArgumentNullException.ThrowIfNull(capture);

        var processor = _plugins
            .Select(plugin => (Plugin: plugin, Processor: plugin.CaptureProcessors.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, processorId, StringComparison.OrdinalIgnoreCase))))
            .FirstOrDefault(candidate => candidate.Processor is not null);
        return processor.Processor is null
            ? null
            : await PluginProcessorInvoker.InvokeAsync(processor.Processor, capture, processor.Plugin.Host, responseMode, ct)
                .ConfigureAwait(false);
    }

    public LoadedPlugin? LoadOne(string dllPath)
    {
        var ctx = new AssemblyLoadContext($"snapture-plugin:{Path.GetFileNameWithoutExtension(dllPath)}",
                                          isCollectible: true);
        // Resolve referenced Snapture.Plugin.Abstractions to the host's already-loaded copy
        // so plugin types are type-equal to host types.
        ctx.Resolving += (alc, name) =>
        {
            if (name.Name == typeof(SnapturePluginAttribute).Assembly.GetName().Name)
                return typeof(SnapturePluginAttribute).Assembly;
            return null;
        };

        Assembly asm;
        try
        {
            using var fs = File.OpenRead(dllPath);
            asm = ctx.LoadFromStream(fs);
        }
        catch
        {
            ctx.Unload();
            throw;
        }

        // Discover the plugin attribute on any class.
        var entry = asm.DefinedTypes
            .FirstOrDefault(t => t.GetCustomAttribute<SnapturePluginAttribute>() is not null);
        if (entry is null)
        {
            ctx.Unload();
            _host.Log($"Plugin skipped (no [SnapturePlugin] attribute): {Path.GetFileName(dllPath)}");
            return null;
        }

        var attr = entry.GetCustomAttribute<SnapturePluginAttribute>()!;

        var hostVersion = typeof(PluginLoader).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        if (!PluginCompatibility.TryValidate(
                attr.MinHostVersion,
                attr.MaxHostVersion,
                hostVersion,
                out var compatibilityReason))
        {
            ctx.Unload();
            string message = $"Plugin skipped (host compatibility): {attr.Name} — {compatibilityReason}";
            _host.Log(message);
            Log.Warning("Plugin.SkippedIncompatible {PluginName} {Minimum} {Maximum} {Reason}",
                attr.Name, attr.MinHostVersion, attr.MaxHostVersion, compatibilityReason);
            return null;
        }

        var contracts = new List<string>();
        var destinations = InstantiateAll<IDestination>(asm, contracts);
        var processors = InstantiateAll<ICaptureProcessor>(asm, contracts);
        var effects = InstantiateAll<IEditorEffect>(asm, contracts);
        var configurables = InstantiateAll<IPluginConfigurable>(asm, contracts);

        var info = new LoadedPluginInfo(
            dllPath, attr.Name, attr.Author, attr.Version, attr.Description,
            attr.Capabilities, contracts, attr.MinHostVersion, attr.MaxHostVersion);

        var pluginHost = _host is PluginHostBridge bridge
            ? bridge.ForPlugin(attr.Name)
            : _host;
        var loaded = new LoadedPlugin(info, destinations, processors, effects, ctx, pluginHost, configurables);
        _plugins.Add(loaded);
        Log.Information("Plugin.Loaded {PluginName} {PluginVersion}", info.Name, info.Version);
        _host.Log($"Plugin loaded: {info.Name} v{info.Version} by {info.Author} " +
                  $"(caps: {info.Capabilities}; types: {string.Join(", ", info.ContractTypes)})");
        return loaded;
    }

    /// <summary>Install a user-selected DLL, replacing the same plugin by declared name.</summary>
    public LoadedPlugin InstallOrUpdate(string sourcePath)
    {
        string source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source)) throw new FileNotFoundException("The selected plugin DLL does not exist.", source);
        if (!string.Equals(Path.GetExtension(source), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Plugins must be DLL files.", nameof(sourcePath));

        var manifest = ReadManifest(source);
        var hostVersion = typeof(PluginLoader).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
        if (!PluginCompatibility.TryValidate(manifest.MinHostVersion, manifest.MaxHostVersion, hostVersion, out var reason))
            throw new InvalidDataException($"Plugin '{manifest.Name}' is incompatible: {reason}");

        var existing = _plugins.FirstOrDefault(plugin =>
            string.Equals(plugin.Info.Name, manifest.Name, StringComparison.OrdinalIgnoreCase));
        string destination = existing?.Info.AssemblyPath
            ?? Path.Combine(PluginsDirectory, Path.GetFileName(source));
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(PluginsDirectory);

        var pathOwner = _plugins.FirstOrDefault(plugin =>
            string.Equals(plugin.Info.AssemblyPath, destination, StringComparison.OrdinalIgnoreCase));
        if (pathOwner is not null && !ReferenceEquals(pathOwner, existing))
            throw new InvalidOperationException($"The destination is already owned by plugin '{pathOwner.Info.Name}'.");

        var temporary = destination + $".install-{Guid.NewGuid():N}.tmp";
        var backup = destination + $".backup-{Guid.NewGuid():N}.tmp";
        bool hadExistingFile = File.Exists(destination);
        try
        {
            if (existing is not null)
            {
                _plugins.Remove(existing);
                Unload(existing);
            }
            if (hadExistingFile) File.Copy(destination, backup, overwrite: true);
            File.Copy(source, temporary, overwrite: true);
            File.Move(temporary, destination, overwrite: true);

            var loaded = LoadOne(destination)
                ?? throw new InvalidDataException($"Plugin '{manifest.Name}' could not be loaded after installation.");
            if (File.Exists(backup)) File.Delete(backup);
            return loaded;
        }
        catch
        {
            try
            {
                if (File.Exists(destination)) File.Delete(destination);
                if (File.Exists(backup))
                {
                    File.Move(backup, destination, overwrite: true);
                    LoadOne(destination);
                }
            }
            catch (Exception restoreError)
            {
                Log.Error(restoreError, "Plugin.Install.RestoreFailed {Path}", destination);
            }
            throw;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(backup)) File.Delete(backup);
        }
    }

    /// <summary>Unload and remove a plugin selected in the Plugins window.</summary>
    public bool Uninstall(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (!_plugins.Remove(plugin)) return false;
        Unload(plugin);
        try
        {
            if (File.Exists(plugin.Info.AssemblyPath)) File.Delete(plugin.Info.AssemblyPath);
            return true;
        }
        catch
        {
            Log.Warning("Plugin.Uninstall.DeleteFailed {Path}", plugin.Info.AssemblyPath);
            return false;
        }
    }

    private void Unload(LoadedPlugin plugin)
    {
        try { plugin.Context.Unload(); } catch { }
        if (plugin.Host is IDisposable disposable)
        {
            try { disposable.Dispose(); } catch { }
        }
    }

    private static PluginManifest ReadManifest(string dllPath)
    {
        var context = new AssemblyLoadContext($"snapture-plugin-manifest:{Guid.NewGuid():N}", isCollectible: true);
        context.Resolving += (_, name) =>
            name.Name == typeof(SnapturePluginAttribute).Assembly.GetName().Name
                ? typeof(SnapturePluginAttribute).Assembly
                : null;
        try
        {
            using var stream = File.OpenRead(dllPath);
            var assembly = context.LoadFromStream(stream);
            var entry = assembly.DefinedTypes
                .FirstOrDefault(type => type.GetCustomAttribute<SnapturePluginAttribute>() is not null);
            if (entry?.GetCustomAttribute<SnapturePluginAttribute>() is not { } attr)
                throw new InvalidDataException("The DLL has no [SnapturePlugin] entry point.");
            return new PluginManifest(attr.Name, attr.MinHostVersion, attr.MaxHostVersion);
        }
        finally { context.Unload(); }
    }

    private sealed record PluginManifest(string Name, string? MinHostVersion, string? MaxHostVersion);

    private static IReadOnlyList<T> InstantiateAll<T>(Assembly asm, List<string> contractsTrace)
    {
        var list = new List<T>();
        foreach (var t in asm.DefinedTypes)
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (!typeof(T).IsAssignableFrom(t)) continue;
            try
            {
                if (Activator.CreateInstance(t) is T inst)
                {
                    list.Add(inst);
                    contractsTrace.Add($"{typeof(T).Name}:{t.Name}");
                }
            }
            catch { /* skip — bad ctor */ }
        }
        return list;
    }

    public void UnloadAll()
    {
        foreach (var p in _plugins)
            Unload(p);
        _plugins.Clear();
    }

    public void Dispose() => UnloadAll();
}

/// <summary>Concrete <see cref="IPluginHost"/> exposed to plugins.</summary>
public sealed class PluginHostBridge : IPluginHost, IPluginSecretStore, IDisposable
{
    public string ScratchDirectory { get; }

    private readonly Action<string, string> _toast;
    private readonly Action<string> _log;

    private readonly PluginSecretStore? _secretStore;

    public PluginHostBridge(
        string scratchDir,
        Action<string, string> toast,
        Action<string> log,
        PluginSecretStore? secretStore = null)
    {
        ScratchDirectory = scratchDir;
        _toast = toast;
        _log = log;
        _secretStore = secretStore;
        Directory.CreateDirectory(scratchDir);
    }

    public PluginHostBridge ForPlugin(string pluginName)
    {
        string safeName = new string(pluginName
            .Select(character => char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "plugin";
        return new PluginHostBridge(
            Path.Combine(ScratchDirectory, safeName),
            _toast,
            _log,
            new PluginSecretStore(PortableMode.LocalDataDirectory, pluginName));
    }

    public void ShowToast(string title, string message) => _toast(title, message);
    public void Log(string message) => _log(message);

    public IReadOnlyList<string> Keys => _secretStore?.Keys ?? Array.Empty<string>();

    public bool TryGetSecret(string key, out string value)
    {
        if (_secretStore is null)
        {
            value = string.Empty;
            return false;
        }
        return _secretStore.TryGetSecret(key, out value);
    }

    public void SetSecret(string key, string value) =>
        (_secretStore ?? throw new InvalidOperationException("Secret storage is unavailable for this host.")).SetSecret(key, value);

    public bool RemoveSecret(string key) =>
        _secretStore?.RemoveSecret(key) == true;

    public void Dispose() => _secretStore?.Dispose();
}
