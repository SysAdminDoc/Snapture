using Microsoft.Data.Sqlite;
using SkiaSharp;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class CaptureHistoryFeatureTests
{
    [TestMethod]
    public void AddIndexesFeaturesAndFeatureQueriesUseThem()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-CaptureHistoryFeatureTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var firstPath = CreateImage(root, "first.png", new SKColor(240, 24, 32));
            var secondPath = CreateImage(root, "second.png", new SKColor(238, 22, 30));
            var distinctPath = CreateImage(root, "distinct.png", new SKColor(32, 80, 160));
            var dbPath = Path.Combine(root, "index.db");

            using var history = new CaptureHistoryService(dbPath);
            var firstId = history.Add(firstPath, "Region", "TestApp", "First", 96, 64, null);
            var secondId = history.Add(secondPath, "Region", "TestApp", "Second", 96, 64, null);
            var distinctId = history.Add(distinctPath, "Region", "TestApp", "Distinct", 96, 64, null);
            var entries = history.Recent(10);

            Assert.IsTrue(entries.All(entry =>
                !string.IsNullOrWhiteSpace(entry.DominantColorHex)
                && !string.IsNullOrWhiteSpace(entry.PerceptualHash)));
            Assert.AreEqual(firstId, history.SearchByDominantColor("#F01820", maxDistance: 20, limit: 10)
                .First(entry => entry.Id == firstId).Id);

            var duplicateIds = history.FindNearDuplicateIds(entries);
            Assert.AreEqual(firstId, duplicateIds.First(id => id == firstId));
            Assert.AreEqual(secondId, duplicateIds.First(id => id == secondId));
            Assert.DoesNotContain(distinctId, duplicateIds);

            var projectId = history.CreateProject("Release guide");
            history.AssignToProject(new[] { firstId, secondId }, projectId);
            var assigned = history.Recent(10).Where(entry => entry.ProjectId == projectId).ToArray();
            Assert.HasCount(2, assigned);
            Assert.AreEqual(2, assigned.Count(entry => entry.ProjectName == "Release guide"));
            history.AssignToProject(new[] { firstId }, projectId: null);
            Assert.IsNull(history.Recent(10).Single(entry => entry.Id == firstId).ProjectId);
            history.DeleteProject(projectId);
            Assert.IsEmpty(history.Projects());

            using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            Assert.AreEqual(4L, (long)(command.ExecuteScalar() ?? 0L));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void VersionOneDatabaseGetsFeatureColumnsAndBackfill()
    {
        var root = Path.Combine(Path.GetTempPath(), "Snapture-CaptureHistoryMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var imagePath = CreateImage(root, "legacy.png", new SKColor(240, 24, 32));
            var dbPath = Path.Combine(root, "index.db");
            SQLitePCL.Batteries_V2.Init();
            using (var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = @"
CREATE TABLE captures (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path TEXT NOT NULL,
    captured_at TEXT NOT NULL,
    source TEXT NOT NULL,
    source_app TEXT,
    window_title TEXT,
    width INTEGER NOT NULL,
    height INTEGER NOT NULL,
    ocr_text TEXT
);
INSERT INTO captures(file_path, captured_at, source, source_app, window_title, width, height, ocr_text)
VALUES ($file_path, $captured_at, 'Region', 'LegacyApp', 'Legacy', 96, 64, NULL);
PRAGMA user_version = 1;";
                command.Parameters.AddWithValue("$file_path", imagePath);
                command.Parameters.AddWithValue("$captured_at", DateTime.UtcNow.ToString("O"));
                command.ExecuteNonQuery();
            }

            using var history = new CaptureHistoryService(dbPath);
            var entry = history.Recent(1).Single();
            Assert.IsNotNull(entry.DominantColorHex);
            Assert.IsNotNull(entry.PerceptualHash);
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
