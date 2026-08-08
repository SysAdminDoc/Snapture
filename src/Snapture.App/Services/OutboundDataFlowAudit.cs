using System.Text.RegularExpressions;

namespace Snapture.App.Services;

public enum OutboundDataFlowBoundaryKind
{
    HttpClient,
    KestrelListener,
    Update,
    Process,
    PluginTrustBoundary
}

public sealed record OutboundDataFlowEntry(
    string Id,
    OutboundDataFlowBoundaryKind Kind,
    string Source,
    string Trigger,
    string Destination,
    string Payload,
    string Credentials,
    string Transport,
    string Retention,
    string FailureBehavior,
    string DocumentationKey);

public sealed record OutboundDataFlowAuditReport(
    bool IsComplete,
    IReadOnlyList<string> Issues);

/// <summary>
/// The maintained, machine-readable inventory of Snapture's network and process boundaries.
/// Keep this list synchronized whenever a new HTTP, Kestrel, updater, plugin, or child-process
/// path is added. Tests deliberately require every known ID and every audit field.
/// </summary>
public static class OutboundDataFlowAudit
{
    public const string LocalAiDiscoveryHttp = "local-ai-discovery-http";
    public const string LocalAiFoundryProcess = "local-ai-foundry-process";
    public const string LocalAiInferenceHttp = "local-ai-inference-http";
    public const string LanShareListener = "lan-share-listener";
    public const string McpListener = "mcp-listener";
    public const string GithubUpdateApi = "github-update-api";
    public const string VelopackUpdateFeed = "velopack-update-feed";
    public const string DeclarativeUploader = "declarative-uploader";
    public const string NextcloudUpload = "nextcloud-upload";
    public const string ImmichUpload = "immich-upload";
    public const string PluginDependencyDownload = "plugin-dependency-download";
    public const string ExternalCommand = "external-command";
    public const string OneOcrSidecar = "oneocr-sidecar";
    public const string MagnificationHelper = "magnification-helper";
    public const string PluginCode = "plugin-code";
    public const string WindowsShell = "windows-shell";

    private static readonly string[] RequiredIds =
    [
        LocalAiDiscoveryHttp,
        LocalAiFoundryProcess,
        LocalAiInferenceHttp,
        LanShareListener,
        McpListener,
        GithubUpdateApi,
        VelopackUpdateFeed,
        DeclarativeUploader,
        NextcloudUpload,
        ImmichUpload,
        PluginDependencyDownload,
        ExternalCommand,
        OneOcrSidecar,
        MagnificationHelper,
        PluginCode,
        WindowsShell
    ];

