using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SkiaSharp;
using Snapture.App.Editor;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>
/// Opt-in, loopback-only MCP server. The endpoint implements the JSON-RPC methods needed by
/// MCP clients for initialization, tool discovery, and tool invocation over Streamable HTTP.
/// It intentionally does not share the LAN-share adapter or bind to a non-loopback address.
/// </summary>
public sealed class McpServer : IDisposable
{
    public const string EndpointPath = "/mcp";
    public const string ProtocolVersion = "2025-11-25";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly SettingsService _settings;
    private readonly Func<ICaptureEngine> _engine;
    private readonly CaptureOrchestrator _orchestrator;
    private readonly CaptureHistoryService _history;
    private readonly Action<string>? _onToolCall;
    private readonly SemaphoreSlim _toolGate = new(2, 2);
    private WebApplication? _app;
    private bool _disposed;

    public McpServer(
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator,
        CaptureHistoryService history,
        Action<string>? onToolCall = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _onToolCall = onToolCall;
    }

    public bool IsRunning => _app is not null;
    public int Port { get; private set; }
    public string? BaseUrl => IsRunning ? $"http://127.0.0.1:{Port}{EndpointPath}" : null;

    public void Start(int port)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(McpServer));
        if (port is < 1024 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "MCP port must be between 1024 and 65535.");

        Stop();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(McpServer).Assembly.GetName().Name
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (!IsLoopbackRequest(context) || !McpOriginPolicy.IsAllowed(context.Request.Headers.Origin.FirstOrDefault()))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next().ConfigureAwait(false);
        });
        app.MapGet(EndpointPath, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
        app.MapPost(EndpointPath, HandlePostAsync);

        try
        {
            app.StartAsync().GetAwaiter().GetResult();
            _app = app;
            Port = port;
            Log.Information("Mcp.Started {BaseUrl} {ProtocolVersion}", BaseUrl, ProtocolVersion);
        }
        catch
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }
    }

    public void Stop()
    {
        try { _app?.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
        try { _app?.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        _app = null;
        Port = 0;
    }

    private async Task HandlePostAsync(HttpContext context)
    {
        context.Response.Headers["MCP-Protocol-Version"] = ProtocolVersion;
        if (context.Request.ContentLength is > 1_048_576)
        {
            await WriteRpcErrorAsync(context, null, -32600, "MCP request is too large.", StatusCodes.Status413PayloadTooLarge)
                .ConfigureAwait(false);
            return;
        }

        JsonDocument? document = null;
        try
        {
            document = await JsonDocument.ParseAsync(
                context.Request.Body,
                new JsonDocumentOptions { MaxDepth = 32 },
                context.RequestAborted).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("jsonrpc", out var jsonRpc)
                || jsonRpc.GetString() != "2.0"
                || !root.TryGetProperty("method", out var methodElement)
                || methodElement.ValueKind != JsonValueKind.String)
            {
                await WriteRpcErrorAsync(context, null, -32600, "Invalid JSON-RPC request.", StatusCodes.Status400BadRequest)
                    .ConfigureAwait(false);
                return;
            }

            string method = methodElement.GetString()!;
            bool hasId = root.TryGetProperty("id", out var id);
            JsonObject? result;
            try
            {
                result = await DispatchAsync(method, root.TryGetProperty("params", out var parameters)
                    ? parameters
                    : default).ConfigureAwait(false);
            }
            catch (McpProtocolException ex)
            {
                if (!hasId)
                {
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                await WriteRpcErrorAsync(context, id, ex.Code, ex.Message, StatusCodes.Status200OK)
                    .ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mcp.RequestFailed {Method}", method);
                if (!hasId)
                {
                    context.Response.StatusCode = StatusCodes.Status202Accepted;
                    return;
                }

                await WriteRpcErrorAsync(context, id, -32603, "Internal MCP server error.", StatusCodes.Status200OK)
                    .ConfigureAwait(false);
                return;
            }

            if (!hasId)
            {
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            var response = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = JsonNodeFrom(id),
                ["result"] = result
            };
            await WriteJsonAsync(context, response, StatusCodes.Status200OK).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteRpcErrorAsync(context, null, -32700, "Invalid JSON.", StatusCodes.Status400BadRequest)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // The client disconnected; there is no response to write.
        }
        finally
        {
            document?.Dispose();
        }
    }

    private async Task<JsonObject?> DispatchAsync(string method, JsonElement parameters)
    {
        switch (method)
        {
            case "initialize":
                return new JsonObject
                {
                    ["protocolVersion"] = SelectProtocolVersion(parameters),
                    ["capabilities"] = new JsonObject
                    {
                        ["tools"] = new JsonObject { ["listChanged"] = false }
                    },
                    ["serverInfo"] = new JsonObject
                    {
                        ["name"] = "snapture",
                        ["title"] = "Snapture",
                        ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "unknown",
                        ["description"] = "Local-first Windows capture, OCR, history, and redaction tools."
                    },
                    ["instructions"] = "Snapture is loopback-only and opt-in. Capture tools return metadata and a saved local path by default; set include_image=true only when the full PNG is required."
                };
            case "notifications/initialized":
                return null;
            case "ping":
                return new JsonObject();
            case "tools/list":
                return new JsonObject { ["tools"] = McpToolCatalog.CreateJson() };
            case "tools/call":
                return await DispatchToolCallAsync(parameters).ConfigureAwait(false);
            default:
                throw new McpProtocolException(-32601, $"Method '{method}' is not supported.");
        }
    }

    private async Task<JsonObject> DispatchToolCallAsync(JsonElement parameters)
    {
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("name", out var nameElement)
            || nameElement.ValueKind != JsonValueKind.String)
            throw new McpProtocolException(-32602, "tools/call requires a tool name.");

        string name = nameElement.GetString()!;
        var arguments = parameters.TryGetProperty("arguments", out var args)
            && args.ValueKind == JsonValueKind.Object
            ? args
            : default;
        if (!McpToolCatalog.Contains(name))
            throw new McpProtocolException(-32602, $"Unknown tool '{name}'.");

        _onToolCall?.Invoke(name);
        await _toolGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await McpToolOperations.CallAsync(
                name, arguments, _settings, _engine, _orchestrator, _history).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mcp.ToolFailed {Tool}", name);
            return McpToolOperations.ErrorResult(ex.Message);
        }
        finally
        {
            _toolGate.Release();
        }
    }

    private static string SelectProtocolVersion(JsonElement parameters)
    {
        if (parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("protocolVersion", out var version)
            && version.ValueKind == JsonValueKind.String
            && version.GetString() is { } requested
            && requested is "2025-11-25" or "2025-06-18" or "2025-03-26")
            return requested;
        return ProtocolVersion;
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote is null) return true;
        if (remote.IsIPv4MappedToIPv6) remote = remote.MapToIPv4();
        return IPAddress.IsLoopback(remote);
    }

    private static JsonNode? JsonNodeFrom(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? null : JsonNode.Parse(element.GetRawText());

    private static async Task WriteRpcErrorAsync(
        HttpContext context,
        JsonElement? id,
        int code,
        string message,
        int status)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id is null ? null : JsonNodeFrom(id.Value),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
        await WriteJsonAsync(context, response, status).ConfigureAwait(false);
    }

    private static async Task WriteJsonAsync(HttpContext context, JsonObject payload, int status)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(payload.ToJsonString(JsonOptions), context.RequestAborted)
            .ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _toolGate.Dispose();
    }
}

