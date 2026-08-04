using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>
/// .snapture project file. Plain ZIP container with:
///   - <c>document.json</c> — JSON-serialized list of <see cref="Shape"/>s
///   - <c>background.png</c> — the original (unannotated) capture
///   - <c>manifest.json</c> — metadata: format version, created-at, app version
/// Round-trips losslessly between save and load.
/// </summary>
public static class SnapFileFormat
{
    public const int FormatVersion = 1;
    public const string Extension = ".snapture";
    private const long MaxProjectBytes = 100L * 1024 * 1024;
    private const long MaxEntryBytes = 50L * 1024 * 1024;
    private const long MaxDocumentBytes = 8L * 1024 * 1024;
    private static readonly HashSet<string> AllowedEntries = new(StringComparer.Ordinal)
    {
        "background.png", "document.json", "manifest.json"
    };

    public static void Save(string path, AnnotationDocument doc)
    {
        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        // Background PNG
        using (var bgStream = zip.CreateEntry("background.png", CompressionLevel.Optimal).Open())
        using (var bgImage = SKImage.FromBitmap(doc.Background))
        using (var bgData = bgImage.Encode(SKEncodedImageFormat.Png, 100))
        {
            bgData.SaveTo(bgStream);
        }

        // Document JSON
        var json = doc.SerializeShapes();
        using (var docStream = zip.CreateEntry("document.json", CompressionLevel.Optimal).Open())
        using (var sw = new StreamWriter(docStream, new UTF8Encoding(false)))
        {
            sw.Write(json);
        }

        // Manifest
        var manifest = $"{{\n  \"version\": {FormatVersion},\n  \"app\": \"Snapture\",\n  \"createdAtUtc\": \"{DateTime.UtcNow:O}\"\n}}\n";
        using (var manStream = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open())
        using (var sw = new StreamWriter(manStream, new UTF8Encoding(false)))
        {
            sw.Write(manifest);
        }
    }

    public static AnnotationDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("The .snapture project does not exist.", path);
        if (file.Length > MaxProjectBytes)
            throw new InvalidDataException("The .snapture project exceeds the 100 MB safety limit.");

        using var zip = OpenArchive(path);
        ValidateEntries(zip);

        var bgEntry = zip.GetEntry("background.png")
            ?? throw new InvalidDataException("Missing background.png in .snapture project.");
        SKBitmap background;
        using (var bgStream = bgEntry.Open())
        using (var ms = new MemoryStream())
        {
            CopyBounded(bgStream, ms, MaxEntryBytes, "background.png");
            ms.Position = 0;
            background = SKBitmap.Decode(ms)
                ?? throw new InvalidDataException("Could not decode background.png.");
        }

        if (background.Width > 32_768 || background.Height > 32_768)
        {
            background.Dispose();
            throw new InvalidDataException("The .snapture background dimensions exceed the safety limit.");
        }

        var doc = new AnnotationDocument(background);

        var docEntry = zip.GetEntry("document.json");
        if (docEntry is not null)
        {
            using var docStream = docEntry.Open();
            using var docBuffer = new MemoryStream();
            CopyBounded(docStream, docBuffer, MaxDocumentBytes, "document.json");
            try
            {
                doc.DeserializeShapes(Encoding.UTF8.GetString(docBuffer.ToArray()));
            }
            catch (JsonException ex)
            {
                background.Dispose();
                throw new InvalidDataException("The .snapture document JSON is invalid.", ex);
            }
        }
        return doc;
    }

    private static ZipArchive OpenArchive(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException("The .snapture project could not be opened as a ZIP archive.", ex);
        }
    }

    private static void ValidateEntries(ZipArchive zip)
    {
        if (zip.Entries.Count > AllowedEntries.Count)
            throw new InvalidDataException("The .snapture project contains unsupported entries.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)
                || !AllowedEntries.Contains(entry.FullName)
                || !seen.Add(entry.FullName))
            {
                throw new InvalidDataException("The .snapture project contains an unsafe or duplicate entry.");
            }

            long cap = entry.FullName == "document.json" ? MaxDocumentBytes : MaxEntryBytes;
            if (entry.Length < 0 || entry.Length > cap)
                throw new InvalidDataException($"The .snapture entry '{entry.FullName}' exceeds the safety limit.");
        }
    }

    private static void CopyBounded(Stream source, Stream destination, long maxBytes, string entryName)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
                throw new InvalidDataException($"The .snapture entry '{entryName}' exceeds the safety limit.");
            destination.Write(buffer, 0, read);
        }
    }
}
