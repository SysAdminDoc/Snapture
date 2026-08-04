using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Snapture.App.Services;

public sealed record HistoryLibraryImportResult(
    int ImportedCaptures,
    int SkippedCaptures,
    int ImportedProjects);

public sealed partial class CaptureHistoryService
{
    private const int LibraryFormatVersion = 1;
    private const long MaxManifestBytes = 4 * 1024 * 1024;
    private const long MaxDatabaseBytes = 2L * 1024 * 1024 * 1024;
    private const long MaxAssetBytes = 512L * 1024 * 1024;

    public string ExportLibrary(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (string.Equals(fullOutputPath, _dbPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The library archive cannot overwrite the active history database.", nameof(outputPath));

        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        CheckpointDatabase();

        var staging = Path.Combine(Path.GetTempPath(), "Snapture-history-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            var stagedDatabase = Path.Combine(staging, "index.db");
            var stagedArchive = Path.Combine(staging, "library.snapture-library");
            File.Copy(_dbPath, stagedDatabase, overwrite: true);
            var assets = new List<LibraryAsset>();
            var entries = Recent(int.MaxValue);

            using (var archive = ZipFile.Open(stagedArchive, ZipArchiveMode.Create))
            {
                archive.CreateEntryFromFile(stagedDatabase, "index.db", CompressionLevel.Fastest);
                foreach (var entry in entries)
                {
                    if (!File.Exists(entry.FilePath))
                        continue;

                    var archivePath = $"images/{entry.Id:D12}{NormalizeImageExtension(entry.FilePath)}";
                    archive.CreateEntryFromFile(entry.FilePath, archivePath, CompressionLevel.Fastest);
                    assets.Add(new LibraryAsset(entry.Id, archivePath, entry.FilePath));
                }

                var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
                using var manifestStream = manifestEntry.Open();
                JsonSerializer.Serialize(manifestStream, new LibraryManifest(
                    LibraryFormatVersion,
                    DateTime.UtcNow,
                    CurrentSchemaVersion,
                    assets), JsonOptions);
            }

            File.Copy(stagedArchive, fullOutputPath, overwrite: true);
            return fullOutputPath;
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public HistoryLibraryImportResult ImportLibrary(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        var fullInputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(fullInputPath))
            throw new FileNotFoundException("The Snapture library archive was not found.", fullInputPath);

        var staging = Path.Combine(Path.GetTempPath(), "Snapture-history-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var copiedAssets = new List<string>();
        try
        {
            using var archive = ZipFile.OpenRead(fullInputPath);
            ValidateArchive(archive);
            var manifest = ReadManifest(archive);
            if (manifest.FormatVersion != LibraryFormatVersion)
                throw new InvalidDataException($"Unsupported Snapture library format: {manifest.FormatVersion}.");
            if (manifest.HistorySchemaVersion > CurrentSchemaVersion)
                throw new InvalidDataException("The Snapture library was created by a newer history schema.");

            var manifestAssets = manifest.Assets ?? Array.Empty<LibraryAsset>();
            if (manifestAssets.Any(asset => asset.CaptureId <= 0 || !IsSafeImagePath(asset.ArchivePath)))
                throw new InvalidDataException("The Snapture library contains an unsafe image manifest path.");
            if (manifestAssets.Select(asset => asset.CaptureId).Distinct().Count() != manifestAssets.Count)
                throw new InvalidDataException("The Snapture library contains duplicate image manifest entries.");

            var databaseEntry = archive.GetEntry("index.db")
                ?? throw new InvalidDataException("The Snapture library is missing index.db.");
            if (databaseEntry.Length > MaxDatabaseBytes)
                throw new InvalidDataException("The archived history database is too large.");

            var stagedDatabase = Path.Combine(staging, "index.db");
            using (var source = databaseEntry.Open())
            using (var destination = File.Create(stagedDatabase))
                source.CopyTo(destination);

            using var importedConnection = new SqliteConnection($"Data Source={stagedDatabase};Pooling=False;Mode=ReadOnly");
            importedConnection.Open();
            var projects = ReadImportedProjects(importedConnection);
            var captures = ReadImportedCaptures(importedConnection);
            var assets = manifestAssets
                .ToDictionary(asset => asset.CaptureId);
            var destinationDirectory = Path.Combine(
                Path.GetDirectoryName(_dbPath)!, "library", "images");
            Directory.CreateDirectory(destinationDirectory);

            int importedCaptures = 0;
            int skippedCaptures = 0;
            int importedProjects = 0;
            var projectMap = new Dictionary<long, long>();
            using var transaction = _conn.BeginTransaction();
            try
            {
                foreach (var project in projects.Values.OrderBy(project => project.Id))
                {
                    var mappedProjectId = GetOrCreateProject(project, transaction, out var created);
                    projectMap[project.Id] = mappedProjectId;
                    if (created)
                        importedProjects++;
                }

                foreach (var capture in captures)
                {
                    if (CaptureAlreadyExists(capture, transaction))
                    {
                        skippedCaptures++;
                        continue;
                    }

                    if (!assets.TryGetValue(capture.Id, out var asset)
                        || !IsSafeImagePath(asset.ArchivePath))
                    {
                        skippedCaptures++;
                        continue;
                    }

                    var assetEntry = archive.GetEntry(asset.ArchivePath);
                    if (assetEntry is null || assetEntry.Length > MaxAssetBytes)
                    {
                        skippedCaptures++;
                        continue;
                    }

                    var destinationPath = Path.Combine(
                        destinationDirectory,
                        $"import_{Guid.NewGuid():N}{NormalizeImageExtension(asset.ArchivePath)}");
                    using (var source = assetEntry.Open())
                    using (var destination = File.Create(destinationPath))
                        source.CopyTo(destination);
                    copiedAssets.Add(destinationPath);

                    long? projectId = null;
                    if (capture.ProjectId is { } sourceProjectId
                        && projectMap.TryGetValue(sourceProjectId, out var mappedProjectId))
                    {
                        projectId = mappedProjectId;
                    }

                    InsertImportedCapture(capture, destinationPath, projectId, transaction);
                    importedCaptures++;
                }

                transaction.Commit();
            }
            catch
            {
                foreach (var copiedAsset in copiedAssets)
                    TryDeleteFile(copiedAsset);
                throw;
            }

            return new HistoryLibraryImportResult(importedCaptures, skippedCaptures, importedProjects);
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    private void CheckpointDatabase()
    {
        try
        {
            using var command = _conn.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch { }
    }

    private static LibraryManifest ReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("The Snapture library is missing manifest.json.");
        if (entry.Length > MaxManifestBytes)
            throw new InvalidDataException("The Snapture library manifest is too large.");

        using var stream = entry.Open();
        return JsonSerializer.Deserialize<LibraryManifest>(stream, JsonOptions)
            ?? throw new InvalidDataException("The Snapture library manifest is invalid.");
    }

    private static void ValidateArchive(ZipArchive archive)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!paths.Add(entry.FullName))
                throw new InvalidDataException($"Duplicate path in Snapture library: {entry.FullName}");
            if (entry.FullName is "index.db" or "manifest.json")
                continue;
            if (!IsSafeImagePath(entry.FullName))
                throw new InvalidDataException($"Unsafe path in Snapture library: {entry.FullName}");
        }
    }

    private static bool IsSafeImagePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\'))
            return false;
        var parts = path.Split('/', StringSplitOptions.None);
        return parts.Length == 2
            && string.Equals(parts[0], "images", StringComparison.Ordinal)
            && parts[1].Length > 0
            && parts[1] is not "." and not "..";
    }

    private static Dictionary<long, ImportedProject> ReadImportedProjects(SqliteConnection connection)
    {
        var projects = new Dictionary<long, ImportedProject>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, created_at FROM projects";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            projects[reader.GetInt64(0)] = new ImportedProject(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2));
        }

        return projects;
    }

    private static List<ImportedCapture> ReadImportedCaptures(SqliteConnection connection)
    {
        var captures = new List<ImportedCapture>();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT id, file_path, captured_at, source, source_app, window_title,
                                       width, height, ocr_text, dominant_color, perceptual_hash, project_id
                                FROM captures";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            captures.Add(new ImportedCapture(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11)));
        }

        return captures;
    }

