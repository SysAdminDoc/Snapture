using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Snapture.App.Services;

/// <summary>
/// One image registered for LAN sharing. Tokens are 32 random bytes (URL-safe base64).
/// </summary>
public sealed record LanShareEntry(string Token, string FilePath, DateTime CreatedUtc, TimeSpan Ttl, bool Consumed);

/// <summary>
/// Local-only share server. Binds Kestrel to a single user-chosen adapter (never 0.0.0.0 by
/// default), serves <c>/s/{token}</c> URLs that resolve to a file on disk. Tokens are
/// single-fetch by default and TTL-bounded. mDNS / firewall integration deferred to v0.4.x.
/// </summary>
public sealed class LanShareServer : IDisposable
{
    private readonly ConcurrentDictionary<string, LanShareEntry> _entries = new();
    private IHost? _host;
    private string? _bindAddress;
    private int _port;
    private bool _disposed;

    public bool IsRunning => _host is not null;
    public string? BindAddress => _bindAddress;
    public int Port => _port;

    /// <summary>Returns base URL like <c>http://192.168.1.42:9087</c>.</summary>
    public string? BaseUrl => _host is null ? null : $"http://{_bindAddress}:{_port}";

    /// <summary>Per-adapter IPv4 candidates the user can choose between.</summary>
    public static IReadOnlyList<(string Adapter, string Ip)> EnumerateAdapters()
    {
        var list = new List<(string, string)>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(addr.Address)) continue;
                    list.Add((nic.Name, addr.Address.ToString()));
                }
            }
        }
        catch { /* swallow */ }
        return list;
    }

    public void Start(string bindIp, int port)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LanShareServer));
        Stop();

        if (!IPAddress.TryParse(bindIp, out var ip))
            throw new ArgumentException($"Invalid IP: {bindIp}", nameof(bindIp));

        _bindAddress = bindIp;
        _port = port;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.Listen(ip, port);
        });

        var app = builder.Build();

        // Health endpoint — confirms server identity.
        app.MapGet("/", () => Results.Text(
            "Snapture LAN share. Use the /s/{token} URL the desktop app generated.",
            "text/plain"));

        // Token-served images.
        app.MapGet("/s/{token}", (string token) =>
        {
            if (!_entries.TryGetValue(token, out var entry))
                return Results.NotFound();
            if (DateTime.UtcNow - entry.CreatedUtc > entry.Ttl)
            {
                _entries.TryRemove(token, out _);
                return Results.NotFound();
            }
            if (!File.Exists(entry.FilePath))
            {
                _entries.TryRemove(token, out _);
                return Results.NotFound();
            }

            // Single-fetch: mark consumed and remove on first read.
            _entries.TryRemove(token, out _);
            var ct = entry.FilePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                     entry.FilePath.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? "image/jpeg"
                : entry.FilePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                    ? "image/webp"
                    : "image/png";
            return Results.File(entry.FilePath, ct);
        });

        _host = app;
        _ = app.RunAsync();
    }

    public void Stop()
    {
        try { _host?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
        try { _host?.Dispose(); } catch { }
        _host = null;
        _entries.Clear();
    }

    /// <summary>Register a file for sharing. Returns the full URL.</summary>
    public string Register(string filePath, TimeSpan? ttl = null)
    {
        if (_host is null) throw new InvalidOperationException("Server not started.");
        if (!File.Exists(filePath)) throw new FileNotFoundException("Cannot share a missing file.", filePath);

        var token = TokenString();
        var entry = new LanShareEntry(token, filePath, DateTime.UtcNow, ttl ?? TimeSpan.FromMinutes(15), false);
        _entries[token] = entry;
        return $"{BaseUrl}/s/{token}";
    }

    public IReadOnlyList<LanShareEntry> ActiveEntries() => _entries.Values.ToList();

    public bool Revoke(string token) => _entries.TryRemove(token, out _);

    private static string TokenString()
    {
        Span<byte> buf = stackalloc byte[24];
        RandomNumberGenerator.Fill(buf);
        // URL-safe base64
        return Convert.ToBase64String(buf).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
