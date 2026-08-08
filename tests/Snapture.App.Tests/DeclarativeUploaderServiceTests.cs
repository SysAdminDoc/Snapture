using System.Net;
using System.Net.Http;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class DeclarativeUploaderServiceTests
{
    [TestMethod]
    public void ImportsShareXCustomUploaderFields()
    {
        const string json = """
        {
          "Version": "17.0.0",
          "Name": "Local host",
          "DestinationType": "ImageUploader",
          "RequestMethod": "POST",
          "RequestURL": "http://127.0.0.1:8787/upload",
          "Parameters": { "private": "true" },
          "Headers": { "X-Token": "test" },
          "Body": "MultipartFormData",
          "Arguments": { "album": "screenshots" },
          "FileFormName": "image",
          "URL": "{json:url}",
          "DeletionURL": "{json:delete_url}"
        }
        """;

        var profile = DeclarativeUploaderService.ImportJson(json, "local.sxcu");

        Assert.AreEqual("Local host", profile.Name);
        Assert.AreEqual("POST", profile.RequestMethod);
        Assert.AreEqual("image", profile.FileFormName);
        Assert.AreEqual("true", profile.Parameters["private"]);
        Assert.AreEqual("{json:url}", profile.UrlTemplate);
    }

    [TestMethod]
    public async Task UploadBuildsMultipartRequestAndResolvesJsonUrl()
    {
        var handler = new RecordingHandler("{\"url\":\"https://files.example.test/capture.png\",\"delete_url\":\"https://files.example.test/delete\"}");
        using var http = new HttpClient(handler);
        var profile = new DeclarativeUploaderProfile
        {
            Name = "Test uploader",
            RequestMethod = "POST",
            RequestUrl = "http://127.0.0.1:8787/upload",
            Parameters = new Dictionary<string, string> { ["private"] = "true" },
            Headers = new Dictionary<string, string> { ["X-Token"] = "abc" },
            Body = DeclarativeUploaderBodyTypes.MultipartFormData,
            Arguments = new Dictionary<string, string> { ["caption"] = "{filename}" },
            FileFormName = "image",
            UrlTemplate = "{json:url}",
            DeletionUrlTemplate = "{json:delete_url}"
        };

        var result = await DeclarativeUploaderService.UploadAsync(
            profile,
            new DeclarativeUploaderRequest(
                new byte[] { 1, 2, 3, 4 },
                "capture.png",
                "Region",
                20,
                10,
                DateTime.UtcNow),
            http);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("https://files.example.test/capture.png", result.Url);
        Assert.AreEqual("https://files.example.test/delete", result.DeletionUrl);
        Assert.IsNotNull(handler.Request);
        Assert.AreEqual("true", ParseQuery(handler.Request!.RequestUri!.Query)["private"]);
        Assert.IsTrue(handler.Request.Headers.TryGetValues("X-Token", out var token));
        CollectionAssert.Contains(token!.ToArray(), "abc");
        StringAssert.Contains(handler.Body, "name=image");
        StringAssert.Contains(handler.Body, "filename=capture.png");
        StringAssert.Contains(handler.Body, "name=caption");
    }

    [TestMethod]
    public void ResolvesJsonArrayAndHeaderResponseTokens()
    {
        string? value = DeclarativeUploaderService.ResolveResponseTemplate(
            "{json:files[0].url}|{header:Location}",
            "{\"files\":[{\"url\":\"https://example.test/a\"}]}",
            responseHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Location"] = "https://example.test/redirect"
            });

        Assert.AreEqual("https://example.test/a|https://example.test/redirect", value);
    }

    [TestMethod]
    public void DestinationPreviewShowsEndpointAndDataWithoutHeaderValues()
    {
        var profile = new DeclarativeUploaderProfile
        {
            Name = "Preview uploader",
            RequestUrl = "http://upload.example.test/capture",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer do-not-show" },
            Body = DeclarativeUploaderBodyTypes.Binary
        };
        var request = new DeclarativeUploaderRequest(
            new byte[2048],
            "capture.png",
            "Editor window",
            80,
            40,
            DateTime.UtcNow);

        string preview = DeclarativeUploaderService.BuildDestinationPreview(profile, request);

        StringAssert.Contains(preview, "http://upload.example.test/capture");
        StringAssert.Contains(preview, "WARNING: unencrypted HTTP");
        StringAssert.Contains(preview, "2.0 KB");
        StringAssert.Contains(preview, "values hidden");
        Assert.IsFalse(preview.Contains("do-not-show", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RejectsEmbeddedUrlCredentials()
    {
        Assert.ThrowsExactly<DeclarativeUploaderException>(() => DeclarativeUploaderService.ValidateProfile(
            new DeclarativeUploaderProfile
            {
                Name = "Embedded credentials",
                RequestUrl = "https://alice:secret@upload.example.test/capture"
            }));
    }

    [TestMethod]
    public async Task TransportExceptionIsReturnedVisibleWithoutLeakingAuthorization()
    {
        var profile = new DeclarativeUploaderProfile
        {
            Name = "Failing uploader",
            RequestUrl = "https://upload.example.test/capture",
            Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret" }
        };

        var result = await DeclarativeUploaderService.UploadAsync(
            profile,
            new DeclarativeUploaderRequest(new byte[] { 1 }, "capture.png", "Editor", 1, 1, DateTime.UtcNow),
            new HttpClient(new ThrowingHandler()));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ErrorMessage!, "HTTP request failed");
        Assert.IsFalse(result.ErrorMessage!.Contains("Bearer secret", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => Uri.UnescapeDataString(pair[0]), pair => Uri.UnescapeDataString(pair.ElementAtOrDefault(1) ?? ""));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _response;

        public HttpRequestMessage? Request { get; private set; }
        public string Body { get; private set; } = string.Empty;

        public RecordingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(_response)
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection failed; Authorization: Bearer secret");
    }
}
