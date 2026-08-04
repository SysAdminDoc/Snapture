using System.Drawing;
using System.Drawing.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ImageCombinerServiceTests
{
    [TestMethod]
    public void CombinesVerticalHorizontalAndGridLayouts()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.CombinerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = CreateImage(root, "first.png", 20, 10, Color.MediumPurple);
            string second = CreateImage(root, "second.png", 10, 30, Color.CadetBlue);
            string third = CreateImage(root, "third.png", 15, 15, Color.Goldenrod);

            var vertical = ImageCombinerService.Combine(
                new[] { first, second, third }, Path.Combine(root, "vertical.png"),
                new ImageCombinerOptions(Gap: 2));
            Assert.AreEqual(20, vertical.Width);
            Assert.AreEqual(59, vertical.Height);

            var horizontal = ImageCombinerService.Combine(
                new[] { first, second }, Path.Combine(root, "horizontal.png"),
                new ImageCombinerOptions(ImageCombineLayout.Horizontal, Gap: 2));
            Assert.AreEqual(32, horizontal.Width);
            Assert.AreEqual(30, horizontal.Height);

            var grid = ImageCombinerService.Combine(
                new[] { first, second, third }, Path.Combine(root, "grid.png"),
                new ImageCombinerOptions(ImageCombineLayout.Grid, Gap: 2, GridColumns: 2));
            Assert.AreEqual(32, grid.Width);
            Assert.AreEqual(47, grid.Height);
            Assert.IsTrue(File.Exists(grid.OutputPath));
            using var written = new Bitmap(grid.OutputPath);
            Assert.AreEqual(grid.Width, written.Width);
            Assert.AreEqual(grid.Height, written.Height);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void RejectsInvalidLayoutOptionsAndOutputCollisions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageCombinerOptions(Gap: -1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageCombinerOptions(GridColumns: 0).Validate());
        Assert.Throws<ArgumentException>(() => new ImageCombinerOptions(OutputFormat: "tga").Validate());

        string root = Path.Combine(Path.GetTempPath(), "Snapture.CombinerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string first = CreateImage(root, "first.png", 4, 4, Color.Black);
            string second = CreateImage(root, "second.png", 4, 4, Color.White);
            Assert.Throws<InvalidOperationException>(() => ImageCombinerService.Combine(
                new[] { first, second }, first, new ImageCombinerOptions()));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string CreateImage(string root, string name, int width, int height, Color color)
    {
        string path = Path.Combine(root, name);
        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(color);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }
}