    private bool CaptureAlreadyExists(ImportedCapture capture, SqliteTransaction transaction)
    {
        using var command = _conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"SELECT 1 FROM captures
                                WHERE captured_at = $captured_at
                                  AND source = $source
                                  AND COALESCE(source_app, '') = COALESCE($source_app, '')
                                  AND COALESCE(window_title, '') = COALESCE($window_title, '')
                                  AND width = $width
                                  AND height = $height
                                  AND COALESCE(ocr_text, '') = COALESCE($ocr_text, '')
                                  AND COALESCE(dominant_color, '') = COALESCE($dominant_color, '')
                                  AND COALESCE(perceptual_hash, '') = COALESCE($perceptual_hash, '')
                                LIMIT 1";
        command.Parameters.AddWithValue("$captured_at", capture.CapturedAt);
        command.Parameters.AddWithValue("$source", capture.Source);
        command.Parameters.AddWithValue("$source_app", (object?)capture.SourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$window_title", (object?)capture.WindowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", capture.Width);
        command.Parameters.AddWithValue("$height", capture.Height);
        command.Parameters.AddWithValue("$ocr_text", (object?)capture.OcrText ?? DBNull.Value);
        command.Parameters.AddWithValue("$dominant_color", (object?)capture.DominantColorHex ?? DBNull.Value);
        command.Parameters.AddWithValue("$perceptual_hash", (object?)capture.PerceptualHash ?? DBNull.Value);
        return command.ExecuteScalar() is not null;
    }

