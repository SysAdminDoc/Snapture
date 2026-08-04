using System.IO.Compression;
using System.Security.Cryptography;
using Snapture.App.Editor;

namespace Snapture.App.Tests;

[TestClass]
public sealed class SnapFileFormatSecurityTests
{
    [TestMethod]
    public void RandomAndTruncatedZipBytesAreCleanlyRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.SnapFuzz", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int iteration = 0; iteration < 100; iteration++)
            {
                string path = Path.Combine(root, $"random-{iteration}.snapture");
                byte[] bytes = RandomNumberGenerator.GetBytes(1 + iteration * 7);
                File.WriteAllBytes(path, bytes);

                Exception? error = CaptureLoadError(path);
                Assert.IsNotNull(error);
                Assert.IsInstanceOfType(error, typeof(InvalidDataException));
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void ZipPathTraversalAndUnknownEntriesAreRejectedBeforeDecode()
    {
        string root = Path.Combine(Path.GetTempPath(), "Snapture.SnapFuzz", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(root, "traversal.snapture");
        Directory.CreateDirectory(root);
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("../escape.txt").Open()))
                writer.Write("must never be extracted");

            Exception? error = CaptureLoadError(path);
            Assert.IsNotNull(error);
            Assert.IsInstanceOfType(error, typeof(InvalidDataException));
            Assert.IsFalse(File.Exists(Path.Combine(root, "..", "escape.txt")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static Exception? CaptureLoadError(string path)
    {
        try
        {
            var document = SnapFileFormat.Load(path);
            document.Background.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
