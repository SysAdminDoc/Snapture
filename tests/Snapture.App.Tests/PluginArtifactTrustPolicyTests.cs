using Snapture.App.Services;
using Snapture.Plugin;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginArtifactTrustPolicyTests
{
    [TestMethod]
    public void ExactHashIsApprovedAndHashOrVersionChangesRequireNewTrust()
    {
        var settings = new SnaptureSettings();
        var manifest = Manifest("1.0.0", "a");

        Assert.AreEqual(PluginTrustState.Unapproved, PluginArtifactTrustPolicy.GetState(settings, manifest));
        PluginArtifactTrustPolicy.Approve(settings, manifest);
        Assert.AreEqual(PluginTrustState.Approved, PluginArtifactTrustPolicy.GetState(settings, manifest));
        Assert.AreEqual(
            PluginTrustState.ArtifactChanged,
            PluginArtifactTrustPolicy.GetState(settings, manifest with { Sha256 = new string('b', 64) }));
        Assert.AreEqual(
            PluginTrustState.VersionChanged,
            PluginArtifactTrustPolicy.GetState(settings, manifest with { Version = "1.1.0" }));
    }

    [TestMethod]
    public void CapabilityApprovalRemainsSeparateFromArtifactTrust()
    {
        var settings = new SnaptureSettings();
        var manifest = Manifest("1.0.0", "a") with { Capabilities = PluginCapability.Network };

        PluginArtifactTrustPolicy.Approve(settings, manifest);
        Assert.IsFalse(PluginCapabilityPolicy.IsApproved(settings, manifest));
        PluginCapabilityPolicy.Approve(settings, manifest);
        Assert.IsTrue(PluginCapabilityPolicy.IsApproved(settings, manifest));
        Assert.IsTrue(PluginArtifactTrustPolicy.IsApproved(settings, manifest));
    }

    [TestMethod]
    public void FileHashIsStableAndChangesWhenArtifactChanges()
    {
        string path = Path.Combine(Path.GetTempPath(), "SnapturePluginHash-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            string first = PluginArtifactTrustPolicy.ComputeSha256(path);
            File.WriteAllBytes(path, [1, 2, 4]);

            Assert.AreNotEqual(first, PluginArtifactTrustPolicy.ComputeSha256(path));
            Assert.AreEqual(64, first.Length);
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    private static PluginLoader.PluginManifestInfo Manifest(string version, string hashSeed) =>
        new(
            "Example plugin",
            "Author",
            version,
            "Description",
            PluginCapability.None,
            null,
            null,
            new string(hashSeed[0], 64));
}
