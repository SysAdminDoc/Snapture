using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CapturePresetServiceTests
{
    [TestMethod]
    public void PresetsApplyDeterministicLocalCaptureProfiles()
    {
        var settings = new SnaptureSettings();

        Assert.IsTrue(CapturePresetService.Apply("bug-report", settings));
        Assert.AreEqual("bug-report", settings.ActiveCapturePreset);
        Assert.AreEqual("PNG", settings.OutputFormat);
        Assert.IsTrue(settings.OutputFolder.EndsWith("BugReports", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(settings.IncludeCursor);
        Assert.IsTrue(settings.AutoBorderOnCapture);
        Assert.IsFalse(settings.LanShareEnabled);

        Assert.IsTrue(CapturePresetService.Apply("code-block", settings));
        Assert.AreEqual("code-block", settings.ActiveCapturePreset);
        Assert.IsFalse(settings.IncludeCursor);
        Assert.IsFalse(settings.AutoBorderOnCapture);
        Assert.IsTrue(settings.FilenamePattern.StartsWith("Code_", StringComparison.Ordinal));

        Assert.IsTrue(CapturePresetService.Apply("documentation", settings));
        Assert.AreEqual("documentation", settings.ActiveCapturePreset);
        Assert.IsFalse(settings.CopyToClipboard);
        Assert.IsTrue(settings.OutputFolder.EndsWith("Documentation", StringComparison.OrdinalIgnoreCase));

        Assert.IsTrue(CapturePresetService.Apply("quick-share-lan", settings));
        Assert.AreEqual("quick-share-lan", settings.ActiveCapturePreset);
        Assert.IsTrue(settings.LanShareEnabled);
        Assert.IsTrue(settings.OpenEditorAfterCapture);
        Assert.IsTrue(settings.CopyToClipboard);
    }

    [TestMethod]
    public void CustomAndUnknownKeysDoNotMutateSettings()
    {
        var settings = new SnaptureSettings
        {
            OutputFolder = "custom-folder",
            ActiveCapturePreset = "custom"
        };

        Assert.IsFalse(CapturePresetService.Apply(CapturePresetService.CustomKey, settings));
        Assert.AreEqual("custom-folder", settings.OutputFolder);
        Assert.IsFalse(CapturePresetService.Apply("missing", settings));
        Assert.AreEqual("custom", settings.ActiveCapturePreset);
    }
}
