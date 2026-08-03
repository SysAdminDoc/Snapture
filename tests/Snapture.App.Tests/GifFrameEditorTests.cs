using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class GifFrameEditorTests
{
    [TestMethod]
    public void DuplicatePreservesDelayAndAddsFrameAfterSource()
    {
        using var source = CreateBitmap(Color.CornflowerBlue);
        using var editor = new GifFrameEditor(new[] { source }, 140);

        editor.Duplicate(0);

        Assert.AreEqual(2, editor.Count);
        Assert.AreEqual(0, editor.GetInfo(0).Index);
        Assert.AreEqual(1, editor.GetInfo(1).Index);
        Assert.AreEqual(140, editor.GetInfo(1).DelayMs);
    }

    [TestMethod]
    public void DeleteKeepsOneFrameAndRejectsDeletingTheLastFrame()
    {
        using var first = CreateBitmap(Color.CornflowerBlue);
        using var second = CreateBitmap(Color.Goldenrod);
        using var editor = new GifFrameEditor(new[] { first, second }, 100);

        editor.Delete(0);

        Assert.AreEqual(1, editor.Count);
        Assert.Throws<InvalidOperationException>(() => editor.Delete(0));
    }

    [TestMethod]
    public void SetDelayClampsToSafeGifRange()
    {
        using var source = CreateBitmap(Color.CornflowerBlue);
        using var editor = new GifFrameEditor(new[] { source }, 100);

        editor.SetDelay(0, 1);
        Assert.AreEqual(20, editor.GetInfo(0).DelayMs);

        editor.SetDelay(0, 20_000);
        Assert.AreEqual(10_000, editor.GetInfo(0).DelayMs);
    }

    [TestMethod]
    public void ApplyDitherReplacesEditableCopyWithoutMutatingSource()
    {
        using var source = CreateBitmap(Color.FromArgb(127, 127, 127));
        int sourceArgb = source.GetPixel(1, 1).ToArgb();
        using var editor = new GifFrameEditor(new[] { source }, 100);

        editor.ApplyDither(0);
        using var edited = editor.CloneFrame(0);

        Assert.IsTrue(editor.GetInfo(0).IsDithered);
        Assert.AreEqual(sourceArgb, source.GetPixel(1, 1).ToArgb());
        Assert.AreNotEqual(sourceArgb, edited.GetPixel(1, 1).ToArgb());
    }

    [TestMethod]
    public void SaveAsWritesAnimatedGifWithEditedFrames()
    {
        using var first = CreateBitmap(Color.CornflowerBlue);
        using var second = CreateBitmap(Color.Goldenrod);
        using var editor = new GifFrameEditor(new[] { first, second }, 100);
        editor.SetDelay(1, 240);

        string outputPath = Path.Combine(Path.GetTempPath(), $"SnaptureGifEditor_{Guid.NewGuid():N}.gif");
        try
        {
            editor.SaveAs(outputPath);

            Assert.IsTrue(File.Exists(outputPath));
            Assert.IsGreaterThan(0L, new FileInfo(outputPath).Length);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }

    private static Bitmap CreateBitmap(Color color)
    {
        var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.Clear(color);
        return bitmap;
    }
}
