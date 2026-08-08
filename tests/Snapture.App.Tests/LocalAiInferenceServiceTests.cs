using System.Net;
using System.Net.Http;
using System.Text.Json;
using Snapture.App.Services;
using SkiaSharp;

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
            CreatePng(2, 2),
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
    public async Task SendRejectsNonVisionModelBeforeCreatingRequest()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("phi-4-mini"));

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.VisionUnsupported, exception.Kind);
    }

    [TestMethod]
    public async Task SendRejectsUnavailableProviderBeforeCreatingRequest()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with { IsAvailable = false };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.ProviderUnavailable, exception.Kind);
    }

    [TestMethod]
    public async Task SendRejectsOversizedImageBeforeBase64Expansion()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with
        {
            Capabilities = LocalAiProviderCapabilities.For(LocalAiProviderKind.LmStudio) with
            {
                Limits = LocalAiProviderLimits.Default with { MaxImageBytes = 8 }
            }
        };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(2, 2)));

        Assert.AreEqual(LocalAiInferenceErrorKind.ImageTooLarge, exception.Kind);
    }

    [TestMethod]
    public async Task SendRejectsDimensionsAboveProviderBudget()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with
        {
            Capabilities = LocalAiProviderCapabilities.For(LocalAiProviderKind.LmStudio) with
            {
                Limits = LocalAiProviderLimits.Default with { MaxImageWidth = 1, MaxImageHeight = 1 }
            }
        };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(2, 2)));

        Assert.AreEqual(LocalAiInferenceErrorKind.ImageDimensionsExceeded, exception.Kind);
    }

    [TestMethod]
    public async Task SendRejectsEncodedRequestAboveProviderBudget()
    {
        using var http = new HttpClient(new ThrowingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with
        {
            Capabilities = LocalAiProviderCapabilities.For(LocalAiProviderKind.LmStudio) with
            {
                Limits = LocalAiProviderLimits.Default with { MaxRequestBytes = 100 }
            }
        };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.RequestTooLarge, exception.Kind);
    }

    [TestMethod]
    public async Task SendClassifiesOversizedResponse()
    {
        var handler = new RecordingHandler(new string('x', 128));
        using var http = new HttpClient(handler);
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with
        {
            Capabilities = LocalAiProviderCapabilities.For(LocalAiProviderKind.LmStudio) with
            {
                Limits = LocalAiProviderLimits.Default with { MaxResponseBytes = 32 }
            }
        };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.ResponseTooLarge, exception.Kind);
    }

    [TestMethod]
    public async Task SendClassifiesInvalidAndTruncatedResponses()
    {
        var handler = new RecordingHandler("{\"choices\":[");
        using var http = new HttpClient(handler);
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl"));

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.InvalidResponse, exception.Kind);
    }

    [TestMethod]
    public async Task SendPreservesProviderErrorClassificationAndMetadata()
    {
        var handler = new RecordingHandler(
            "{\"error\":{\"message\":\"model does not support images\",\"code\":\"vision_disabled\",\"type\":\"invalid_request\"}}",
            HttpStatusCode.BadRequest);
        using var http = new HttpClient(handler);
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl"));

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.VisionUnsupported, exception.Kind);
        Assert.AreEqual(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.AreEqual("vision_disabled", exception.ProviderErrorCode);
        Assert.AreEqual("invalid_request", exception.ProviderErrorType);
    }

    [TestMethod]
    public async Task SendClassifiesCallerCancellation()
    {
        using var http = new HttpClient(new BlockingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl"));
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(25);

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1),
            cancellationToken: cancellation.Token));

        Assert.AreEqual(LocalAiInferenceErrorKind.Cancelled, exception.Kind);
    }

    [TestMethod]
    public async Task SendClassifiesProviderTimeout()
    {
        using var http = new HttpClient(new BlockingHandler());
        var service = new LocalAiInferenceService(http);
        var provider = CreateProvider(new LocalAiModel("qwen2-vl")) with
        {
            Capabilities = LocalAiProviderCapabilities.For(LocalAiProviderKind.LmStudio) with
            {
                Limits = LocalAiProviderLimits.Default with { Timeout = TimeSpan.FromMilliseconds(25) }
            }
        };

        var exception = await Assert.ThrowsAsync<LocalAiInferenceException>(() => service.SendImageAsync(
            new LocalAiModelChoice(provider, provider.Models[0]),
            CreatePng(1, 1)));

        Assert.AreEqual(LocalAiInferenceErrorKind.Timeout, exception.Kind);
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
        private readonly HttpStatusCode _statusCode;

        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? ContentType { get; private set; }
        public string Body { get; private set; } = string.Empty;

        public RecordingHandler(string response, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            _response = response;
            _statusCode = statusCode;
        }

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
            return new HttpResponseMessage(_statusCode)
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

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static LocalAiProviderInfo CreateProvider(LocalAiModel model) => new(
        LocalAiProviderKind.LmStudio,
        LocalAiProviderService.LmStudioKey,
        "LM Studio",
        new Uri("http://127.0.0.1:1234/v1/"),
        true,
        new[] { model },
        "Detected · 1 model");

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
