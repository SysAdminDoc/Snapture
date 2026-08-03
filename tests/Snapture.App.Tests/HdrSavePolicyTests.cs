using System.Drawing;
using System.Drawing.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class HdrSavePolicyTests
{
    [TestMethod]
    public void Save_WritesPngJxlAndAvifVariants()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SnaptureHdrSavePolicy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var bitmap = new Bitmap(8, 6, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.FromArgb(255, 90, 150, 220));
            }

            var result = HdrSavePolicy.Save(Path.Combine(directory, "capture"), bitmap, writeJxr: false);

            Assert.IsTrue(File.Exists(result.PngPath));
            Assert.IsNotNull(result.JxlPath, "JPEG XL output is required for the HDR policy.");
            Assert.IsNotNull(result.AvifPath, "AVIF output is required for the HDR policy.");
            Assert.IsTrue(File.Exists(result.JxlPath));
            Assert.IsTrue(File.Exists(result.AvifPath));
            Assert.AreEqual(3, result.WrittenCount);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