internal sealed class McpProtocolException(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

internal static class McpOriginPolicy
{
    public static bool IsAllowed(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return true;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            return false;
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }
}

internal static class McpToolCatalog
{
    private static readonly IReadOnlyDictionary<string, JsonObject> Definitions =
        BuildDefinitions().ToDictionary(tool => (string)tool["name"]!, StringComparer.Ordinal);

    public static bool Contains(string name) => Definitions.ContainsKey(name);

    public static JsonArray CreateJson()
    {
        var tools = new JsonArray();
        foreach (var tool in Definitions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            tools.Add(tool.Value.DeepClone());
        return tools;
    }

    private static IEnumerable<JsonObject> BuildDefinitions()
    {
        yield return Tool(
            "capture_region",
            "Capture a fixed screen rectangle without opening the editor. Returns metadata and a saved local path; set include_image=true for the full PNG.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["x"] = Number("Screen-space left coordinate."),
                    ["y"] = Number("Screen-space top coordinate."),
                    ["width"] = Number("Capture width in pixels."),
                    ["height"] = Number("Capture height in pixels."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false."),
                    ["output_path"] = String("Optional output path under the configured Snapture output folder.")
                },
                "x", "y", "width", "height"));

        yield return Tool(
            "capture_window",
            "Capture a visible top-level window by handle, process id, title substring, or class name. Use list_windows first when the target is ambiguous.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["window_handle"] = String("Win32 HWND as decimal or 0x-prefixed hexadecimal."),
                    ["process_id"] = Number("Target process id."),
                    ["title_contains"] = String("Case-insensitive window title substring."),
                    ["class_name"] = String("Case-insensitive Win32 window class name."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false."),
                    ["output_path"] = String("Optional output path under the configured Snapture output folder.")
                }));

        yield return Tool(
            "capture_monitor",
            "Capture one monitor without opening the editor. Use list_monitors to discover device names and bounds.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["monitor_index"] = Number("Zero-based monitor index from list_monitors."),
                    ["device_name"] = String("Exact device name such as \\\\.\\DISPLAY1."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false."),
                    ["output_path"] = String("Optional output path under the configured Snapture output folder.")
                }));

        yield return Tool(
            "capture_element",
            "Capture a UI Automation element inside a window by automation id, name substring, or control type. This does not click or focus the element.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["window_handle"] = String("Root window HWND as decimal or 0x-prefixed hexadecimal."),
                    ["automation_id"] = String("Exact UI Automation AutomationId."),
                    ["name_contains"] = String("Case-insensitive UI Automation Name substring."),
                    ["control_type"] = String("UI Automation control type, for example Button or Edit."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false."),
                    ["output_path"] = String("Optional output path under the configured Snapture output folder.")
                },
                "window_handle"));

        yield return Tool(
            "capture_scrolling",
            "Capture a vertically scrollable window through UI Automation and stitch the frames. The target window is restored to its original scroll position when possible.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["window_handle"] = String("Root window HWND as decimal or 0x-prefixed hexadecimal."),
                    ["process_id"] = Number("Target process id used to resolve a visible window."),
                    ["title_contains"] = String("Case-insensitive window title substring."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false."),
                    ["output_path"] = String("Optional output path under the configured Snapture output folder.")
                },
                "window_handle"));

        yield return Tool(
            "ocr_image",
            "Run local OCR on a history capture or an image path. Returns recognized text and word geometry; the source image is omitted unless include_image=true.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["capture_id"] = Number("History capture id."),
                    ["path"] = String("Existing image path under the user profile or Snapture data folders."),
                    ["language"] = String("Optional Windows OCR language tag such as en-US."),
                    ["include_image"] = Boolean("Embed the full PNG in the MCP result; defaults to false.")
                }));

        yield return Tool(
            "history_search",
            "Search Snapture's local SQLite/FTS5 capture history by OCR text, source application, or window title. Results are metadata-first and include local paths.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["query"] = String("FTS search text; omit or leave empty for recent captures."),
                    ["limit"] = Number("Maximum result count, capped at 100."),
                    ["verified_redacted"] = Boolean("If true, return only captures marked Verified-redacted.")
                }));

        yield return Tool(
            "auto_redact",
            "Run local OCR plus Snapture's enabled secret/PII rule pack, write a new redacted PNG, and mark the result Verified-redacted. The original is never overwritten.",
            Schema(
                new Dictionary<string, JsonNode?>
                {
                    ["capture_id"] = Number("History capture id."),
                    ["path"] = String("Existing image path under the user profile or Snapture data folders."),
                    ["output_path"] = String("Optional new PNG path under the configured Snapture output folder."),
                    ["include_image"] = Boolean("Embed the full redacted PNG in the MCP result; defaults to false.")
                }));

        yield return Tool(
            "list_windows",
            "List visible, capturable top-level windows with handles, process ids, classes, titles, and bounds.",
            Schema(new Dictionary<string, JsonNode?>()));

        yield return Tool(
            "list_monitors",
            "List active monitors with device names, bounds, work areas, primary status, and DPI.",
            Schema(new Dictionary<string, JsonNode?>()));
    }

    private static JsonObject Tool(string name, string description, JsonObject schema) => new()
    {
        ["name"] = name,
        ["title"] = name.Replace('_', ' '),
        ["description"] = description,
        ["inputSchema"] = schema,
        ["annotations"] = new JsonObject
        {
            ["readOnlyHint"] = name is "history_search" or "list_windows" or "list_monitors" or "ocr_image",
            ["destructiveHint"] = false,
            ["idempotentHint"] = name is "history_search" or "list_windows" or "list_monitors" or "ocr_image",
            ["openWorldHint"] = false
        }
    };

    private static JsonObject Schema(
        IReadOnlyDictionary<string, JsonNode?> properties,
        params string[] required)
    {
        var propertyObject = new JsonObject();
        foreach (var property in properties)
            propertyObject[property.Key] = property.Value;

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = propertyObject,
            ["additionalProperties"] = false
        };
        if (required.Length > 0)
        {
            var requiredArray = new JsonArray();
            foreach (var name in required) requiredArray.Add(name);
            schema["required"] = requiredArray;
        }
        return schema;
    }

    private static JsonObject String(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description
    };

    private static JsonObject Number(string description) => new()
    {
        ["type"] = "integer",
        ["description"] = description
    };

    private static JsonObject Boolean(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
        ["default"] = false
    };
}

