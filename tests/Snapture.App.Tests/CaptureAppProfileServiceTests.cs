using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CaptureAppProfileServiceTests
{
    [TestMethod]
    public void FindsProfilesByWindowClassCaseInsensitively()
    {
        var profile = CaptureAppProfileService.Find(
            "chrome_widgetwin_1",
            [new CaptureAppProfile("Chrome_WidgetWin_1", "code-block")]);

        Assert.IsNotNull(profile);
        Assert.AreEqual("Chrome_WidgetWin_1", profile.WindowClassName);
        Assert.AreEqual("code-block", profile.PresetKey);
    }

    [TestMethod]
    public void NormalizeDropsInvalidAndDuplicateEntries()
    {
        var profiles = CaptureAppProfileService.Normalize(
        [
            new CaptureAppProfile("  Notepad  ", "documentation"),
            new CaptureAppProfile("notepad", "bug-report"),
            new CaptureAppProfile("", "code-block"),
            new CaptureAppProfile("Terminal", "custom"),
            new CaptureAppProfile("Unknown", "missing")
        ]);

        Assert.HasCount(1, profiles);
        Assert.AreEqual("Notepad", profiles[0].WindowClassName);
        Assert.AreEqual("bug-report", profiles[0].PresetKey);
    }

    [TestMethod]
    public void ApplyingProfileUsesTheExistingPresetContract()
    {
        var settings = new SnaptureSettings
        {
            PerAppCaptureProfiles =
            [new CaptureAppProfile("Notepad", "documentation")]
        };

        Assert.IsTrue(CaptureAppProfileService.ApplyForClass("notepad", settings));
        Assert.AreEqual("documentation", settings.ActiveCapturePreset);
        Assert.IsTrue(settings.OpenEditorAfterCapture);
        Assert.IsFalse(settings.CopyToClipboard);
    }
}
