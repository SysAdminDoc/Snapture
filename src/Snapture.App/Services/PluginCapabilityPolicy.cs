using System.Globalization;
using Snapture.Plugin;

namespace Snapture.App.Services;

internal static class PluginCapabilityPolicy
{
    public static bool IsApproved(SnaptureSettings settings, PluginLoader.PluginManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifest);
        return manifest.Capabilities == PluginCapability.None
            || settings.ApprovedPluginManifests.Contains(CreateKey(manifest), StringComparer.Ordinal);
    }

    public static void Approve(SnaptureSettings settings, PluginLoader.PluginManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Capabilities == PluginCapability.None) return;
        string key = CreateKey(manifest);
        if (!settings.ApprovedPluginManifests.Contains(key, StringComparer.Ordinal))
            settings.ApprovedPluginManifests.Add(key);
    }

    public static string CreateKey(PluginLoader.PluginManifestInfo manifest) =>
        string.Join('|',
            manifest.Name,
            manifest.Version,
            ((int)manifest.Capabilities).ToString(CultureInfo.InvariantCulture));
}