internal static class McpToolOperations
{
    private const int MaxImageBytes = 25 * 1024 * 1024;

    public static async Task<JsonObject> CallAsync(
        string name,
        JsonElement arguments,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator,
        CaptureHistoryService history)
    {
        return name switch
        {
            "capture_region" => await CaptureRegionAsync(arguments, settings, engine, orchestrator).ConfigureAwait(false),
            "capture_window" => await CaptureWindowAsync(arguments, settings, engine, orchestrator).ConfigureAwait(false),
            "capture_monitor" => await CaptureMonitorAsync(arguments, settings, engine, orchestrator).ConfigureAwait(false),
            "capture_element" => await CaptureElementAsync(arguments, settings, engine, orchestrator).ConfigureAwait(false),
            "capture_scrolling" => await CaptureScrollingAsync(arguments, settings, engine, orchestrator).ConfigureAwait(false),
            "ocr_image" => await OcrImageAsync(arguments, settings, history).ConfigureAwait(false),
            "history_search" => HistorySearch(arguments, history),
            "auto_redact" => await AutoRedactAsync(arguments, settings, history).ConfigureAwait(false),
            "list_windows" => ListWindows(),
            "list_monitors" => ListMonitors(),
            _ => throw new McpProtocolException(-32602, $"Unknown tool '{name}'.")
        };
    }

