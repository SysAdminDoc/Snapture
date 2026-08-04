using Snapture.App.Services;
using System.Runtime.InteropServices;

namespace Snapture.App.Tests;

[TestClass]
public sealed class VelopackUpdateServiceTests
{
    [TestMethod]
    public void UsesArchitectureSpecificStableGithubReleaseFeed()
    {
        Assert.AreEqual(
            "https://github.com/SysAdminDoc/Snapture/releases/latest/download",
            VelopackUpdateService.FeedUrl);
        string expectedArchitecture = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";
        Assert.AreEqual($"{expectedArchitecture}-stable", VelopackUpdateService.Channel);
    }

    [TestMethod]
    public async Task PendingUpdateOperationsAreSafeBeforeVelopackInstall()
    {
        VelopackUpdateService.ResetForTests();

        Assert.IsFalse(await VelopackUpdateService.DownloadPendingAsync());
        Assert.IsFalse(VelopackUpdateService.ApplyPendingAndRestart());
    }
}
