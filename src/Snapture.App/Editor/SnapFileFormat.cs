using System.IO;
using System.IO.Compression;
using System.Text;
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
        using var zip = ZipFile.OpenRead(path);

        var bgEntry = zip.GetEntry("background.png")
            ?? throw new InvalidDataException("Missing background.png in .snapture project.");
        SKBitmap background;
        using (var bgStream = bgEntry.Open())
        using (var ms = new MemoryStream())
        {
            bgStream.CopyTo(ms);
            ms.Position = 0;
            background = SKBitmap.Decode(ms)
                ?? throw new InvalidDataException("Could not decode background.png.");
        }

        var doc = new AnnotationDocument(background);

        var docEntry = zip.GetEntry("document.json");
        if (docEntry is not null)
        {
            using var docStream = docEntry.Open();
            using var sr = new StreamReader(docStream, Encoding.UTF8);
            doc.DeserializeShapes(sr.ReadToEnd());
        }
        return doc;
    }
}
