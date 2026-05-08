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
    string? OcrText);

/// <summary>
/// SQLite-backed history index at <c>%LOCALAPPDATA%\Snapture\history\index.db</c>. Exposes a
/// FTS5 virtual table over OCR text + window title + source app for fast full-text search.
/// </summary>
public sealed class CaptureHistoryService : IDisposable
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Snapture", "history");
    public static string DbPath { get; } = Path.Combine(Dir, "index.db");

    private readonly SqliteConnection _conn;
    private bool _disposed;

    public CaptureHistoryService()
    {
        Directory.CreateDirectory(Dir);
        Batteries.EnsureInitialized();
        _conn = new SqliteConnection($"Data Source={DbPath};Pooling=False");
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

    private void Migrate()
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
    ocr_text      TEXT
);
CREATE INDEX IF NOT EXISTS idx_captures_at ON captures(captured_at DESC);
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

    public long Add(string filePath, string source, string? sourceApp, string? windowTitle,
                    int width, int height, string? ocrText)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO captures(file_path, captured_at, source, source_app, window_title, width, height, ocr_text)
                            VALUES($file_path, $captured_at, $source, $source_app, $window_title, $width, $height, $ocr_text);
                            SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$file_path", filePath);
        cmd.Parameters.AddWithValue("$captured_at", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$source", source);
        cmd.Parameters.AddWithValue("$source_app", (object?)sourceApp ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$window_title", (object?)windowTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$width", width);
        cmd.Parameters.AddWithValue("$height", height);
        cmd.Parameters.AddWithValue("$ocr_text", (object?)ocrText ?? DBNull.Value);
        return (long)(cmd.ExecuteScalar() ?? 0L);
    }

    public IReadOnlyList<HistoryEntry> Recent(int limit = 60)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT id, file_path, captured_at, source, source_app, window_title, width, height, ocr_text
                            FROM captures ORDER BY captured_at DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
    }

    public IReadOnlyList<HistoryEntry> Search(string query, int limit = 60)
    {
        if (string.IsNullOrWhiteSpace(query)) return Recent(limit);
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT c.id, c.file_path, c.captured_at, c.source, c.source_app, c.window_title, c.width, c.height, c.ocr_text
                            FROM captures c
                            JOIN captures_fts f ON f.rowid = c.id
                            WHERE captures_fts MATCH $q
                            ORDER BY c.captured_at DESC
                            LIMIT $limit";
        cmd.Parameters.AddWithValue("$q", BuildFtsQuery(query));
        cmd.Parameters.AddWithValue("$limit", limit);
        return Read(cmd);
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
                r.IsDBNull(8) ? null : r.GetString(8)));
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
