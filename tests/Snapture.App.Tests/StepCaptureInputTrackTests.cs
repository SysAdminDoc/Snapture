using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class StepCaptureInputTrackTests
{
    [TestMethod]
    public void FormatTrackPreservesKeyAndClickOrder()
    {
        var track = StepCaptureInputFormatter.FormatTrack(
            new[]
            {
                new StepCaptureKeyStroke("CTRL+S", DateTime.UtcNow),
                new StepCaptureKeyStroke("ENTER", DateTime.UtcNow)
            },
            new[]
            {
                new StepCaptureClick(120, 240, StepCaptureClickButton.Left, DateTime.UtcNow),
                new StepCaptureClick(400, 300, StepCaptureClickButton.Middle, DateTime.UtcNow)
            });

        Assert.AreEqual(
            "Keys: CTRL+S, ENTER · Clicks: left (120, 240), middle (400, 300)",
            track);
    }

    [TestMethod]
    public void FormatTrackReturnsNullWhenNoInputWasCaptured()
    {
        Assert.IsNull(StepCaptureInputFormatter.FormatTrack(Array.Empty<StepCaptureKeyStroke>(), Array.Empty<StepCaptureClick>()));
        Assert.IsNull(StepCaptureInputFormatter.FormatMarkdown(null, null));
        Assert.IsNull(StepCaptureInputFormatter.FormatOfficeCaption(null, null));
    }

    [TestMethod]
    public void ExportMarkdownIncludesInputTrackAlongsideImage()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-StepCaptureInputTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = Path.Combine(root, "source.png");
            File.WriteAllBytes(imagePath, new byte[] { 1, 2, 3 });
            var output = StepCaptureExporter.ExportMarkdown(
                root,
                "Input guide",
                new[]
                {
                    new StepCaptureExporter.StepEntry(
                        1,
                        imagePath,
                        "Save the form",
                        new[] { new StepCaptureKeyStroke("CTRL+S", DateTime.UtcNow) },
                        new[] { new StepCaptureClick(12, 34, StepCaptureClickButton.Left, DateTime.UtcNow) })
                });

            var markdown = File.ReadAllText(output);
            StringAssert.Contains(markdown, "_Input track: Keys: CTRL+S · Clicks: left (12, 34)_");
            StringAssert.Contains(markdown, "![Step 1](images/step_001.png)");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