    public static JsonObject ErrorResult(string message) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = message
            }
        },
        ["isError"] = true
    };

    private static async Task<JsonObject> CaptureRegionAsync(
        JsonElement args,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator)
    {
        int x = RequiredInt(args, "x");
        int y = RequiredInt(args, "y");
        int width = RequiredInt(args, "width");
        int height = RequiredInt(args, "height");
        ValidateCaptureSize(width, height);
        var result = await engine().CaptureRegionAsync(new Rectangle(x, y, width, height)).ConfigureAwait(false);
        return await DeliverCaptureAsync(result, "MCP.Region", args, settings, orchestrator).ConfigureAwait(false);
    }

    private static async Task<JsonObject> CaptureWindowAsync(
        JsonElement args,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator)
    {
        var window = ResolveWindow(args);
        var result = await engine().CaptureWindowAsync(window.Handle).ConfigureAwait(false);
        return await DeliverCaptureAsync(result, "MCP.Window", args, settings, orchestrator,
            new JsonObject
            {
                ["window_handle"] = FormatHandle(window.Handle),
                ["window_title"] = window.Title,
                ["window_class"] = window.ClassName,
                ["process_id"] = window.ProcessId
            }).ConfigureAwait(false);
    }

    private static async Task<JsonObject> CaptureMonitorAsync(
        JsonElement args,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator)
    {
        var monitor = ResolveMonitor(args);
        var result = await engine().CaptureMonitorAsync(monitor).ConfigureAwait(false);
        return await DeliverCaptureAsync(result, "MCP.Monitor", args, settings, orchestrator,
            new JsonObject
            {
                ["device_name"] = monitor.DeviceName,
                ["monitor_index"] = MonitorEnumerator.Enumerate().ToList().FindIndex(m => m.Handle == monitor.Handle)
            }).ConfigureAwait(false);
    }

    private static async Task<JsonObject> CaptureElementAsync(
        JsonElement args,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator)
    {
        nint windowHandle = RequiredHandle(args, "window_handle");
        if (!WindowEnumerator.GetExtendedFrameBounds(windowHandle, out _))
            throw new ArgumentException("The supplied window_handle is not a visible capturable window.");

        var element = ResolveElement(windowHandle, args);
        var rect = element.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width < 1 || rect.Height < 1)
            throw new InvalidOperationException("The matching UI Automation element has no visible bounds.");

        var bounds = new Rectangle(
            (int)Math.Round(rect.X),
            (int)Math.Round(rect.Y),
            (int)Math.Round(rect.Width),
            (int)Math.Round(rect.Height));
        ValidateCaptureSize(bounds.Width, bounds.Height);
        var result = await engine().CaptureRegionAsync(bounds).ConfigureAwait(false);
        return await DeliverCaptureAsync(result, "MCP.Element", args, settings, orchestrator,
            new JsonObject
            {
                ["window_handle"] = FormatHandle(windowHandle),
                ["element_name"] = element.Current.Name,
                ["automation_id"] = element.Current.AutomationId,
                ["control_type"] = element.Current.ControlType?.ProgrammaticName,
                ["element_bounds"] = RectangleJson(bounds)
            }).ConfigureAwait(false);
    }

    private static async Task<JsonObject> CaptureScrollingAsync(
        JsonElement args,
        SettingsService settings,
        Func<ICaptureEngine> engine,
        CaptureOrchestrator orchestrator)
    {
        var window = ResolveWindow(args);
        var bitmap = await new ScrollingCaptureService(engine()).CaptureScrollingForegroundAsync(window.Handle)
            .ConfigureAwait(false);
        if (bitmap is null)
            throw new InvalidOperationException("The target window did not expose a usable UIA scroll provider.");

        var result = new Snapture.Capture.CaptureResult(
            bitmap,
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            DateTime.UtcNow,
            "MCP.Scrolling",
            window.Handle);
        return await DeliverCaptureAsync(result, "MCP.Scrolling", args, settings, orchestrator,
            new JsonObject
            {
                ["window_handle"] = FormatHandle(window.Handle),
                ["window_title"] = window.Title,
                ["window_class"] = window.ClassName,
                ["process_id"] = window.ProcessId
            }).ConfigureAwait(false);
    }

    private static async Task<JsonObject> DeliverCaptureAsync(
        Snapture.Capture.CaptureResult result,
        string source,
        JsonElement args,
        SettingsService settings,
        CaptureOrchestrator orchestrator,
        JsonObject? extra = null)
    {
        bool includeImage = OptionalBool(args, "include_image");
        string? outputPath = OptionalString(args, "output_path");
        if (outputPath is not null)
            outputPath = ValidateOutputPath(outputPath, settings);

        int width = result.Bitmap.Width;
        int height = result.Bitmap.Height;
        DateTime capturedAt = result.CapturedAtUtc;
        try
        {
            var delivery = await orchestrator.DeliverCaptureForCliAsync(
                result,
                outputPath,
                copyToClipboard: false).ConfigureAwait(false);
            if (delivery.SavedPath is null)
                throw new IOException("Snapture could not save the MCP capture.");

            var metadata = new JsonObject
            {
                ["source"] = source,
                ["path"] = delivery.SavedPath,
                ["width"] = width,
                ["height"] = height,
                ["captured_at_utc"] = capturedAt.ToString("O"),
                ["image_available"] = true,
                ["include_image"] = includeImage
            };
            if (delivery.LanUrl is not null) metadata["lan_url"] = delivery.LanUrl;
            if (extra is not null)
                foreach (var item in extra) metadata[item.Key] = item.Value?.DeepClone();

            return await ResultWithOptionalImageAsync(metadata, delivery.SavedPath, includeImage).ConfigureAwait(false);
        }
        finally
        {
            result.Bitmap.Dispose();
        }
    }

    private static async Task<JsonObject> OcrImageAsync(
        JsonElement args,
        SettingsService settings,
        CaptureHistoryService history)
    {
        string path = ResolveImagePath(args, settings, history);
        string? language = OptionalString(args, "language");
        bool includeImage = OptionalBool(args, "include_image");
        using var bitmap = SKBitmap.Decode(path)
            ?? throw new InvalidDataException("The selected file is not a readable image.");
        var result = await OcrService.RecognizeAsync(bitmap, language).ConfigureAwait(false);
        if (result is null)
            throw new InvalidOperationException("No local OCR engine returned a result.");

        var metadata = new JsonObject
        {
            ["path"] = path,
            ["engine"] = result.Engine.ToString(),
            ["text"] = result.Text,
            ["line_count"] = result.Lines.Count,
            ["lines"] = LinesJson(result.Lines),
            ["include_image"] = includeImage
        };
        return await ResultWithOptionalImageAsync(metadata, path, includeImage).ConfigureAwait(false);
    }

    private static JsonObject HistorySearch(JsonElement args, CaptureHistoryService history)
    {
        string query = OptionalString(args, "query") ?? string.Empty;
        int limit = Math.Clamp(OptionalInt(args, "limit") ?? 20, 1, 100);
        bool? verified = args.TryGetProperty("verified_redacted", out var verifiedElement)
            && verifiedElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? verifiedElement.GetBoolean()
            : null;
        var entries = history.Search(query, limit: verified is null ? limit : 5000)
            .Where(entry => verified is null || entry.VerifiedRedacted == verified.Value)
            .Take(limit)
            .ToList();
        var metadata = new JsonObject
        {
            ["query"] = query,
            ["count"] = entries.Count,
            ["results"] = EntriesJson(entries)
        };
        return Success(metadata);
    }

    private static async Task<JsonObject> AutoRedactAsync(
        JsonElement args,
        SettingsService settings,
        CaptureHistoryService history)
    {
        string path = ResolveImagePath(args, settings, history);
        string outputPath = OptionalString(args, "output_path") is { } requested
            ? ValidateOutputPath(requested, settings)
            : CreateRedactedPath(path, settings);
        if (!outputPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            outputPath = Path.ChangeExtension(outputPath, ".png");

        using var source = SKBitmap.Decode(path)
            ?? throw new InvalidDataException("The selected file is not a readable image.");
        var findings = await AutoRedactor.ScanAsync(source, new HashSet<string>(settings.Current.DisabledRedactRules, StringComparer.OrdinalIgnoreCase))
            .ConfigureAwait(false);
        using var redacted = source.Copy();
        using (var canvas = new SKCanvas(redacted))
        using (var paint = new SKPaint { Color = new SKColor(0x11, 0x11, 0x11), Style = SKPaintStyle.Fill, IsAntialias = false })
        {
            foreach (var finding in findings)
            {
                var box = ClampRect(finding.Box, redacted.Width, redacted.Height);
                if (!box.IsEmpty) canvas.DrawRect(box, paint);
            }
            canvas.Flush();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        using (var image = SKImage.FromBitmap(redacted))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            if (data.Size > MaxImageBytes) throw new InvalidDataException("The redacted PNG exceeds the MCP image size limit.");
            using var output = File.Create(outputPath);
            data.SaveTo(output);
        }

        long historyId = history.Add(
            outputPath,
            "MCP.AutoRedact",
            null,
            null,
            redacted.Width,
            redacted.Height,
            ocrText: null,
            verifiedRedacted: true);
        bool includeImage = OptionalBool(args, "include_image");
        var metadata = new JsonObject
        {
            ["source_path"] = path,
            ["redacted_path"] = outputPath,
            ["history_id"] = historyId,
            ["finding_count"] = findings.Count,
            ["findings"] = FindingsJson(findings),
            ["verified_redacted"] = true,
            ["include_image"] = includeImage
        };
        return await ResultWithOptionalImageAsync(metadata, outputPath, includeImage).ConfigureAwait(false);
    }

    private static JsonObject ListWindows()
    {
        var windows = new JsonArray();
        foreach (var window in WindowEnumerator.EnumerateTopLevel().OrderBy(w => w.Title, StringComparer.OrdinalIgnoreCase))
        {
            windows.Add(new JsonObject
            {
                ["window_handle"] = FormatHandle(window.Handle),
                ["title"] = window.Title,
                ["class_name"] = window.ClassName,
                ["process_id"] = window.ProcessId,
                ["bounds"] = RectangleJson(window.Bounds)
            });
        }
        return Success(new JsonObject { ["count"] = windows.Count, ["windows"] = windows });
    }

    private static JsonObject ListMonitors()
    {
        var monitors = new JsonArray();
        int index = 0;
        foreach (var monitor in MonitorEnumerator.Enumerate())
        {
            monitors.Add(new JsonObject
            {
                ["monitor_index"] = index++,
                ["device_name"] = monitor.DeviceName,
                ["is_primary"] = monitor.IsPrimary,
                ["dpi_x"] = monitor.DpiX,
                ["dpi_y"] = monitor.DpiY,
                ["bounds"] = RectangleJson(monitor.Bounds),
                ["work_area"] = RectangleJson(monitor.WorkArea)
            });
        }
        return Success(new JsonObject { ["count"] = monitors.Count, ["monitors"] = monitors });
    }

    private static JsonObject Success(JsonObject metadata) => new()
    {
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = metadata.ToJsonString()
            }
        },
        ["structuredContent"] = metadata,
        ["isError"] = false
    };

    private static async Task<JsonObject> ResultWithOptionalImageAsync(
        JsonObject metadata,
        string path,
        bool includeImage)
    {
        if (!includeImage)
            return Success(metadata);

        using var image = SKBitmap.Decode(path)
            ?? throw new InvalidDataException("The selected image could not be encoded for MCP.");
        using var encodedImage = SKImage.FromBitmap(image).Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidDataException("The selected image could not be encoded for MCP.");
        if (encodedImage.Size > MaxImageBytes)
            throw new InvalidDataException("The MCP image exceeds the 25 MB response limit.");

        var content = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = metadata.ToJsonString()
            },
            new JsonObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(encodedImage.ToArray()),
                ["mimeType"] = "image/png"
            }
        };
        return new JsonObject
        {
            ["content"] = content,
            ["structuredContent"] = metadata,
            ["isError"] = false
        };
    }

    private static JsonArray LinesJson(IReadOnlyList<OcrLineResult> lines)
    {
        var output = new JsonArray();
        foreach (var line in lines)
        {
            var words = new JsonArray();
            foreach (var word in line.Words)
            {
                var polygon = new JsonArray();
                foreach (var point in word.Polygon)
                    polygon.Add(new JsonObject { ["x"] = point.X, ["y"] = point.Y });
                words.Add(new JsonObject
                {
                    ["text"] = word.Text,
                    ["confidence"] = word.Confidence,
                    ["bounds"] = RectangleJson(word.BoundingBox),
                    ["polygon"] = polygon
                });
            }
            output.Add(new JsonObject
            {
                ["text"] = line.Text,
                ["bounds"] = RectangleJson(line.BoundingBox),
                ["words"] = words
            });
        }
        return output;
    }

    private static JsonArray FindingsJson(IReadOnlyList<RedactionFinding> findings)
    {
        var output = new JsonArray();
        foreach (var finding in findings)
        {
            output.Add(new JsonObject
            {
                ["rule_id"] = finding.RuleId,
                ["description"] = finding.Description,
                ["matched_text"] = finding.MatchedText,
                ["bounds"] = RectangleJson(finding.Box)
            });
        }
        return output;
    }

    private static JsonArray EntriesJson(IReadOnlyList<HistoryEntry> entries)
    {
        var output = new JsonArray();
        foreach (var entry in entries)
        {
            output.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["path"] = entry.FilePath,
                ["captured_at_utc"] = entry.CapturedAtUtc.ToString("O"),
                ["source"] = entry.Source,
                ["source_app"] = entry.SourceApp,
                ["window_title"] = entry.WindowTitle,
                ["width"] = entry.Width,
                ["height"] = entry.Height,
                ["ocr_text"] = entry.OcrText,
                ["dominant_color_hex"] = entry.DominantColorHex,
                ["perceptual_hash"] = entry.PerceptualHash,
                ["project_id"] = entry.ProjectId,
                ["project_name"] = entry.ProjectName,
                ["verified_redacted"] = entry.VerifiedRedacted
            });
        }
        return output;
    }

    private static JsonObject RectangleJson(Rectangle rectangle) => new()
    {
        ["x"] = rectangle.X,
        ["y"] = rectangle.Y,
        ["width"] = rectangle.Width,
        ["height"] = rectangle.Height
    };

    private static JsonObject RectangleJson(SKRect rectangle) => new()
    {
        ["x"] = rectangle.Left,
        ["y"] = rectangle.Top,
        ["width"] = rectangle.Width,
        ["height"] = rectangle.Height
    };

    private static SKRect ClampRect(SKRect rectangle, int width, int height)
    {
        float left = Math.Clamp(rectangle.Left, 0, width);
        float top = Math.Clamp(rectangle.Top, 0, height);
        float right = Math.Clamp(rectangle.Right, 0, width);
        float bottom = Math.Clamp(rectangle.Bottom, 0, height);
        return right <= left || bottom <= top ? SKRect.Empty : new SKRect(left, top, right, bottom);
    }

    private static void ValidateCaptureSize(int width, int height)
    {
        if (width is < 1 or > 16_384 || height is < 1 or > 16_384)
            throw new ArgumentOutOfRangeException(nameof(width), "Capture dimensions must be between 1 and 16384 pixels.");
        if ((long)width * height > 64_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "The requested capture is too large.");
    }

    private static int RequiredInt(JsonElement args, string name) =>
        OptionalInt(args, name)
        ?? throw new ArgumentException($"The '{name}' argument is required and must be an integer.");

    private static int? OptionalInt(JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        throw new ArgumentException($"The '{name}' argument must be an integer.");
    }

    private static bool OptionalBool(JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var value)) return false;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
        throw new ArgumentException($"The '{name}' argument must be a boolean.");
    }

    private static string? OptionalString(JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException($"The '{name}' argument must be a string.");
        string? text = value.GetString()?.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryGetProperty(JsonElement args, string name, out JsonElement value)
    {
        value = default;
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value);
    }

    private static nint RequiredHandle(JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var value))
            throw new ArgumentException($"The '{name}' argument is required.");

        long handle;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out handle))
        {
            // Parsed below.
        }
        else if (value.ValueKind == JsonValueKind.String
            && TryParseHandle(value.GetString(), out handle))
        {
            // Parsed below.
        }
        else
        {
            throw new ArgumentException($"The '{name}' argument must be a valid HWND.");
        }

        if (handle <= 0) throw new ArgumentException($"The '{name}' argument must be a non-zero HWND.");
        return new nint(handle);
    }

    private static bool TryParseHandle(string? text, out long handle)
    {
        handle = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out handle);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out handle);
    }

    private static string FormatHandle(nint handle) =>
        $"0x{handle.ToInt64():X}";

    private static WindowInfo ResolveWindow(JsonElement args)
    {
        nint? requestedHandle = null;
        if (TryGetProperty(args, "window_handle", out _))
            requestedHandle = RequiredHandle(args, "window_handle");
        int? processId = OptionalInt(args, "process_id");
        string? titleContains = OptionalString(args, "title_contains");
        string? className = OptionalString(args, "class_name");
        var windows = WindowEnumerator.EnumerateTopLevel();

        if (requestedHandle is { } handle)
        {
            var exact = windows.FirstOrDefault(window => window.Handle == handle);
            if (exact is null)
                throw new ArgumentException("The supplied window_handle is not a visible capturable window.");
            return exact;
        }

        var matches = windows.Where(window =>
                (!processId.HasValue || window.ProcessId == processId.Value)
                && (titleContains is null || window.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                && (className is null || string.Equals(window.ClassName, className, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (matches.Count == 0)
            throw new ArgumentException("No visible capturable window matched the supplied selector.");
        if (matches.Count > 1 && processId is null && titleContains is null && className is null)
            throw new ArgumentException("The window selector is ambiguous; use window_handle, process_id, title_contains, or class_name.");
        return matches[0];
    }

    private static MonitorInfo ResolveMonitor(JsonElement args)
    {
        var monitors = MonitorEnumerator.Enumerate();
        string? deviceName = OptionalString(args, "device_name");
        int? index = OptionalInt(args, "monitor_index");
        if (deviceName is not null)
        {
            var exact = monitors.FirstOrDefault(monitor =>
                string.Equals(monitor.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
            if (exact is null) throw new ArgumentException($"Monitor '{deviceName}' was not found.");
            return exact;
        }
        if (index is not { } monitorIndex)
            throw new ArgumentException("Specify monitor_index or device_name.");
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            throw new ArgumentOutOfRangeException(nameof(monitorIndex), "The monitor index is out of range.");
        return monitors[monitorIndex];
    }

    private static AutomationElement ResolveElement(nint windowHandle, JsonElement args)
    {
        string? automationId = OptionalString(args, "automation_id");
        string? nameContains = OptionalString(args, "name_contains");
        string? controlType = OptionalString(args, "control_type");
        if (automationId is null && nameContains is null && controlType is null)
            throw new ArgumentException("Specify automation_id, name_contains, or control_type.");

        AutomationElement root;
        try { root = AutomationElement.FromHandle(windowHandle); }
        catch (Exception ex) { throw new InvalidOperationException("UI Automation could not open the target window.", ex); }
        if (root is null) throw new InvalidOperationException("UI Automation returned no root element.");

        var queue = new Queue<AutomationElement>();
        queue.Enqueue(root);
        int budget = 500;
        while (queue.Count > 0 && budget-- > 0)
        {
            var candidate = queue.Dequeue();
            if (ElementMatches(candidate, automationId, nameContains, controlType)) return candidate;
            try
            {
                var children = candidate.FindAll(TreeScope.Children, Condition.TrueCondition);
                foreach (AutomationElement child in children) queue.Enqueue(child);
            }
            catch { }
        }
        throw new ArgumentException("No matching UI Automation element was found in the target window.");
    }

    private static bool ElementMatches(
        AutomationElement element,
        string? automationId,
        string? nameContains,
        string? controlType)
    {
        try
        {
            var current = element.Current;
            if (automationId is not null
                && !string.Equals(current.AutomationId, automationId, StringComparison.Ordinal)) return false;
            if (nameContains is not null
                && !current.Name.Contains(nameContains, StringComparison.OrdinalIgnoreCase)) return false;
            if (controlType is not null)
            {
                string actual = current.ControlType?.ProgrammaticName ?? string.Empty;
                if (actual.StartsWith("ControlType.", StringComparison.OrdinalIgnoreCase)) actual = actual[12..];
                if (!string.Equals(actual, controlType, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static string ResolveImagePath(JsonElement args, SettingsService settings, CaptureHistoryService history)
    {
        long? captureId = OptionalLong(args, "capture_id");
        string? requestedPath = OptionalString(args, "path");
        if (captureId.HasValue == (requestedPath is not null))
            throw new ArgumentException("Specify exactly one of capture_id or path.");

        string path;
        if (captureId is { } id)
        {
            var entry = history.Recent(5000).FirstOrDefault(item => item.Id == id)
                ?? throw new ArgumentException($"Capture id {id} was not found in local history.");
            path = entry.FilePath;
        }
        else
        {
            path = requestedPath!;
        }
        return ValidateReadablePath(path, settings);
    }

    private static long? OptionalLong(JsonElement args, string name)
    {
        if (!TryGetProperty(args, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)) return number;
        if (value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        throw new ArgumentException($"The '{name}' argument must be an integer.");
    }

    private static string ValidateReadablePath(string path, SettingsService settings)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The selected image does not exist.", fullPath);
        string extension = Path.GetExtension(fullPath);
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".webp" and not ".gif")
            throw new ArgumentException("Only supported image files may be used by MCP.");
        if (!IsPathAllowed(fullPath, settings, includeOutputFolder: true))
            throw new UnauthorizedAccessException("The selected image is outside Snapture's permitted local folders.");
        return fullPath;
    }

    private static string ValidateOutputPath(string path, SettingsService settings)
    {
        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!string.IsNullOrEmpty(extension)
            && extension is not ".png" and not ".jpg" and not ".jpeg" and not ".bmp" and not ".webp")
            throw new ArgumentException("MCP output paths must use a supported image extension.");
        if (!IsPathAllowed(fullPath, settings, includeOutputFolder: true))
            throw new UnauthorizedAccessException("The MCP output path is outside Snapture's permitted local folders.");
        return fullPath;
    }

    private static bool IsPathAllowed(string fullPath, SettingsService settings, bool includeOutputFolder)
    {
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        };
        if (includeOutputFolder) roots.Add(settings.Current.OutputFolder);
        roots.Add(CaptureHistoryService.Dir);
        return roots.Where(root => !string.IsNullOrWhiteSpace(root)).Any(root => IsWithin(root, fullPath));
    }

    private static bool IsWithin(string root, string path)
    {
        try
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(fullRoot, fullPath);
            return relative == "."
                || (!Path.IsPathRooted(relative)
                    && !relative.Equals("..", StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));
        }
        catch { return false; }
    }

    private static string CreateRedactedPath(string sourcePath, SettingsService settings)
    {
        string folder = string.IsNullOrWhiteSpace(settings.Current.OutputFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Snapture")
            : settings.Current.OutputFolder;
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        string candidate = Path.Combine(folder, stem + "_redacted.png");
        int suffix = 1;
        while (File.Exists(candidate))
            candidate = Path.Combine(folder, $"{stem}_redacted_{suffix++}.png");
        return ValidateOutputPath(candidate, settings);
    }
}
