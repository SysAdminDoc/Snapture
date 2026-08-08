using System.Buffers.Binary;
using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class SafeImageInputTests
{
    [TestMethod]
    public void OpensContentMatchingExtensionAndKeepsValidatedStreamReadable()
    {
        string root = CreateTempDirectory();
        try
        {
            string path = Path.Combine(root, "capture.png");
            File.WriteAllBytes(path, CreatePng(8, 4));

            using var input = SafeImageInput.Open(path);
            Assert.AreEqual(Path.GetFullPath(path), input.Info.FullPath);
            Assert.AreEqual(8, input.Info.Width);
            Assert.AreEqual(4, input.Info.Height);
            Assert.AreEqual(SafeImageFormat.Png, input.Info.Format);
            using var bitmap = SKBitmap.Decode(input.Stream);
            Assert.IsNotNull(bitmap);
            Assert.AreEqual(8, bitmap.Width);
            Assert.AreEqual(4, bitmap.Height);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void RejectsMalformedAndExtensionMismatchedContent()
    {
        string root = CreateTempDirectory();
        try
        {
            string malformed = Path.Combine(root, "malformed.png");
            File.WriteAllBytes(malformed, new byte[] { 1, 2, 3, 4 });
            Assert.Throws<SafeImageInputException>(() => SafeImageInput.ValidateFile(malformed));

            string decoderFailure = Path.Combine(root, "decoder-failure.png");
            File.WriteAllBytes(decoderFailure, CreatePngHeader(2, 2));
            var decoderException = Assert.Throws<SafeImageInputException>(() => SafeImageInput.ValidateFile(decoderFailure));
            StringAssert.Contains(decoderException.Message, "decoder");

            string mismatch = Path.Combine(root, "capture.jpg");
            File.WriteAllBytes(mismatch, CreatePng(8, 4));
            var exception = Assert.Throws<SafeImageInputException>(() => SafeImageInput.ValidateFile(mismatch));
            StringAssert.Contains(exception.Message, "extension");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void RejectsOversizedFileAndPixelBombBeforeExpensiveDecode()
    {
        string root = CreateTempDirectory();
        try
        {
            string oversizedFile = Path.Combine(root, "oversized.png");
            using (var stream = new FileStream(oversizedFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(SafeImageInput.MaxFileBytes + 1);
            var sizeException = Assert.Throws<SafeImageInputException>(() => SafeImageInput.ValidateFile(oversizedFile));
            StringAssert.Contains(sizeException.Message, "safety limit");

            string pixelBomb = Path.Combine(root, "pixel-bomb.png");
            File.WriteAllBytes(pixelBomb, CreatePngHeader((uint)SafeImageInput.MaxDimension + 1, 1));
            var pixelException = Assert.Throws<SafeImageInputException>(() => SafeImageInput.ValidateFile(pixelBomb));
            StringAssert.Contains(pixelException.Message, "dimensions");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void RejectsReparsePointImagePath()
    {
        string root = CreateTempDirectory();
        try
        {
            string source = Path.Combine(root, "source.png");
            string link = Path.Combine(root, "link.png");
            File.WriteAllBytes(source, CreatePng(2, 2));
            try
            {
                File.CreateSymbolicLink(link, source);
            }
            catch (UnauthorizedAccessException)
            {
                Assert.Inconclusive("The test account cannot create a symbolic link.");
                return;
            }
            catch (IOException)
            {
                Assert.Inconclusive("The filesystem does not support symbolic links for this test.");
                return;
            }

            Assert.Throws<UnauthorizedAccessException>(() => SafeImageInput.ValidateFile(link));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(42, 48, 64));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static byte[] CreatePngHeader(uint width, uint height)
    {
        var header = new byte[24];
        header[0] = 0x89;
        header[1] = 0x50;
        header[2] = 0x4e;
        header[3] = 0x47;
        header[4] = 0x0d;
        header[5] = 0x0a;
        header[6] = 0x1a;
        header[7] = 0x0a;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8, 4), 13);
        header[12] = (byte)'I';
        header[13] = (byte)'H';
        header[14] = (byte)'D';
        header[15] = (byte)'R';
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20, 4), height);
        return header;
    }

    private static string CreateTempDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.SafeImageInput", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }
}
