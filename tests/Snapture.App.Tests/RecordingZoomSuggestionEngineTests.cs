using System.Drawing;
using System.Text.Json;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class RecordingZoomSuggestionEngineTests
{
    [TestMethod]
    public void BuildSuggestions_CreatesClampedCropAroundClick()
    {
        var engine = new RecordingZoomSuggestionEngine();

        engine.AddClick(TimeSpan.FromMilliseconds(200), new Point(16, 20));
        var suggestions = engine.BuildSuggestions(1920, 1080);

        Assert.HasCount(1, suggestions);
        Assert.AreEqual(TimeSpan.Zero, suggestions[0].Start);
        Assert.AreEqual(0f, suggestions[0].Crop.X);
        Assert.AreEqual(0f, suggestions[0].Crop.Y);
        Assert.IsGreaterThan(1.0, suggestions[0].Scale);
    }

    [TestMethod]
    public void BuildSuggestions_MergesNearbyClicks()
    {
        var engine = new RecordingZoomSuggestionEngine();

        engine.AddClick(TimeSpan.FromSeconds(1), new Point(400, 300));
        engine.AddClick(TimeSpan.FromSeconds(2), new Point(430, 315));
        var suggestions = engine.BuildSuggestions(1280, 720);

        Assert.HasCount(1, suggestions);
        Assert.AreEqual(2, suggestions[0].ClickCount);
        Assert.IsGreaterThan(TimeSpan.FromMilliseconds(1_800), suggestions[0].Duration);
    }

    [TestMethod]
    public void BuildSuggestions_UsesNearbyCursorSamplesForTarget()
    {
        var engine = new RecordingZoomSuggestionEngine();

        engine.AddCursorSample(TimeSpan.FromMilliseconds(900), new Point(500, 500));
        engine.AddCursorSample(TimeSpan.FromMilliseconds(980), new Point(520, 520));
        engine.AddClick(TimeSpan.FromSeconds(1), new Point(600, 600));
        var suggestions = engine.BuildSuggestions(1280, 720);

        Assert.HasCount(1, suggestions);
        Assert.IsLessThan(600f, suggestions[0].Center.X);
        Assert.IsLessThan(600f, suggestions[0].Center.Y);
    }

    [TestMethod]
    public void ExportSidecar_WritesJsonWhenSuggestionsExist()
    {
        var engine = new RecordingZoomSuggestionEngine();
        string videoPath = Path.Combine(Path.GetTempPath(), $"snapture-zoom-{Guid.NewGuid():N}.mp4");
        string? sidecar = null;

        try
        {
            engine.AddClick(TimeSpan.FromSeconds(1), new Point(640, 360));

            sidecar = engine.ExportSidecar(videoPath, 1280, 720);

            Assert.IsNotNull(sidecar);
            Assert.IsTrue(File.Exists(sidecar));
            using var doc = JsonDocument.Parse(File.ReadAllText(sidecar));
            Assert.AreEqual(1, doc.RootElement.GetProperty("SchemaVersion").GetInt32());
            Assert.AreEqual(Path.GetFileName(videoPath), doc.RootElement.GetProperty("SourceVideo").GetString());
            Assert.AreEqual(1, doc.RootElement.GetProperty("Suggestions").GetArrayLength());
        }
        finally
        {
            if (sidecar is not null && File.Exists(sidecar))
                File.Delete(sidecar);
        }
    }
}
