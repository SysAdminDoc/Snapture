using System.Drawing;
using System.Drawing.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class BatchProcessServiceTests
{
    [TestMethod]
    public void ProcessesDirectoryWithResizeBorderAndWatermark()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.BatchTests", Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "input");
        string output = Path.Combine(root, "output");
        Directory.CreateDirectory(input);
        try
        {
            using (var bitmap = new Bitmap(20, 10))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.MediumPurple);
                bitmap.Save(Path.Combine(input, "one.png"), ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(input, "ignore.txt"), "not an image");

            var results = BatchProcessService.ProcessDirectory(input, output, new BatchProcessOptions(
                ResizePercent: 50,
                BorderWidth: 2,
                BorderColor: 0xFF000000,
                WatermarkText: "Snapture",
                OutputFormat: "png"));

            Assert.HasCount(1, results);
            Assert.IsTrue(results[0].Succeeded, results[0].Error);
            Assert.IsTrue(File.Exists(results[0].OutputPath));
            using var written = new Bitmap(results[0].OutputPath!);
            Assert.AreEqual(14, written.Width);
            Assert.AreEqual(9, written.Height);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RejectsInvalidOptionsAndMissingInput()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BatchProcessOptions(ResizePercent: 0).Validate());
        Assert.Throws<ArgumentException>(() => new BatchProcessOptions(OutputFormat: "tga").Validate());

        var result = BatchProcessService.ProcessOne(
            Path.Combine(Path.GetTempPath(), "missing-snapture-image.png"),
            Path.GetTempPath(),
            new BatchProcessOptions());
        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains(result.Error!, "does not exist");
    }
}
