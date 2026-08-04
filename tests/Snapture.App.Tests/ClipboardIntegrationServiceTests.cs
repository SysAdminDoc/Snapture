using System.Text.Json;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ClipboardIntegrationServiceTests
{
    [TestMethod]
    public void MarkdownCopyWritesPngAndOnlyUsesTheRelativeLink()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "capture.png");
            var vault = Path.Combine(root, "vault");
            Directory.CreateDirectory(vault);
            File.WriteAllBytes(source, new byte[] { 1, 2, 3, 4 });
            string? clipboard = null;

            var result = ClipboardIntegrationService.TryCopyCaptureAsMarkdown(
                source,
                vault,
                "attachments",
                new ClipboardTargetContext("Obsidian", "Daily - Demo - Obsidian"),
                clipboardWriter: value => clipboard = value);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual("![Snapture capture](attachments/capture.png)", result.Markdown);
            Assert.AreEqual(result.Markdown, clipboard);
            Assert.IsNotNull(result.DestinationPath);
            Assert.IsTrue(File.Exists(result.DestinationPath));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(result.DestinationPath!));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void ObsidianVaultDiscoveryUsesTheActiveVaultTitle()
    {
        var root = CreateTempDirectory();
        try
        {
            var alpha = Path.Combine(root, "Alpha");
            var beta = Path.Combine(root, "Beta");
            Directory.CreateDirectory(alpha);
            Directory.CreateDirectory(beta);
            var config = Path.Combine(root, "obsidian.json");
            File.WriteAllText(config, JsonSerializer.Serialize(new
            {
                vaults = new Dictionary<string, object>
                {
                    ["alpha-id"] = new { path = alpha },
                    ["beta-id"] = new { path = beta }
                }
            }));

            var resolved = ClipboardIntegrationService.ResolveVaultFolder(
                configuredFolder: null,
                new ClipboardTargetContext("Obsidian", "Meeting notes - Beta - Obsidian"),
                config);

            Assert.AreEqual(Path.GetFullPath(beta), resolved);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void AttachmentTraversalIsRejectedBeforeWriting()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "capture.png");
            var vault = Path.Combine(root, "vault");
            Directory.CreateDirectory(vault);
            File.WriteAllBytes(source, new byte[] { 1, 2, 3 });
            string? clipboard = null;

            var result = ClipboardIntegrationService.TryCopyCaptureAsMarkdown(
                source,
                vault,
                @"..\outside",
                clipboardWriter: value => clipboard = value);

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(clipboard);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "outside")));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void MarkdownLinksEscapeUnsafeSegmentsAndAltText()
    {
        var markdown = ClipboardIntegrationService.BuildMarkdown(
            "attachments/Screen shot [1].png",
            "A [capture]");

        Assert.AreEqual("![A \\[capture\\]](attachments/Screen%20shot%20%5B1%5D.png)", markdown);
        Assert.Throws<ArgumentException>(() => ClipboardIntegrationService.BuildMarkdown(@"C:\outside.png"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SnaptureClipboardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
