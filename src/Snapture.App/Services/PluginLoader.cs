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
        AssemblyLoadContext Context);

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
            .SelectMany(plugin => plugin.CaptureProcessors)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, processorId, StringComparison.OrdinalIgnoreCase));
        return processor is null
            ? null
            : await PluginProcessorInvoker.InvokeAsync(processor, capture, _host, responseMode, ct)
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

        var info = new LoadedPluginInfo(
            dllPath, attr.Name, attr.Author, attr.Version, attr.Description,
            attr.Capabilities, contracts, attr.MinHostVersion, attr.MaxHostVersion);

        var loaded = new LoadedPlugin(info, destinations, processors, effects, ctx);
        _plugins.Add(loaded);
        Log.Information("Plugin.Loaded {PluginName} {PluginVersion}", info.Name, info.Version);
        _host.Log($"Plugin loaded: {info.Name} v{info.Version} by {info.Author} " +
                  $"(caps: {info.Capabilities}; types: {string.Join(", ", info.ContractTypes)})");
        return loaded;
    }

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
        {
            try { p.Context.Unload(); } catch { }
        }
        _plugins.Clear();
    }

    public void Dispose() => UnloadAll();
}

/// <summary>Concrete <see cref="IPluginHost"/> exposed to plugins.</summary>
public sealed class PluginHostBridge : IPluginHost
{
    public string ScratchDirectory { get; }

    private readonly Action<string, string> _toast;
    private readonly Action<string> _log;

    public PluginHostBridge(string scratchDir, Action<string, string> toast, Action<string> log)
    {
        ScratchDirectory = scratchDir;
        _toast = toast;
        _log = log;
        Directory.CreateDirectory(scratchDir);
    }

    public void ShowToast(string title, string message) => _toast(title, message);
    public void Log(string message) => _log(message);
}
