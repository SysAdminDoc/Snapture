using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginDependencyStoreTests
{
    [TestMethod]
    public async Task DownloadsVerifiesCachesAndRedownloadsTamperedDependency()
    {
        string root = Path.Combine(Path.GetTempPath(), "SnaptureDependencyTests", Guid.NewGuid().ToString("N"));
        byte[] payload = Encoding.UTF8.GetBytes("fake ffmpeg tool");
        int downloads = 0;
        using var http = new HttpClient(new Handler(() =>
        {
            downloads++;
            return payload;
        }));
        var store = new PluginDependencyStore(root, "Tools", http);
        var dependency = new PluginDependency(
            "ffmpeg",
            "7.1.1",
            "https://example.test/ffmpeg.exe",
            Convert.ToHexString(SHA256.HashData(payload)),
            "ffmpeg.exe");

        try
        {
            string first = await store.EnsureAsync(dependency);
            CollectionAssert.AreEqual(payload, File.ReadAllBytes(first));
            Assert.AreEqual(1, downloads);
            string cached = await store.EnsureAsync(dependency);
            Assert.AreEqual(first, cached);
            Assert.AreEqual(1, downloads);

            File.WriteAllText(first, "tampered");
            await store.EnsureAsync(dependency);
            Assert.AreEqual(2, downloads);
            CollectionAssert.AreEqual(payload, File.ReadAllBytes(first));
            Assert.IsTrue(store.Remove(dependency));
            Assert.IsFalse(File.Exists(first));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public async Task RejectsUnpinnedOrNonHttpsDependencies()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => new PluginDependencyStore(
            Path.GetTempPath(),
            "Tools").EnsureAsync(new PluginDependency(
                "ffmpeg",
                "7.1.1",
                "http://example.test/ffmpeg.exe",
                new string('a', 64),
                "ffmpeg.exe")));

        await Assert.ThrowsAsync<ArgumentException>(() => new PluginDependencyStore(
            Path.GetTempPath(),
            "Tools").EnsureAsync(new PluginDependency(
                "ffmpeg",
                "7.1.1",
                "https://user:password@example.test/ffmpeg.exe",
                new string('a', 64),
                "ffmpeg.exe")));
    }

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<byte[]> _payload;

        public Handler(Func<byte[]> payload) => _payload = payload;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload())
            });
    }
}