    private static readonly IReadOnlyList<OutboundDataFlowEntry> Inventory =
    [
        new(
            LocalAiDiscoveryHttp,
            OutboundDataFlowBoundaryKind.HttpClient,
            "LocalAiProviderService",
            "Explicit Local AI discovery from the editor or AI settings",
            "Ollama 127.0.0.1:11434, LM Studio 127.0.0.1:1234, or a Foundry Local loopback endpoint",
            "GET model and service-status metadata; no capture pixels",
            "None",
            "Loopback HTTP only; endpoints are validated before requests",
            "Responses are parsed in memory and not written by Snapture",
            "Unavailable providers are skipped; discovery errors are surfaced as no available model",
            "privacy.local-ai-discovery"),
        new(
            LocalAiFoundryProcess,
            OutboundDataFlowBoundaryKind.Process,
            "LocalAiProviderService.RunFoundryServiceStatusAsync",
            "Local AI discovery when the Foundry Local CLI is installed",
            "The user-selected `foundry` executable resolved from PATH",
            "Service status arguments and bounded discovery output; no capture pixels",
            "None",
            "Hidden child process with shell execution disabled",
            "Output is consumed in memory for endpoint discovery",
            "A missing, failed, or timed-out CLI marks Foundry Local unavailable",
            "privacy.local-ai-foundry"),
        new(
            LocalAiInferenceHttp,
            OutboundDataFlowBoundaryKind.HttpClient,
            "LocalAiInferenceService.SendImageAsync",
            "Explicit Local AI send after the user selects a discovered model",
            "The selected provider's validated loopback `/v1/chat/completions` endpoint",
            "Flattened capture as a base64 PNG image part, model ID, and user prompt",
            "None; cloud endpoints are rejected",
            "Loopback HTTP only; request timeout is two minutes",
            "Response is shown locally and not retained as an AI transcript",
            "HTTP and model errors are shown in the editor with sensitive text redacted",
            "privacy.local-ai-inference"),
        new(
            LanShareListener,
            OutboundDataFlowBoundaryKind.KestrelListener,
            "LanShareServer.Start / Register",
            "Explicit Share to LAN action or an opted-in capture preset/CLI flag",
            "A Kestrel listener on the single user-selected LAN IPv4 adapter",
            "Only the explicitly registered local image file, served once at a random token URL",
            "Random URL-safe token; no account credential",
            "Inbound HTTP on the selected adapter; Snapture makes no outbound request",
            "The registered file remains local; the token expires or is removed after one fetch",
            "Missing, expired, or already-consumed tokens return 404; startup failure is reported",
            "privacy.lan-share"),
        new(
            McpListener,
            OutboundDataFlowBoundaryKind.KestrelListener,
            "McpServer.Start / HandlePostAsync",
            "Explicit MCP enable/start and authenticated client tool calls",
            "Loopback `http://127.0.0.1:<port>/mcp` only",
            "JSON-RPC tool parameters; saved capture metadata by default and pixels only when requested",
            "In-memory bearer token rotated on every start; never serialized",
            "Inbound loopback HTTP with loopback, Origin, and exact bearer-token checks",
            "Token is cleared on stop; capture files remain under the normal local history policy",
            "Unauthorized requests are rejected; protocol/tool failures are visible without returning secrets",
            "privacy.mcp"),
        new(
            GithubUpdateApi,
            OutboundDataFlowBoundaryKind.Update,
            "UpdateChecker.CheckAsync",
            "One explicit tray Check for updates click on an unpackaged build",
            "`https://api.github.com/repos/SysAdminDoc/Snapture/releases/latest`",
            "A fixed User-Agent and release metadata request; no capture or settings data",
            "None",
            "HTTPS GET with `Snapture-UpdateCheck/1.0`",
            "Release JSON is parsed in memory and discarded",
            "The tray shows the sanitized network error and does not retry automatically",
            "privacy.github-update"),
        new(
            VelopackUpdateFeed,
            OutboundDataFlowBoundaryKind.Update,
            "VelopackUpdateService.CheckAsync / DownloadPendingAsync / ApplyPendingAndRestart",
            "Explicit tray update check, then separate user-confirmed download and restart",
            "Architecture-specific HTTPS GitHub release feed at the fixed Snapture repository",
            "Release metadata and the selected signed-by-hash package payload; no capture data",
            "None",
            "HTTPS through Velopack; x64 and ARM64 stable channels are selected locally",
            "Velopack stages packages in its local update cache until the user applies them",
            "Check/download failures are visible and sanitized; apply only proceeds when a pending update exists",
            "privacy.velopack"),
        new(
            DeclarativeUploader,
            OutboundDataFlowBoundaryKind.HttpClient,
            "DeclarativeUploaderService.UploadAsync",
            "Explicit editor or tray upload after selecting a user-owned profile",
            "The profile's expanded HTTP or HTTPS URL; HTTP is called out as unencrypted",
            "Flattened PNG plus the profile's configured body, query parameters, and metadata arguments",
            "User-configured headers may contain credentials; preview hides all header values and URLs are redacted",
            "HTTP(S) with bounded input, response, and timeout; redirects are not followed by the built-in client",
            "The response is held for the visible result only; Snapture does not retain it",
            "HTTP status and transport exceptions are visible; error text is redacted before UI/log use",
            "privacy.declarative-uploader"),
        new(
            NextcloudUpload,
            OutboundDataFlowBoundaryKind.HttpClient,
            "SelfHostedDestinationService.UploadNextcloudAsync",
            "Explicit editor or tray upload after Nextcloud is enabled",
            "Configured Nextcloud WebDAV path under the user's server URL",
            "The flattened PNG at the configured remote folder and file name",
            "Current-user DPAPI credential sent as Basic authorization; never displayed or serialized",
            "Configured HTTP(S); the confirmation warns when the server uses unencrypted HTTP",
            "The remote Nextcloud server owns the uploaded file; response body is capped and shown only for the result",
            "HTTP status, timeout, and transport failures are visible with sensitive text redacted",
            "privacy.nextcloud"),
        new(
            ImmichUpload,
            OutboundDataFlowBoundaryKind.HttpClient,
            "SelfHostedDestinationService.UploadImmichAsync",
            "Explicit editor or tray upload after Immich is enabled",
            "Configured Immich `/api/assets` endpoint and optional album-assignment endpoint",
            "The flattened PNG plus device ID, capture timestamp, favorite flag, and optional album ID",
            "Current-user DPAPI API key in `x-api-key`; never displayed or serialized",
            "Configured HTTP(S); the confirmation warns when the server uses unencrypted HTTP",
            "The remote Immich server owns the asset; response body is capped and used for album assignment",
            "Upload and album-assignment failures are visible, including partial-success state, with redaction",
            "privacy.immich"),
        new(
            PluginDependencyDownload,
            OutboundDataFlowBoundaryKind.HttpClient,
            "PluginDependencyStore.EnsureAsync",
            "Explicit plugin feature request; never plugin discovery or startup",
            "The plugin-declared absolute HTTPS URL with a simple file name",
            "GET of the pinned dependency artifact; no capture data",
            "None; URLs with embedded user info are rejected",
            "HTTPS only, streamed to a per-plugin temporary file, then SHA-256 verified",
            "Verified artifacts remain in the per-plugin local cache until removed",
            "HTTP, size, cancellation, and hash failures are returned to the requesting plugin without exposing secrets",
            "privacy.plugin-dependencies"),
        new(
            ExternalCommand,
            OutboundDataFlowBoundaryKind.Process,
            "ExternalCommandService.RunAsync",
            "Explicit editor or tray command action after a user-owned profile is selected",
            "The configured executable path or PATH command",
            "Flattened PNG through stdin or a temporary `{file}` path, plus declared source/dimension/time arguments",
            "No Snapture credential injection; the child inherits only the normal process environment",
            "Hidden process, shell execution disabled, tokenized ArgumentList, bounded input and returned output",
            "Temporary file is deleted after the process; command owns anything it persists",
            "Exit code, stdout/stderr, timeout, and launch failures are visible to the caller",
            "privacy.external-command"),
        new(
            OneOcrSidecar,
            OutboundDataFlowBoundaryKind.Process,
            "OcrService.RunOneOcrSidecarAsync",
            "Explicit OCR request when the user configured a OneOCR executable",
            "The configured local OCR executable",
            "Encoded capture bytes on stdin; recognized text/geometry on stdout",
            "None",
            "Hidden process with shell execution disabled and `stdin stdout` arguments",
            "Input and bounded output are held only for the OCR result",
            "Timeout, non-zero exit, malformed output, and launch errors fall back to another local OCR engine",
            "privacy.oneocr"),
        new(
            MagnificationHelper,
            OutboundDataFlowBoundaryKind.Process,
            "MagnificationCapture.CaptureAsync",
            "Capture fallback when the Windows Magnification path is selected",
            "A short-lived Snapture helper process launched from the current executable",
            "Validated rectangle coordinates in arguments; bounded PNG output on stdout",
            "None",
            "Hidden child process with shell execution disabled and a five-second timeout",
            "The helper exits after one capture; returned pixels are decoded in memory",
            "Oversized, timed-out, failed, or malformed helper output is rejected and capture falls back",
            "privacy.magnification-helper"),
        new(
            PluginCode,
            OutboundDataFlowBoundaryKind.PluginTrustBoundary,
            "PluginLoader and PluginHostBridge",
            "Explicit plugin install/load and later approved contract invocation",
            "Third-party code loaded in a collectible in-process AssemblyLoadContext",
            "Plugin-defined; it may receive capture pixels and host metadata through approved contracts",
            "Capability manifest approval is required, but there is no OS sandbox; plugin secrets use DPAPI",
            "In-process .NET execution; Network and LaunchProcess capabilities are visible before approval",
            "Plugin-owned network/process data and retention are outside Snapture's first-party privacy boundary",
            "Malformed, incompatible, unapproved, or failing plugins are rejected/unloaded; trusted plugin code remains its author's responsibility",
            "privacy.plugins"),
        new(
            WindowsShell,
            OutboundDataFlowBoundaryKind.Process,
            "Explorer and ms-settings Process.Start calls",
            "Explicit reveal/open-settings actions from the UI",
            "Windows Explorer or the Windows Settings shell",
            "A selected local path or fixed `ms-settings:` URI; no capture bytes",
            "None",
            "User-approved shell launch; not an Snapture network client",
            "The Windows shell owns any navigation state; Snapture retains no additional data",
            "Shell launch failures are best-effort and do not affect capture data",
            "privacy.windows-shell")
    ];

