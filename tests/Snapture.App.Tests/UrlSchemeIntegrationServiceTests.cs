using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class UrlSchemeIntegrationServiceTests
{
    [TestMethod]
    public void AcceptsCaptureModesAndDestinations()
    {
        Assert.IsTrue(UrlSchemeIntegrationService.TryParse(
            "snapture://capture?mode=fullscreen&dest=file",
            out var request,
            out var error), error);

        Assert.AreEqual(UriCaptureMode.Fullscreen, request!.Mode);
        Assert.IsFalse(request.CopyToClipboardOverride);
        Assert.IsFalse(request.OpenEditorOverride);
    }

    [TestMethod]
    public void RejectsCveStyleFileReferencesBeforeDispatch()
    {
        var maliciousUris = new[]
        {
            @"snapture://capture?filePath=\\attacker\share\secret.png",
            "snapture://capture?filePath=file%3A%2F%2Fattacker%2Fsecret.png",
            "snapture://capture?filePath=C%3A%5CUsers%5C--%5C..%5Csecret.png",
            "snapture://capture?filePath=C%3A%5CWindows%5CSystem32%5Csecret.png",
            "snapture://capture?filePath=smb%3A%2F%2Fattacker%2Fshare%2Fsecret.png"
        };

        foreach (string maliciousUri in maliciousUris)
        {
            Assert.IsFalse(
                UrlSchemeIntegrationService.TryParse(maliciousUri, out _, out var error),
                maliciousUri);
            StringAssert.Contains(error, "rejected", maliciousUri);
        }
    }

    [TestMethod]
    public void BuildsCurrentUserProtocolRegistration()
    {
        var registrations = UrlSchemeIntegrationService.BuildRegistrations(
            @"C:\Program Files\Snapture\Snapture.App.exe");

        Assert.IsTrue(registrations.Any(registration =>
            registration.RelativeKeyPath.Length == 0
            && registration.Values["URL Protocol"] == string.Empty));
        var command = registrations.Single(registration => registration.RelativeKeyPath == "shell\\open\\command");
        Assert.AreEqual(
            "\"C:\\Program Files\\Snapture\\Snapture.App.exe\" --uri \"%1\"",
            command.Values[string.Empty]);
    }
}