    private long GetOrCreateProject(ImportedProject project, SqliteTransaction transaction, out bool created)
    {
        using var insert = _conn.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = @"INSERT INTO projects(name, created_at)
                               VALUES($name, $created_at)
                               ON CONFLICT(name) DO NOTHING";
        insert.Parameters.AddWithValue("$name", project.Name);
        insert.Parameters.AddWithValue("$created_at", project.CreatedAt);
        created = insert.ExecuteNonQuery() > 0;

        using var select = _conn.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT id FROM projects WHERE name = $name";
        select.Parameters.AddWithValue("$name", project.Name);
        return (long)(select.ExecuteScalar() ?? throw new InvalidDataException("Could not import history project."));
    }

    private void InsertImportedCapture(
        ImportedCapture capture,
        string destinationPath,
        long? projectId,
        SqliteTransaction transaction)
    {
        using var command = _conn.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"INSERT INTO captures(
                                    file_path, captured_at, source, source_app, window_title,
                                    width, height, ocr_text, dominant_color, perceptual_hash, project_id)
                                VALUES(
                                    $file_path, $captured_at, $source, $source_app, $window_title,
                                    $width, $height, $ocr_text, $dominant_color, $perceptual_hash, $project_id)";
        command.Parameters.AddWithValue("$file_path", destinationPath);
        command.Parameters.AddWithValue("$captured_at", capture.CapturedAt);
        command.Parameters.AddWithValue("$source", capture.Source);
        command.Parameters.AddWithValue("$source_app", (object?)capture.SourceApp ?? DBNull.Value);
        command.Parameters.AddWithValue("$window_title", (object?)capture.WindowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", capture.Width);
        command.Parameters.AddWithValue("$height", capture.Height);
        command.Parameters.AddWithValue("$ocr_text", (object?)capture.OcrText ?? DBNull.Value);
        command.Parameters.AddWithValue("$dominant_color", (object?)capture.DominantColorHex ?? DBNull.Value);
        command.Parameters.AddWithValue("$perceptual_hash", (object?)capture.PerceptualHash ?? DBNull.Value);
        command.Parameters.AddWithValue("$project_id", (object?)projectId ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private static string NormalizeImageExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp"
            ? extension
            : ".png";
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    private sealed record LibraryManifest(
        int FormatVersion,
        DateTime CreatedAtUtc,
        int HistorySchemaVersion,
        IReadOnlyList<LibraryAsset> Assets);

    private sealed record LibraryAsset(
        long CaptureId,
        string ArchivePath,
        string OriginalPath);

    private sealed record ImportedProject(long Id, string Name, string CreatedAt);

    private sealed record ImportedCapture(
        long Id,
        string FilePath,
        string CapturedAt,
        string Source,
        string? SourceApp,
        string? WindowTitle,
        int Width,
        int Height,
        string? OcrText,
        string? DominantColorHex,
        string? PerceptualHash,
        long? ProjectId);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
