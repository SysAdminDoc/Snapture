using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginCapabilityPolicyTests
{
    [TestMethod]
    public void CapabilityManifestRequiresExplicitApprovalAndKeysVersion()
    {
        var settings = new SnaptureSettings();
        var manifest = new PluginLoader.PluginManifestInfo(
            "Example uploader",
            "Author",
            "1.0.0",
            "Uploads captures",
            PluginCapability.Network | PluginCapability.FilesystemWrite,
            null,
            null);

        Assert.IsFalse(PluginCapabilityPolicy.IsApproved(settings, manifest));
        PluginCapabilityPolicy.Approve(settings, manifest);
        Assert.IsTrue(PluginCapabilityPolicy.IsApproved(settings, manifest));

        var updated = manifest with { Version = "1.1.0" };
        Assert.IsFalse(PluginCapabilityPolicy.IsApproved(settings, updated));
    }

    [TestMethod]
    public void CapabilityFreePluginsDoNotNeedAConsentEntry()
    {
        var settings = new SnaptureSettings();
        var manifest = new PluginLoader.PluginManifestInfo(
            "Local processor", "Author", "1.0.0", "", PluginCapability.None, null, null);
        Assert.IsTrue(PluginCapabilityPolicy.IsApproved(settings, manifest));
    }
}
