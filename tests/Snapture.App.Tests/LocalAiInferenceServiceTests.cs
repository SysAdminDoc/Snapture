using System.Net;
using System.Net.Http;
using System.Text.Json;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class LocalAiInferenceServiceTests
{
    [TestMethod]
    public void RequestPayloadContainsModelPromptAndPngDataUrl()
    {
        var payload = LocalAiInferenceService.BuildRequestPayload(
            "llava:latest",
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            "What is visible?");
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.AreEqual("llava:latest", root.GetProperty("model").GetString());
        var content = root.GetProperty("messages")[0].GetProperty("content");
        Assert.AreEqual("What is visible?", content[0].GetProperty("text").GetString());
        Assert.AreEqual(
            "data:image/png;base64,iVBORw==",
            content[1].GetProperty("image_url").GetProperty("url").GetString());
    }

    [TestMethod]
    public void ResponseParserSupportsStringAndMultipartContent()
    {
        Assert.AreEqual(
            "A window is visible.",
            LocalAiInferenceService.ParseResponseText(
                "{\"choices\":[{\"message\":{\"content\":\"A window is visible.\"}}]}"));

        Assert.AreEqual(
            "First line\nSecond line",
            LocalAiInferenceService.ParseResponseText(
                "{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"First line\"},{\"type\":\"text\",\"text\":\"Second line\"}]}}]}"));
    }

    [TestMethod]
    public void SendPostsOnlyToSelectedLoopbackProvider()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":\"Local answer\"}}]}");
        using var http = new HttpClient(handler);
        var service = new LocalAiInferenceService(http);
        var provider = new LocalAiProviderInfo(
            LocalAiProviderKind.LmStudio,
            LocalAiProviderService.LmStudioKey,
            "LM Studio",
            new Uri("http://127.0.0.1:1234/v1/"),
            true,
            new[] { new LocalAiModel("qwen2-vl") },
            "Detected · 1 model");

        var response = service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            new byte[] { 1, 2, 3 },
            "Inspect it.").GetAwaiter().GetResult();

        Assert.AreEqual("Local answer", response);
        Assert.AreEqual("http://127.0.0.1:1234/v1/chat/completions", handler.RequestUri?.AbsoluteUri);
        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("application/json", handler.ContentType);
        Assert.IsTrue(handler.Body.Contains("qwen2-vl", StringComparison.Ordinal));
        Assert.IsTrue(handler.Body.Contains("Inspect it.", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SendRejectsCloudEndpointBeforeCreatingRequest()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = new LocalAiProviderInfo(
            LocalAiProviderKind.Ollama,
            LocalAiProviderService.OllamaKey,
            "Ollama",
            new Uri("https://api.example.invalid/v1/"),
            true,
            new[] { new LocalAiModel("llava") },
            "Detected · 1 model");

        await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            new byte[] { 1 }));
    }

    [TestMethod]
    public void ErrorParserExtractsLocalApiMessage()
    {
        Assert.AreEqual(
            "model does not support images",
            LocalAiInferenceService.ParseErrorMessage(
                "{\"error\":{\"message\":\"model does not support images\"}}"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = string.Empty;

        public RecordingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The handler must not be reached.");
    }
}
