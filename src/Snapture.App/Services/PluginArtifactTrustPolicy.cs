using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace Snapture.App.Services;

internal enum PluginTrustState
{
    Unapproved,
    Approved,
    ArtifactChanged,
    VersionChanged
}

/// <summary>
/// Records explicit approval for the exact plugin artifact selected by the user. This is an
/// integrity allowlist, not a signature or sandbox: plugin code still runs in-process.
/// </summary>
internal static class PluginArtifactTrustPolicy
{
    private const string KeyPrefix = "plugin-artifact-v1|";

    public static PluginTrustState GetState(
        SnaptureSettings settings,
        PluginLoader.PluginManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
            return PluginTrustState.Unapproved;

        string exact = CreateKey(manifest);
        if (settings.ApprovedPluginManifests.Contains(exact, StringComparer.Ordinal))
            return PluginTrustState.Approved;

        string identityPrefix = CreateIdentityPrefix(manifest);
        var related = settings.ApprovedPluginManifests
            .Where(key => key.StartsWith(identityPrefix, StringComparison.Ordinal))
            .ToArray();
        if (related.Length == 0)
            return PluginTrustState.Unapproved;

        string versionPrefix = identityPrefix + Encode(manifest.Version) + "|";
        return related.Any(key => key.StartsWith(versionPrefix, StringComparison.Ordinal))
            ? PluginTrustState.ArtifactChanged
            : PluginTrustState.VersionChanged;
    }

    public static bool IsApproved(
        SnaptureSettings settings,
        PluginLoader.PluginManifestInfo manifest) =>
        GetState(settings, manifest) == PluginTrustState.Approved;

    public static void Approve(
        SnaptureSettings settings,
        PluginLoader.PluginManifestInfo manifest)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(manifest.Sha256))
            throw new InvalidOperationException("A plugin artifact hash is required before approval.");

        string key = CreateKey(manifest);
        if (!settings.ApprovedPluginManifests.Contains(key, StringComparer.Ordinal))
            settings.ApprovedPluginManifests.Add(key);
    }

    public static string CreateKey(PluginLoader.PluginManifestInfo manifest) =>
        CreateIdentityPrefix(manifest) + Encode(manifest.Version) + "|" + manifest.Sha256.ToLowerInvariant();

    public static string ComputeSha256(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CreateIdentityPrefix(PluginLoader.PluginManifestInfo manifest) =>
        KeyPrefix + Encode(manifest.Name) + "|" + Encode(manifest.Author) + "|";

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
