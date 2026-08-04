using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ExternalCommandServiceTests
{
    [TestMethod]
    public void ExpandArgumentsTokenizesQuotedValuesAndReplacesMetadata()
    {
        var capturedAt = new DateTime(2026, 8, 3, 12, 34, 56, DateTimeKind.Utc);
        var args = ExternalCommandService.ExpandArguments(
            "--label \"{source}\" --size {width}x{height} --file {file}",
            @"C:\captures\capture one.png",
            "Foreground window",
            1920,
            1080,
            capturedAt);

        CollectionAssert.AreEqual(
            new[]
            {
                "--label", "Foreground window", "--size", "1920x1080", "--file", @"C:\captures\capture one.png"
            },
            args.ToArray());
    }

    [TestMethod]
    public async Task FileArgumentCommandRunsWithoutShellAndReturnsExitCode()
    {
        string command = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        var profile = new ExternalCommandProfile
        {
            Name = "Exit test",
            ExecutablePath = command,
            Arguments = "/c exit /b 7 {file}",
            InputMode = ExternalCommandInputModes.FileArgument,
            TimeoutSeconds = 10
        };

        var result = await ExternalCommandService.RunAsync(
            profile,
            new ExternalCommandRequest(new byte[] { 0, 1, 2, 3 }, null, "Test", 1, 1, DateTime.UtcNow));

        Assert.AreEqual(7, result.ExitCode);
    }

    [TestMethod]
    public void FileArgumentProfileRequiresFilePlaceholder()
    {
        var profile = new ExternalCommandProfile
        {
            Name = "Missing input",
            ExecutablePath = "tool.exe",
            Arguments = "--verbose",
            InputMode = ExternalCommandInputModes.FileArgument
        };

        Assert.Throws<ArgumentException>(() => ExternalCommandService.ValidateProfile(profile));
    }
}
