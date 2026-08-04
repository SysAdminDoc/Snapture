using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ShellIntegrationServiceTests
{
    [TestMethod]
    public void BuildsUserScopedOpenAndConversionVerbs()
    {
        string executable = @"C:\Program Files\Snapture\Snapture.App.exe";
        var registrations = ShellIntegrationService.BuildRegistrations(executable);

        StringAssert.Contains(ShellIntegrationService.ImageAssociationPath, "SystemFileAssociations");
        Assert.IsTrue(registrations.Any(registration =>
            registration.RelativeKeyPath == "Snapture"
            && registration.Values["MUIVerb"] == "Snapture"
            && registration.Values.ContainsKey("SubCommands")));

        var commands = registrations
            .Where(registration => registration.RelativeKeyPath.EndsWith("\\command", StringComparison.Ordinal))
            .Select(registration => registration.Values[string.Empty])
            .ToArray();

        Assert.IsTrue(commands.Any(command => command.EndsWith("--open \"%1\"", StringComparison.Ordinal)));
        Assert.IsTrue(commands.Any(command => command.EndsWith("--convert \"%1\" --format png", StringComparison.Ordinal)));
        Assert.IsTrue(commands.Any(command => command.EndsWith("--convert \"%1\" --format jpg", StringComparison.Ordinal)));
        Assert.IsTrue(commands.Any(command => command.EndsWith("--convert \"%1\" --resize 50", StringComparison.Ordinal)));
        Assert.IsTrue(commands.Any(command => command.EndsWith("--convert \"%1\" --resize 200", StringComparison.Ordinal)));
        Assert.IsTrue(commands.All(command => command.StartsWith("\"C:\\Program Files\\Snapture\\Snapture.App.exe\" ", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RejectsExecutablePathsThatCannotBeQuotedSafely()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            ShellIntegrationService.BuildRegistrations(@"C:\Snapture\bad""path.exe"));
    }
}
