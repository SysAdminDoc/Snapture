using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PortableModeTests
{
    [TestMethod]
    public void IniMarkerSupportsEmptyMarkerAndExplicitFalse()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture-PortableTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string marker = Path.Combine(root, PortableMode.IniFileName);
            File.WriteAllText(marker, "[Snapture]\nPortable=true\n");
            Assert.IsTrue(PortableMode.IsPortableIni(marker));

            File.WriteAllText(marker, "; marker\n");
            Assert.IsTrue(PortableMode.IsPortableIni(marker));

            File.WriteAllText(marker, "[Snapture]\nPortable=false\n");
            Assert.IsFalse(PortableMode.IsPortableIni(marker));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void PortableFlagIsAcceptedAlongsideCaptureArguments()
    {
        Assert.IsTrue(CliCommandLine.TryParse(
            new[] { "--portable", "--region", "1,2,300,200", "--out", "capture.png" },
            out var command,
            out var error), error);
        Assert.AreEqual(CliCommandKind.Capture, command.Kind);
        Assert.AreEqual(new System.Drawing.Rectangle(1, 2, 300, 200), command.Capture!.Region);
    }

    [TestMethod]
    public void PortableModeRoutesSettingsAndDefaultCapturesBesideTheExecutable()
    {
        PortableMode.Initialize(new[] { PortableMode.Flag });
        try
        {
            string dataRoot = Path.Combine(PortableMode.ExecutableDirectory, "SnaptureData");
            Assert.IsTrue(PortableMode.IsEnabled);
            Assert.AreEqual(dataRoot, PortableMode.LocalDataDirectory);
            Assert.AreEqual(Path.Combine(dataRoot, "settings.json"), SettingsService.GetFilePath());
            Assert.AreEqual(Path.Combine(dataRoot, "captures"), new SnaptureSettings().OutputFolder);
        }
        finally
        {
            PortableMode.Initialize(Array.Empty<string>());
        }
    }
}
