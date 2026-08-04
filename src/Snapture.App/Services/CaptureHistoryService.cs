using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Snapture.App.Services;

/// <summary>One capture as remembered by the history index.</summary>
public sealed record HistoryEntry(
    long Id,
    string FilePath,
    DateTime CapturedAtUtc,
    string Source,
    string? SourceApp,
    string? WindowTitle,
    int Width,
    int Height,
    string? OcrText,
    string? DominantColorHex,
    string? PerceptualHash,
    long? ProjectId,
    string? ProjectName);

public sealed record HistoryProject(
    long Id,
    string Name,
    DateTime CreatedAtUtc);

/// <summary>
/// SQLite-backed history index at <c>%LOCALAPPDATA%\Snapture\history\index.db</c>. Exposes a
/// FTS5 virtual table over OCR text + window title + source app for fast full-text search.
/// </summary>
public sealed partial class CaptureHistoryService : IDisposable
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snapture", "history");
    public static string DbPath { get; } = Path.Combine(Dir, "index.db");

    private readonly SqliteConnection _conn;
    private readonly string _dbPath;
    private bool _disposed;

    public CaptureHistoryService() : this(DbPath)
    {
    }

    internal CaptureHistoryService(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        _dbPath = Path.GetFullPath(dbPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        Batteries.EnsureInitialized();
        _conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        _conn.Open();
        Migrate();
    }

    private static class Batteries
    {
        private static int _initialised;
        public static void EnsureInitialized()
        {
            if (Interlocked.Exchange(ref _initialised, 1) == 0)
                SQLitePCL.Batteries_V2.Init();
        }
    }

    private const int CurrentSchemaVersion = 3;

    private void Migrate()
    {
        int version = GetUserVersion();

        if (version < 1)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS captures (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    file_path     TEXT NOT NULL,
    captured_at   TEXT NOT NULL,
    source        TEXT NOT NULL,
    source_app    TEXT,
    window_title  TEXT,
    width         INTEGER NOT NULL,
    height        INTEGER NOT NULL,
    ocr_text      TEXT,
    dominant_color TEXT,
    perceptual_hash TEXT,
    project_id    INTEGER
);
CREATE TABLE IF NOT EXISTS projects (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    name          TEXT NOT NULL COLLATE NOCASE UNIQUE,
    created_at    TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_captures_at ON captures(captured_at DESC);
CREATE INDEX IF NOT EXISTS idx_captures_project ON captures(project_id);
CREATE VIRTUAL TABLE IF NOT EXISTS captures_fts USING fts5(
    source_app, window_title, ocr_text,
    content='captures', content_rowid='id'
);
CREATE TRIGGER IF NOT EXISTS captures_ai AFTER INSERT ON captures BEGIN
    INSERT INTO captures_fts(rowid, source_app, window_title, ocr_text)
    VALUES (new.id, COALESCE(new.source_app,''), COALESCE(new.window_title,''), COALESCE(new.ocr_text,''));
END;
CREATE TRIGGER IF NOT EXISTS captures_ad AFTER DELETE ON captures BEGIN
    INSERT INTO captures_fts(captures_fts, rowid, source_app, window_title, ocr_text)
    VALUES ('delete', old.id, COALESCE(old.source_app,''), COALESCE(old.window_title,''), COALESCE(old.ocr_text,''));
END;
CREATE TRIGGER IF NOT EXISTS captures_au AFTER UPDATE ON captures BEGIN
    INSERT INTO captures_fts(captures_fts, rowid, source_app, window_title, ocr_text)
    VALUES ('delete', old.id, COALESCE(old.source_app,''), COALESCE(old.window_title,''), COALESCE(old.ocr_text,''));
    INSERT INTO captures_fts(rowid, source_app, window_title, ocr_text)
    VALUES (new.id, COALESCE(new.source_app,''), COALESCE(new.window_title,''), COALESCE(new.ocr_text,''));
END;
";
            cmd.ExecuteNonQuery();
        }

        if (version < 2)
        {
            EnsureColumn("dominant_color", "TEXT");
            EnsureColumn("perceptual_hash", "TEXT");
            BackfillFeatures();
        }

        if (version < 3)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS projects (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    name          TEXT NOT NULL COLLATE NOCASE UNIQUE,
    created_at    TEXT NOT NULL
);";
            cmd.ExecuteNonQuery();
            EnsureColumn("project_id", "INTEGER");
            using var index = _conn.CreateCommand();
            index.CommandText = "CREATE INDEX IF NOT EXISTS idx_captures_project ON captures(project_id);";
            index.ExecuteNonQuery();
        }

        SetUserVersion(CurrentSchemaVersion);
    }

    private void EnsureColumn(string name, string type)
    {
        if (HasColumn(name))
            return;

        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE captures ADD COLUMN {name} {type};";
        cmd.ExecuteNonQuery();
    }

    private bool HasColumn(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(captures);";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void BackfillFeatures()
    {
        var pending = new List<(long Id, string FilePath)>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, file_path FROM captures
                                WHERE dominant_color IS NULL OR perceptual_hash IS NULL";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                pending.Add((reader.GetInt64(0), reader.GetString(1)));
        }

        foreach (var item in pending)
        {
            var features = ImageFeatureService.Compute(item.FilePath);
            if (features is null)
                continue;

            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"UPDATE captures
                                SET dominant_color = $dominant_color,
                                    perceptual_hash = $perceptual_hash
                                WHERE id = $id";
            cmd.Parameters.AddWithValue("$dominant_color", features.DominantColorHex);
            cmd.Parameters.AddWithValue("$perceptual_hash", features.PerceptualHash);
            cmd.Parameters.AddWithValue("$id", item.Id);
            cmd.ExecuteNonQuery();
        }
    }

    private int GetUserVersion()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private void SetUserVersion(int version)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = $"PRAGMA user_version = {version};";
        cmd.ExecuteNonQuery();
    }

    public long Add(string filePath, string source, string? sourceApp, string? windowTitle,
                    int width, int height, string? ocrText)
    {
        var features = ImageFeatureService.Compute(filePath);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO captures(file_path, captured_at, source, source_app, window_title, width, height, ocr_text, dominant_color, perceptual_hash)
                            VALUES($file_path, $captured_at, $source, $source_app, $window_title, $width, $height, $ocr_text, $dominant_color, $perceptual_hash);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$file_path", filePath);
        cmd.Parameters.AddWithValue("$captured_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$source_app", (object?)sourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$window_title", (object?)windowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$width", width);
        cmd.Parameters.AddWithValue("$height", height);
        cmd.Parameters.AddWithValue("$ocr_text", (object?)ocrText ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dominant_color", (object?)features?.DominantColorHex ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$perceptual_hash", (object?)features?.PerceptualHash ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<HistoryEntry> Recent(int limit = 60)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT c.id, c.file_path, c.captured_at, c.source, c.source_app, c.window_title, c.width, c.height, c.ocr_text, c.dominant_color, c.perceptual_hash, c.project_id, p.name
                            FROM captures c
                            LEFT JOIN projects p ON p.id = c.project_id
                            ORDER BY c.captured_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
    }

    public IReadOnlyList<HistoryEntry> Search(string query, int limit = 60)
    {
        if (string.IsNullOrWhiteSpace(query)) return Recent(limit);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT c.id, c.file_path, c.captured_at, c.source, c.source_app, c.window_title, c.width, c.height, c.ocr_text, c.dominant_color, c.perceptual_hash, c.project_id, p.name
                            FROM captures c
                            JOIN captures_fts f ON f.rowid = c.id
                            LEFT JOIN projects p ON p.id = c.project_id
                            WHERE captures_fts MATCH $q
                            ORDER BY c.captured_at DESC
                            LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", BuildFtsQuery(query));
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
    }

    public IReadOnlyList<HistoryEntry> SearchByDominantColor(string hex, int maxDistance = 90, int limit = 60)
    {
        if (!ImageFeatureService.TryParseHex(hex, out _))
            return Array.Empty<HistoryEntry>();

        int boundedLimit = Math.Clamp(limit, 1, 5000);
        int boundedDistance = Math.Clamp(maxDistance, 0, 441);
        return Recent(5000)
            .Select(entry => (Entry: entry, Distance: ImageFeatureService.ColorDistance(entry.DominantColorHex, hex)))
            .Where(candidate => candidate.Distance <= boundedDistance)
            .OrderBy(candidate => candidate.Distance)
            .ThenByDescending(candidate => candidate.Entry.CapturedAtUtc)
            .Take(boundedLimit)
            .Select(candidate => candidate.Entry)
                .ToList();
    }

    public IReadOnlyList<HistoryProject> Projects()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT id, name, created_at FROM projects ORDER BY name COLLATE NOCASE";
        var projects = new List<HistoryProject>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            projects.Add(new HistoryProject(
                reader.GetInt64(0),
                reader.GetString(1),
                DateTime.Parse(reader.GetString(2))));
        }

        return projects;
    }

    public long CreateProject(string name)
    {
        var normalized = NormalizeProjectName(name);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO projects(name, created_at)
                            VALUES($name, $created_at)
                            ON CONFLICT(name) DO NOTHING;
                            SELECT id FROM projects WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", normalized);
        cmd.Parameters.AddWithValue("$created_at", DateTime.UtcNow.ToString("O"));
        return (long)(cmd.ExecuteScalar() ?? throw new InvalidOperationException("Could not create history project."));
    }

    public void RenameProject(long id, string name)
    {
        var normalized = NormalizeProjectName(name);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE projects SET name = $name WHERE id = $id";
        cmd.Parameters.AddWithValue("$name", normalized);
        cmd.Parameters.AddWithValue("$id", id);
        if (cmd.ExecuteNonQuery() == 0)
            throw new KeyNotFoundException($"History project {id} was not found.");
    }

    public void DeleteProject(long id)
    {
        using var transaction = _conn.BeginTransaction();
        using (var unassign = _conn.CreateCommand())
        {
            unassign.Transaction = transaction;
            unassign.CommandText = "UPDATE captures SET project_id = NULL WHERE project_id = $id";
            unassign.Parameters.AddWithValue("$id", id);
            unassign.ExecuteNonQuery();
        }

        using (var delete = _conn.CreateCommand())
        {
            delete.Transaction = transaction;
            delete.CommandText = "DELETE FROM projects WHERE id = $id";
            delete.Parameters.AddWithValue("$id", id);
            if (delete.ExecuteNonQuery() == 0)
                throw new KeyNotFoundException($"History project {id} was not found.");
        }

        transaction.Commit();
    }

    public void AssignToProject(IEnumerable<long> captureIds, long? projectId)
    {
        var ids = captureIds.Distinct().ToArray();
        if (ids.Length == 0)
            return;
        if (projectId is not null && !Projects().Any(project => project.Id == projectId.Value))
            throw new KeyNotFoundException($"History project {projectId} was not found.");

        using var transaction = _conn.BeginTransaction();
        foreach (var captureId in ids)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = "UPDATE captures SET project_id = $project_id WHERE id = $capture_id";
            cmd.Parameters.AddWithValue("$project_id", (object?)projectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$capture_id", captureId);
            cmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static string NormalizeProjectName(string name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 80)
            throw new ArgumentException("Project names must be between 1 and 80 characters.", nameof(name));
        return normalized;
    }

    public IReadOnlySet<long> FindNearDuplicateIds(
        IReadOnlyList<HistoryEntry> entries,
        int maxDistance = 6)
    {
        var duplicates = new HashSet<long>();
        int boundedDistance = Math.Clamp(maxDistance, 0, 64);
        for (int i = 0; i < entries.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(entries[i].PerceptualHash))
                continue;

            for (int j = i + 1; j < entries.Count; j++)
            {
                if (ImageFeatureService.IsNearDuplicate(
                    entries[i].PerceptualHash,
                    entries[j].PerceptualHash,
                    boundedDistance)
                    && ImageFeatureService.ColorDistance(
                        entries[i].DominantColorHex,
                        entries[j].DominantColorHex) <= 72)
                {
                    duplicates.Add(entries[i].Id);
                    duplicates.Add(entries[j].Id);
                }
            }
        }

        return duplicates;
    }

    private static string BuildFtsQuery(string user)
    {
        // Quote each token so users don't have to learn FTS5 syntax — typing "API key" matches both.
        var tokens = user.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                         .Select(t => $"\"{t.Replace("\"", "\"\"")}\"");
        return string.Join(" ", tokens);
    }

    public void Delete(long id)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM captures WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public int PurgeOlderThan(int days)
    {
        if (days <= 0) return 0;
        var cutoff = DateTime.UtcNow.AddDays(-days).ToString("O");
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM captures WHERE captured_at < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        return cmd.ExecuteNonQuery();
    }

    public void UpdateOcrText(long id, string text)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE captures SET ocr_text = $t WHERE id = $id";
        cmd.Parameters.AddWithValue("$t", text);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static List<HistoryEntry> Read(SqliteCommand cmd)
    {
        var list = new List<HistoryEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new HistoryEntry(
                r.GetInt64(0),
                r.GetString(1),
                DateTime.Parse(r.GetString(2)),
                r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.GetInt32(6),
                r.GetInt32(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetInt64(11),
                r.IsDBNull(12) ? null : r.GetString(12)));
        }
        return list;
    }

    /// <summary>Resolves the foreground window's process name + title for tagging captures.</summary>
    public static (string? ProcessName, string? WindowTitle) DescribeForeground(nint hwnd)
    {
        if (hwnd == 0) return (null, null);
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            string? procName = null;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                procName = p.ProcessName;
            }
            catch { }
            int len = GetWindowTextLength(hwnd);
            string? title = null;
            if (len > 0)
            {
                var sb = new System.Text.StringBuilder(len + 1);
                _ = GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }
            return (procName, title);
        }
        catch { return (null, null); }
    }

    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint lpdwProcessId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(nint hwnd);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn.Dispose();
    }
}
