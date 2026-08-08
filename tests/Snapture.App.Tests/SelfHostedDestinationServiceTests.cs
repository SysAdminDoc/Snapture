using System.Net;
using System.Net.Http;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class SelfHostedDestinationServiceTests
{
    [TestMethod]
    public async Task NextcloudUsesWebDavPutAndBasicAuth()
    {
        var handler = new RecordingHandler(_ => "");
        using var http = new HttpClient(handler);
        var settings = new NextcloudDestinationSettings
        {
            Enabled = true,
            ServerUrl = "https://cloud.example.test/",
            Username = "alice",
            RemoteFolder = "Snapture/2026"
        };

        var result = await SelfHostedDestinationService.UploadNextcloudAsync(
            settings,
            "app-password",
            Request(),
            http);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual(HttpMethod.Put, handler.Requests[0].Method);
        Assert.AreEqual("https://cloud.example.test/remote.php/dav/files/alice/Snapture/2026/capture.png", handler.Requests[0].Uri.ToString());
        Assert.AreEqual("Basic YWxpY2U6YXBwLXBhc3N3b3Jk", handler.Requests[0].Headers["Authorization"]);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, handler.Requests[0].BodyBytes);
    }

    [TestMethod]
    public async Task ImmichUploadsAssetAndAddsItToConfiguredAlbum()
    {
        var handler = new RecordingHandler(request =>
            request.Uri.AbsolutePath.EndsWith("/api/assets", StringComparison.Ordinal)
                ? "{\"id\":\"asset-123\"}"
                : "[]");
        using var http = new HttpClient(handler);
        var settings = new ImmichDestinationSettings
        {
            Enabled = true,
            ServerUrl = "http://127.0.0.1:2283",
            AlbumId = "album-456"
        };

        var result = await SelfHostedDestinationService.UploadImmichAsync(
            settings,
            "immich-api-key",
            Request(),
            http);

        Assert.IsTrue(result.Succeeded);
        Assert.HasCount(2, handler.Requests);
        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("immich-api-key", handler.Requests[0].Headers["x-api-key"]);
        StringAssert.Contains(handler.Requests[0].BodyText, "assetData");
        Assert.AreEqual(HttpMethod.Put, handler.Requests[1].Method);
        Assert.AreEqual("http://127.0.0.1:2283/api/albums/album-456/assets", handler.Requests[1].Uri.ToString());
        StringAssert.Contains(handler.Requests[1].BodyText, "asset-123");
        Assert.AreEqual("http://127.0.0.1:2283/api/assets/asset-123/original", result.ResourceUrl);
    }

    [TestMethod]
    public void ConnectorsAreDisabledByDefault()
    {
        var settings = new SnaptureSettings();
        CollectionAssert.AreEqual(Array.Empty<SelfHostedDestinationKind>(), SelfHostedDestinationService.EnabledDestinations(settings).ToArray());
    }

    [TestMethod]
    public void DestinationPreviewShowsRemotePathAndHidesCredential()
    {
        var settings = new SnaptureSettings
        {
            Nextcloud = new NextcloudDestinationSettings
            {
                Enabled = true,
                ServerUrl = "http://cloud.example.test",
                Username = "alice",
                RemoteFolder = "Snapture"
            }
        };

        string preview = SelfHostedDestinationService.BuildDestinationPreview(
            SelfHostedDestinationKind.Nextcloud,
            settings,
            Request());

        StringAssert.Contains(preview, "cloud.example.test/remote.php/dav/files/alice/Snapture/capture.png");
        StringAssert.Contains(preview, "WARNING: unencrypted HTTP");
        StringAssert.Contains(preview, "value hidden");
        Assert.IsFalse(preview.Contains("app-password", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task TransportExceptionIsVisibleWithoutLeakingCredential()
    {
        var settings = new NextcloudDestinationSettings
        {
            Enabled = true,
            ServerUrl = "https://cloud.example.test",
            Username = "alice"
        };

        var result = await SelfHostedDestinationService.UploadNextcloudAsync(
            settings,
            "secret-password",
            Request(),
            new HttpClient(new ThrowingHandler()));

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.ErrorMessage!, "HTTP request");
        Assert.IsFalse(result.ErrorMessage!.Contains("secret-password", StringComparison.Ordinal));
    }

    private static SelfHostedUploadRequest Request() => new(
        new byte[] { 1, 2, 3 },
        "capture.png",
        "Region",
        2,
        1,
        DateTime.UtcNow);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<Snapshot, string> _response;

        public List<Snapshot> Requests { get; } = new();

        public RecordingHandler(Func<Snapshot, string> response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[] body = request.Content is null
                ? Array.Empty<byte>()
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            string bodyText = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var snapshot = new Snapshot(
                request.Method,
                request.RequestUri!,
                request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase),
                body,
                bodyText);
            Requests.Add(snapshot);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response(snapshot))
            };
        }
    }

    private sealed record Snapshot(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        byte[] BodyBytes,
        string BodyText);

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection failed; Authorization: Basic secret-password");
    }
}
