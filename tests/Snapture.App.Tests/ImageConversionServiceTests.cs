using System.Drawing;
using System.Drawing.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ImageConversionServiceTests
{
    [TestMethod]
    public void ResizesToUniqueOutputAndPreservesRequestedFormat()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.ConversionTests", Guid.NewGuid().ToString("N"));
        string input = Path.Combine(root, "source.png");
        Directory.CreateDirectory(root);

        try
        {
            using (var bitmap = new Bitmap(8, 4))
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.Clear(Color.MediumPurple);
                bitmap.Save(input, ImageFormat.Png);
            }

            var result = ImageConversionService.Convert(input, "jpg", resizePercent: 50);

            Assert.AreEqual("jpg", result.Format);
            Assert.AreEqual(Path.Combine(root, "source_snapture_50pct.jpg"), result.OutputPath);
            Assert.AreEqual(4, result.Width);
            Assert.AreEqual(2, result.Height);
            Assert.IsTrue(File.Exists(result.OutputPath));
            using var written = new Bitmap(result.OutputPath);
            Assert.AreEqual(4, written.Width);
            Assert.AreEqual(2, written.Height);

            Assert.ThrowsExactly<ArgumentException>(() =>
                ImageConversionService.Convert(input, "png", outputPath: Path.Combine(root, "wrong.jpg")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
