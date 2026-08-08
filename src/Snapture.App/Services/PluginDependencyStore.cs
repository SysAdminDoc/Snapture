using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Snapture.Plugin;

namespace Snapture.App.Services;

/// <summary>
/// Downloads plugin-declared tools lazily into a per-plugin cache. Nothing is fetched when a
/// plugin loads; the plugin must call EnsureAsync in response to an explicit feature request.
/// </summary>
public sealed class PluginDependencyStore : IPluginDependencyStore
{
    public const long MaxDependencyBytes = 500L * 1024 * 1024;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex SafePart = new("^[a-zA-Z0-9._-]{1,128}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly string _root;
    private readonly HttpClient _http;

    public PluginDependencyStore(string dataRoot, string pluginIdentity, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginIdentity);
        string identityHash = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(pluginIdentity.Trim()))).ToLowerInvariant();
        _root = Path.Combine(dataRoot, "plugin-tools", identityHash);
        _http = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AllowAutoRedirect = false
        });
    }

    public async Task<string> EnsureAsync(PluginDependency dependency, CancellationToken ct = default)
    {
        Validate(dependency);
        string dependencyHash = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(dependency.Id + "\0" + dependency.Version))).ToLowerInvariant();
        string directory = Path.Combine(_root, dependencyHash);
        string target = Path.Combine(directory, dependency.FileName);
        var gate = Gates.GetOrAdd(target, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(target) && await MatchesHashAsync(target, dependency.Sha256, ct).ConfigureAwait(false))
                return target;
            if (File.Exists(target))
                File.Delete(target);

            Directory.CreateDirectory(directory);
            string temporary = target + $".download-{Guid.NewGuid():N}.tmp";
            try
            {
                using var response = await _http.GetAsync(
                    dependency.DownloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxDependencyBytes)
                    throw new InvalidDataException($"Dependency '{dependency.Id}' exceeds the {MaxDependencyBytes / (1024 * 1024)} MB limit.");

                using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxDependencyBytes)
                        throw new InvalidDataException($"Dependency '{dependency.Id}' exceeds the {MaxDependencyBytes / (1024 * 1024)} MB limit.");
                    hash.AppendData(buffer, 0, read);
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }
                await destination.FlushAsync(ct).ConfigureAwait(false);
                await destination.DisposeAsync().ConfigureAwait(false);
                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, dependency.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Dependency '{dependency.Id}' failed SHA-256 verification.");
                File.Move(temporary, target);
                return target;
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public bool Remove(PluginDependency dependency)
    {
        Validate(dependency);
        string dependencyHash = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(dependency.Id + "\0" + dependency.Version))).ToLowerInvariant();
        string target = Path.Combine(_root, dependencyHash, dependency.FileName);
        if (!File.Exists(target)) return false;
        File.Delete(target);
        return true;
    }

    private static async Task<bool> MatchesHashAsync(string path, string expected, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static void Validate(PluginDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        if (!SafePart.IsMatch(dependency.Id) || !SafePart.IsMatch(dependency.Version))
            throw new ArgumentException("Dependency ID and version may contain only letters, digits, '.', '_' and '-'.", nameof(dependency));
        if (!Uri.TryCreate(dependency.DownloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrWhiteSpace(uri.Host)
            || uri.UserInfo.Length > 0)
            throw new ArgumentException("Dependency downloads require an absolute HTTPS URL.", nameof(dependency));
        if (!SafePart.IsMatch(dependency.FileName) || Path.GetFileName(dependency.FileName) != dependency.FileName)
            throw new ArgumentException("Dependency file names must be simple safe file names.", nameof(dependency));
        if (!Regex.IsMatch(dependency.Sha256, "^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Dependency SHA-256 must be exactly 64 hexadecimal characters.", nameof(dependency));
    }
}
