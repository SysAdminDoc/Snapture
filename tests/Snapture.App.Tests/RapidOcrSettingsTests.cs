using System.Text.Json;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class RapidOcrSettingsTests
{
    [TestMethod]
    public void DirectMlIsOptInAndPersistsWithSettings()
    {
        var settings = new SnaptureSettings();

        Assert.IsFalse(settings.RapidOcrUseDirectMl);
        Assert.AreEqual(string.Empty, settings.OneOcrExecutablePath);

        settings.RapidOcrUseDirectMl = true;
        settings.OneOcrExecutablePath = @"C:\Tools\sponeocr.exe";
        var json = JsonSerializer.Serialize(settings);
        var roundTrip = JsonSerializer.Deserialize<SnaptureSettings>(json);

        Assert.IsNotNull(roundTrip);
        Assert.IsTrue(roundTrip!.RapidOcrUseDirectMl);
        Assert.AreEqual(@"C:\Tools\sponeocr.exe", roundTrip.OneOcrExecutablePath);
    }
}
