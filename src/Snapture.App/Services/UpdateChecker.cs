using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Serilog;

namespace Snapture.App.Services;

public static class UpdateChecker
{
    private const string ReleaseUrl = "https://api.github.com/repos/SysAdminDoc/Snapture/releases/latest";

    public sealed record UpdateResult(
        bool Available,
        string CurrentVersion,
        string LatestVersion,
        string? HtmlUrl,
        bool VelopackEnabled = false,
        string? Error = null);

    public static async Task<UpdateResult> CheckAsync()
    {
        string current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        var velopack = await VelopackUpdateService.CheckAsync().ConfigureAwait(false);
        if (velopack.Supported)
        {
            return new UpdateResult(
                velopack.Available,
                velopack.CurrentVersion,
                velopack.LatestVersion,
                null,
                VelopackEnabled: true,
                velopack.Error);
        }

        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Snapture-UpdateCheck/1.0");
            http.Timeout = TimeSpan.FromSeconds(10);
            var json = await http.GetStringAsync(ReleaseUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string tagName = root.GetProperty("tag_name").GetString() ?? "";
            string htmlUrl = root.GetProperty("html_url").GetString() ?? "";
            string latest = tagName.TrimStart('v', 'V');
            bool available = Version.TryParse(latest, out var latestVer)
                && Version.TryParse(current, out var currentVer)
                && latestVer > currentVer;
            Log.Information("UpdateChecker {Current} {Latest} {Available}", current, latest, available);
            return new UpdateResult(available, current, latest, htmlUrl);
        }
        catch (Exception ex)
        {
            string error = OutboundDataFlowAudit.RedactSensitive(ex.Message);
            Log.Warning("UpdateChecker.Failed {Error}", error);
            return new UpdateResult(false, current, current, null, Error: error);
        }
    }

    public static Task<bool> DownloadPendingAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default) =>
        VelopackUpdateService.DownloadPendingAsync(progress, cancellationToken);

    public static bool ApplyPendingAndRestart() => VelopackUpdateService.ApplyPendingAndRestart();
}
