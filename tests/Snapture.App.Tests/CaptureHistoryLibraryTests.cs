using System.IO.Compression;
using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CaptureHistoryLibraryTests
{
    [TestMethod]
    public void ExportAndImportRoundTripPreservesCapturesProjectsAndAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-CaptureHistoryLibraryTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var firstPath = CreateImage(root, "first.png", new SKColor(240, 24, 32));
            var secondPath = CreateImage(root, "second.png", new SKColor(32, 80, 160));
            var archivePath = Path.Combine(root, "library.snapture-library");
            var sourceDbPath = Path.Combine(root, "source", "index.db");
            var restoredDbPath = Path.Combine(root, "restored", "index.db");

            using (var source = new CaptureHistoryService(sourceDbPath))
            {
                var firstId = source.Add(firstPath, "Region", "TestApp", "First", 96, 64, "first OCR");
                source.Add(secondPath, "Window", "OtherApp", "Second", 96, 64, null);
                var projectId = source.CreateProject("Release guide");
                source.CreateProject("Empty project");
                source.AssignToProject(new[] { firstId }, projectId);
                source.SetVerifiedRedacted(firstPath, true);

                Assert.AreEqual(archivePath, source.ExportLibrary(archivePath));
            }

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                Assert.IsNotNull(archive.GetEntry("index.db"));
                Assert.IsNotNull(archive.GetEntry("manifest.json"));
                Assert.HasCount(2, archive.Entries.Where(entry => entry.FullName.StartsWith("images/", StringComparison.Ordinal)));
            }

            using var restored = new CaptureHistoryService(restoredDbPath);
            var result = restored.ImportLibrary(archivePath);
            Assert.AreEqual(2, result.ImportedCaptures);
            Assert.AreEqual(0, result.SkippedCaptures);
            Assert.AreEqual(2, result.ImportedProjects);

            var entries = restored.Recent(10);
            Assert.HasCount(2, entries);
            Assert.HasCount(2, restored.Projects());
            Assert.AreEqual(1, entries.Count(entry => entry.ProjectName == "Release guide"));
            Assert.AreEqual(1, entries.Count(entry => entry.OcrText == "first OCR"));
            Assert.AreEqual(1, entries.Count(entry => entry.VerifiedRedacted));
            Assert.IsTrue(entries.All(entry => File.Exists(entry.FilePath)));

            var duplicateResult = restored.ImportLibrary(archivePath);
            Assert.AreEqual(0, duplicateResult.ImportedCaptures);
            Assert.AreEqual(2, duplicateResult.SkippedCaptures);
            Assert.AreEqual(0, duplicateResult.ImportedProjects);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void ImportRejectsUnsafeArchivePathsBeforeReadingDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-CaptureHistoryLibrarySecurityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var archivePath = Path.Combine(root, "unsafe.snapture-library");
            using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
            using (var stream = new StreamWriter(archive.CreateEntry("../outside.txt").Open()))
                stream.Write("not an image");

            using var history = new CaptureHistoryService(Path.Combine(root, "index.db"));
            Assert.Throws<InvalidDataException>(() => history.ImportLibrary(archivePath));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateImage(string root, string name, SKColor color)
    {
        var path = Path.Combine(root, name);
        using var bitmap = new SKBitmap(96, 64);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
        return path;
    }
}
