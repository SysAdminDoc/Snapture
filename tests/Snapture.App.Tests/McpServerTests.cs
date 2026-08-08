using System.Drawing;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Snapture.App.Services;
using Snapture.Capture;

namespace Snapture.App.Tests;

[TestClass]
public sealed class McpServerTests
{
    [TestMethod]
    public void AuthorizationRequiresExactBearerToken()
    {
        const string token = "test-token";

        Assert.IsFalse(McpAuthorization.IsValid(null, token));
        Assert.IsFalse(McpAuthorization.IsValid("Basic test-token", token));
        Assert.IsFalse(McpAuthorization.IsValid("Bearer wrong-token", token));
        Assert.IsFalse(McpAuthorization.IsValid("Bearer test-token extra", token));
        Assert.IsTrue(McpAuthorization.IsValid("Bearer test-token", token));
        Assert.IsTrue(McpAuthorization.IsValid("bearer test-token", token));
    }

    [TestMethod]
    public void AccessTokensAreUrlSafeAndUnique()
    {
        string first = McpAuthorization.CreateToken();
        string second = McpAuthorization.CreateToken();

        Assert.AreEqual(43, first.Length);
        Assert.IsFalse(first.Any(char.IsWhiteSpace));
        Assert.AreEqual(-1, first.IndexOf('+'));
        Assert.AreEqual(-1, first.IndexOf('/'));
        Assert.AreNotEqual(first, second);
    }

    [TestMethod]
    public void OriginPolicyAcceptsOnlyLoopbackHttpOrigins()
    {
        Assert.IsTrue(McpOriginPolicy.IsAllowed(null));
        Assert.IsTrue(McpOriginPolicy.IsAllowed("http://localhost:3000"));
        Assert.IsTrue(McpOriginPolicy.IsAllowed("http://127.0.0.1"));
        Assert.IsTrue(McpOriginPolicy.IsAllowed("http://[::1]:8080"));
        Assert.IsFalse(McpOriginPolicy.IsAllowed("https://localhost:3000"));
        Assert.IsFalse(McpOriginPolicy.IsAllowed("http://localhost.evil.example"));
        Assert.IsFalse(McpOriginPolicy.IsAllowed("http://localhost/path"));
        Assert.IsFalse(McpOriginPolicy.IsAllowed("file://localhost"));
    }

    [TestMethod]
    public void ToolCatalogIsStableAndComplete()
    {
        var names = McpToolCatalog.CreateJson()
            .Select(node => node!["name"]!.GetValue<string>())
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                "auto_redact",
                "capture_element",
                "capture_monitor",
                "capture_region",
                "capture_scrolling",
                "capture_window",
                "history_search",
                "list_monitors",
                "list_windows",
                "ocr_image"
            },
            names);
    }

    [TestMethod]
    public async Task StreamableHttpEndpointInitializesListsToolsAndRejectsForeignOrigins()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture-McpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var settings = new SettingsService();
            settings.Current.OutputFolder = root;
            using var history = new CaptureHistoryService(Path.Combine(root, "history", "index.db"));
            using var engine = new TestCaptureEngine();
            var orchestrator = new CaptureOrchestrator(settings, engine, history);
            using var server = new McpServer(settings, () => engine, orchestrator, history);
            server.Start(GetFreePort());
            string accessToken = server.AccessToken!;

            using var client = new HttpClient();
            string endpoint = server.BaseUrl!;
            using var unauthorized = await PostAsync(client, endpoint, null,
                "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"ping\"}");
            Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

            using var initialize = await PostAsync(client, endpoint, accessToken,
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\"}}");
            Assert.AreEqual(HttpStatusCode.OK, initialize.StatusCode);
            Assert.IsTrue(initialize.Headers.TryGetValues("MCP-Protocol-Version", out var protocolValues));
            CollectionAssert.Contains(protocolValues!.ToArray(), McpServer.ProtocolVersion);
            using (var body = JsonDocument.Parse(await initialize.Content.ReadAsStringAsync()))
            {
                Assert.AreEqual("2.0", body.RootElement.GetProperty("jsonrpc").GetString());
                Assert.AreEqual("2025-11-25",
                    body.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());
            }

            using var tools = await PostAsync(client, endpoint, accessToken,
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
            Assert.AreEqual(HttpStatusCode.OK, tools.StatusCode);
            using (var body = JsonDocument.Parse(await tools.Content.ReadAsStringAsync()))
            {
                Assert.AreEqual(10, body.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength());
            }

            using var foreign = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(
                    "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"ping\"}",
                    Encoding.UTF8,
                    "application/json")
            };
            foreign.Headers.TryAddWithoutValidation("Origin", "https://agent.example");
            foreign.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var foreignResponse = await client.SendAsync(foreign);
            Assert.AreEqual(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string endpoint,
        string? accessToken,
        string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (accessToken is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return await client.SendAsync(request);
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class TestCaptureEngine : ICaptureEngine, IDisposable
    {
        public string Name => "Test";

        public Task<CaptureResult> CaptureRegionAsync(Rectangle virtualRegion, CancellationToken ct = default) => NotImplemented();
        public Task<CaptureResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default) => NotImplemented();
        public Task<CaptureResult> CaptureMonitorAsync(MonitorInfo monitor, CancellationToken ct = default) => NotImplemented();
        public Task<CaptureResult> CaptureVirtualScreenAsync(CancellationToken ct = default) => NotImplemented();

        private static Task<CaptureResult> NotImplemented() =>
            Task.FromException<CaptureResult>(new NotSupportedException());

        public void Dispose() { }
    }
}
