using System.Drawing;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CliCommandLineTests
{
    [TestMethod]
    public void ParsesRegionCaptureAndOptionalDestinations()
    {
        var ok = CliCommandLine.TryParse(
            new[]
            {
                "--region", "-20,40,800,600",
                "--out", "capture.png",
                "--copy",
                "--hold",
                "--block", "15",
                "--lan-share",
                "--profile", "documentation"
            },
            out var command,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(CliCommandKind.Capture, command.Kind);
        Assert.IsNotNull(command.Capture);
        Assert.AreEqual(new Rectangle(-20, 40, 800, 600), command.Capture.Region);
        Assert.AreEqual("capture.png", command.Capture.OutputPath);
        Assert.IsTrue(command.Capture.CopyToClipboard);
        Assert.IsTrue(command.Capture.Hold);
        Assert.AreEqual(15, command.Capture.BlockSeconds);
        Assert.IsTrue(command.Capture.LanShare);
        Assert.AreEqual("documentation", command.Capture.Profile);
    }

    [TestMethod]
    public void SupportsEqualsSyntaxAndFullscreen()
    {
        var ok = CliCommandLine.TryParse(
            new[] { "--fullscreen", "--out=screen.png", "--engine", "gdi", "--clipboard", "--profile=bug-report" },
            out var command,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.IsTrue(command.Capture!.Fullscreen);
        Assert.IsNull(command.Capture.Region);
        Assert.IsTrue(command.Capture.CopyToClipboard);
        Assert.AreEqual("gdi", command.Capture.Engine);
        Assert.AreEqual("screen.png", command.Capture.OutputPath);
    }

    [TestMethod]
    public void HelpAndVersionAreStandaloneCommands()
    {
        Assert.IsTrue(CliCommandLine.TryParse(new[] { "--help" }, out var help, out var helpError), helpError);
        Assert.AreEqual(CliCommandKind.Help, help.Kind);

        Assert.IsTrue(CliCommandLine.TryParse(new[] { "--version" }, out var version, out var versionError), versionError);
        Assert.AreEqual(CliCommandKind.Version, version.Kind);
    }

    [TestMethod]
    public void ParsesOpenAndConvertCommands()
    {
        Assert.IsTrue(CliCommandLine.TryParse(
            new[] { "--open", @"C:\Screenshots\note.png" },
            out var open,
            out var openError), openError);
        Assert.AreEqual(CliCommandKind.Open, open.Kind);
        Assert.AreEqual(@"C:\Screenshots\note.png", open.Open!.Path);

        Assert.IsTrue(CliCommandLine.TryParse(
            new[] { "--convert", @"C:\Screenshots\note.png", "--format", "jpeg", "--resize", "50", "--out", "converted.jpg" },
            out var convert,
            out var convertError), convertError);
        Assert.AreEqual(CliCommandKind.Convert, convert.Kind);
        Assert.AreEqual(@"C:\Screenshots\note.png", convert.Convert!.InputPath);
        Assert.AreEqual("jpg", convert.Convert.Format);
        Assert.AreEqual(50, convert.Convert.ResizePercent);
        Assert.AreEqual("converted.jpg", convert.Convert.OutputPath);
    }

    [TestMethod]
    public void RejectsMixedOpenAndConversionOptions()
    {
        Assert.IsFalse(CliCommandLine.TryParse(
            new[] { "--open", "capture.png", "--convert", "other.png" },
            out _,
            out var conflictError));
        StringAssert.Contains(conflictError, "either --open or --convert");

        Assert.IsFalse(CliCommandLine.TryParse(
            new[] { "--convert", "capture.png", "--resize", "0" },
            out _,
            out var resizeError));
        StringAssert.Contains(resizeError, "1 to 1000");
    }

    [TestMethod]
    public void ParsesUriCaptureCommand()
    {
        var ok = CliCommandLine.TryParse(
            new[] { "--uri", "snapture://capture?mode=region&autoscroll=true&dest=clipboard" },
            out var command,
            out var error);

        Assert.IsTrue(ok, error);
        Assert.AreEqual(CliCommandKind.Uri, command.Kind);
        Assert.IsNotNull(command.Uri);
        Assert.AreEqual(UriCaptureMode.Scrolling, command.Uri!.Request.Mode);
        Assert.IsTrue(command.Uri.Request.CopyToClipboardOverride);
        Assert.IsFalse(command.Uri.Request.OpenEditorOverride);
    }

    [TestMethod]
    public void ParsesJumpListInteractiveVerbs()
    {
        Assert.IsTrue(CliCommandLine.TryParse(new[] { "--new-region" }, out var region, out var regionError), regionError);
        Assert.AreEqual(CliCommandKind.Interactive, region.Kind);
        Assert.AreEqual(InteractiveCaptureKind.Region, region.Interactive!.CaptureKind);

        Assert.IsTrue(CliCommandLine.TryParse(new[] { "--new-window" }, out var window, out var windowError), windowError);
        Assert.AreEqual(InteractiveCaptureKind.Window, window.Interactive!.CaptureKind);

        Assert.IsFalse(CliCommandLine.TryParse(
            new[] { "--new-fullscreen", "--copy" }, out _, out var mixedError));
        StringAssert.Contains(mixedError, "cannot be combined");
    }

    [TestMethod]
    public void RejectsMissingOrInvalidCaptureSource()
    {
        Assert.IsFalse(CliCommandLine.TryParse(Array.Empty<string>(), out _, out var emptyError));
        StringAssert.Contains(emptyError, "snapture --region");

        Assert.IsFalse(CliCommandLine.TryParse(new[] { "--region", "0,0,0,10" }, out _, out var sizeError));
        StringAssert.Contains(sizeError, "positive width");

        Assert.IsFalse(CliCommandLine.TryParse(new[] { "--region", "0,0,10,10", "--fullscreen" }, out _, out var modeError));
        StringAssert.Contains(modeError, "either --region");
    }

    [TestMethod]
    public void RejectsUnknownAndOutOfRangeOptions()
    {
        Assert.IsFalse(CliCommandLine.TryParse(new[] { "--region", "0,0,10,10", "--wat" }, out _, out var unknownError));
        StringAssert.Contains(unknownError, "Unknown CLI option");

        Assert.IsFalse(CliCommandLine.TryParse(new[] { "--region", "0,0,10,10", "--block", "86401" }, out _, out var blockError));
        StringAssert.Contains(blockError, "1 to 86400");

        Assert.IsFalse(CliCommandLine.TryParse(new[] { "--region", "0,0,10,10", "--engine", "metal" }, out _, out var engineError));
        StringAssert.Contains(engineError, "auto, winrt, or gdi");
    }

    [TestMethod]
    public async Task CliDeliveryWritesExplicitOutputWithoutOpeningEditor()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.CliTests", Guid.NewGuid().ToString("N"));
        string output = Path.Combine(root, "nested", "capture.png");
        Directory.CreateDirectory(root);

        try
        {
            var settings = new SettingsService();
            settings.Current.OutputFolder = root;
            settings.Current.CopyToClipboard = false;
            settings.Current.OpenEditorAfterCapture = true;
            var orchestrator = new CaptureOrchestrator(settings, new UnsupportedCaptureEngine());

            using var bitmap = new Bitmap(4, 3);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.Clear(Color.MediumPurple);

            var delivery = await orchestrator.DeliverCaptureForCliAsync(
                new Snapture.Capture.CaptureResult(
                    bitmap,
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    DateTime.UtcNow,
                    "CLI"),
                output,
                copyToClipboard: false);

            Assert.AreEqual(Path.GetFullPath(output), delivery.SavedPath);
            Assert.IsNull(delivery.LanUrl);
            Assert.IsTrue(File.Exists(output));
            using var written = new Bitmap(output);
            Assert.AreEqual(4, written.Width);
            Assert.AreEqual(3, written.Height);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private sealed class UnsupportedCaptureEngine : Snapture.Capture.ICaptureEngine
    {
        public string Name => "test";
        public Task<Snapture.Capture.CaptureResult> CaptureRegionAsync(Rectangle region, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Snapture.Capture.CaptureResult> CaptureWindowAsync(nint hwnd, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Snapture.Capture.CaptureResult> CaptureMonitorAsync(Snapture.Capture.MonitorInfo monitor, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Snapture.Capture.CaptureResult> CaptureVirtualScreenAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