    private static readonly Regex SensitiveKeyValue = new(
        """(?<![?&])(?<key>\b(?:authorization|proxy-authorization|x-api-key|api[-_ ]?key|access[-_ ]?token|token|password|secret|credential|cookie|set-cookie)\b)\s*[:=]\s*(?:(?:bearer|basic)\s+)?(?<value>"[^"]*"|'[^']*'|[^\s,;)}\]]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SensitiveQueryValue = new(
        @"(?<prefix>[?&](?:api[-_]?key|access[-_]?token|token|password|secret|credential|key)=)(?<value>[^&#\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AuthorizationValue = new(
        @"\b(?<scheme>Bearer|Basic)\s+(?<value>[A-Za-z0-9+/=_\-.]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<OutboundDataFlowEntry> Entries => Inventory;

    public static IReadOnlyList<string> KnownBoundaryIds => RequiredIds;

    public static OutboundDataFlowAuditReport Audit()
    {
        var issues = new List<string>();
        var duplicateIds = Inventory
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"duplicate boundary ID '{group.Key}'");
        issues.AddRange(duplicateIds);

        var present = Inventory.Select(entry => entry.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        issues.AddRange(RequiredIds
            .Where(id => !present.Contains(id))
            .Select(id => $"missing known boundary '{id}'"));

        foreach (var entry in Inventory)
        {
            if (string.IsNullOrWhiteSpace(entry.Id)) issues.Add("an entry has no ID");
            if (string.IsNullOrWhiteSpace(entry.Source)) issues.Add($"{entry.Id} has no source");
            if (string.IsNullOrWhiteSpace(entry.Trigger)) issues.Add($"{entry.Id} has no trigger");
            if (string.IsNullOrWhiteSpace(entry.Destination)) issues.Add($"{entry.Id} has no destination");
            if (string.IsNullOrWhiteSpace(entry.Payload)) issues.Add($"{entry.Id} has no payload description");
            if (string.IsNullOrWhiteSpace(entry.Credentials)) issues.Add($"{entry.Id} has no credential description");
            if (string.IsNullOrWhiteSpace(entry.Transport)) issues.Add($"{entry.Id} has no transport description");
            if (string.IsNullOrWhiteSpace(entry.Retention)) issues.Add($"{entry.Id} has no retention description");
            if (string.IsNullOrWhiteSpace(entry.FailureBehavior)) issues.Add($"{entry.Id} has no failure description");
            if (string.IsNullOrWhiteSpace(entry.DocumentationKey)) issues.Add($"{entry.Id} has no documentation key");
        }

        return new OutboundDataFlowAuditReport(issues.Count == 0, issues);
    }

    public static string RedactSensitive(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        string redacted = SensitiveKeyValue.Replace(value, match =>
            $"{match.Groups["key"].Value}: [redacted]");
        redacted = SensitiveQueryValue.Replace(redacted, match =>
            $"{match.Groups["prefix"].Value}[redacted]");
        return AuthorizationValue.Replace(redacted, match =>
            $"{match.Groups["scheme"].Value} [redacted]");
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes / (1024d * 1024d):0.0} MB"
    };

    public static string TrimForDisplay(string? value, int maximum = 120)
    {
        string text = RedactSensitive(value).Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= maximum ? text : text[..maximum] + "…";
    }
}
