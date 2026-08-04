using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Snapture.Plugin;

namespace Snapture.App.Services;

/// <summary>
/// Per-plugin Windows DPAPI secret store. Values are encrypted for the current Windows user,
/// scoped to the plugin identity, and written atomically below Snapture's local data root.
/// </summary>
public sealed class PluginSecretStore : IPluginSecretStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private bool _disposed;

    public PluginSecretStore(string dataRoot, string pluginIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginIdentity);
        string identityHash = Convert.ToHexString(SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(pluginIdentity.Trim()))).ToLowerInvariant();
        _path = Path.Combine(dataRoot, "plugin-secrets", identityHash + ".bin");
        Load();
    }

    public IReadOnlyList<string> Keys
    {
        get
        {
            lock (_gate) return _values.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray();
        }
    }

    public bool TryGetSecret(string key, out string value)
    {
        ValidateKey(key);
        lock (_gate) return _values.TryGetValue(key, out value!);
    }

    public void SetSecret(string key, string value)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            ThrowIfDisposed();
            _values[key] = value;
            Save();
        }
    }

    public bool RemoveSecret(string key)
    {
        ValidateKey(key);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_values.Remove(key)) return false;
            Save();
            return true;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var encrypted = File.ReadAllBytes(_path);
            var json = Unprotect(encrypted);
            var values = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (values is null) return;
            foreach (var pair in values)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                    _values[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // A corrupt or copied-to-another-user store must not make the host fail to start.
            Serilog.Log.Warning(ex, "PluginSecrets.LoadFailed {Path}", _path);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(_values, JsonOptions);
        byte[] encrypted = Protect(json);
        string temporary = _path + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllBytes(temporary, encrypted);
        try
        {
            if (File.Exists(_path))
            {
                try { File.Replace(temporary, _path, null); }
                catch (PlatformNotSupportedException) { File.Move(temporary, _path, overwrite: true); }
            }
            else
            {
                File.Move(temporary, _path);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(PluginSecretStore));
    }

    private static void ValidateKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (key.Length > 256 || key.Contains('\0'))
            throw new ArgumentException("Secret keys must be 1-256 characters without null bytes.", nameof(key));
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }

    private static byte[] Protect(byte[] input)
    {
        var source = new DataBlob(input);
        try
        {
            if (!CryptProtectData(ref source.Blob, null, nint.Zero, nint.Zero, nint.Zero, 1, out var output))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not protect plugin secrets.");
            try { return CopyBlob(output); }
            finally { LocalFree(output.pbData); }
        }
        finally { source.Dispose(); }
    }

    private static byte[] Unprotect(byte[] input)
    {
        var source = new DataBlob(input);
        try
        {
            if (!CryptUnprotectData(ref source.Blob, out var outputDescription, nint.Zero, nint.Zero, nint.Zero, 1, out var output))
                throw new CryptographicException($"Windows could not unprotect plugin secrets (error {Marshal.GetLastWin32Error()}).");
            try
            {
                if (outputDescription != nint.Zero) LocalFree(outputDescription);
                return CopyBlob(output);
            }
            finally { LocalFree(output.pbData); }
        }
        finally { source.Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBlob
    {
        public int cbData;
        public nint pbData;
    }

    private sealed class DataBlob : IDisposable
    {
        public NativeBlob Blob;

        public DataBlob(byte[] bytes)
        {
            Blob.cbData = bytes.Length;
            Blob.pbData = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, Blob.pbData, bytes.Length);
        }

        public void Dispose()
        {
            if (Blob.pbData == nint.Zero) return;
            Marshal.FreeHGlobal(Blob.pbData);
            Blob.pbData = nint.Zero;
            Blob.cbData = 0;
        }
    }

    private static byte[] CopyBlob(NativeBlob blob)
    {
        if (blob.cbData < 0 || blob.pbData == nint.Zero)
            throw new CryptographicException("Windows returned an invalid protected-data buffer.");
        var bytes = new byte[blob.cbData];
        Marshal.Copy(blob.pbData, bytes, 0, bytes.Length);
        return bytes;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref NativeBlob dataIn,
        string? description,
        nint optionalEntropy,
        nint reserved,
        nint promptStruct,
        uint flags,
        out NativeBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref NativeBlob dataIn,
        out nint description,
        nint optionalEntropy,
        nint reserved,
        nint promptStruct,
        uint flags,
        out NativeBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
