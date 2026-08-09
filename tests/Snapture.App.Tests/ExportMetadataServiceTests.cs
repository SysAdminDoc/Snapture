using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using ImageMagick;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ExportMetadataServiceTests
{
    [TestMethod]
    public void PoliciesStripPreserveReplaceAndRedactionSuppressSourceFields()
    {
        string directory = CreateTempDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            byte[] sourceBytes = CreateSourcePng();
            File.WriteAllBytes(sourcePath, sourceBytes);
            var snapshot = ExportMetadataService.TryReadSource(sourcePath);

            Assert.IsNotNull(snapshot);
            Assert.IsTrue(snapshot.Attributes.ContainsKey("comment"));

            var stripped = ExportMetadataService.Apply(
                sourceBytes,
                MagickFormat.Png,
                new ExportMetadataOptions(ExportMetadataMode.Strip, ExportIccMode.Strip),
                snapshot);
            using (var image = new MagickImage(stripped.Bytes))
            {
                Assert.IsTrue(string.IsNullOrEmpty(image.GetAttribute("comment")));
                Assert.IsFalse(image.ProfileNames.Any(name =>
                    name.Equals("xmp", StringComparison.OrdinalIgnoreCase)));
            }

            var preserved = ExportMetadataService.Apply(
                sourceBytes,
                MagickFormat.Png,
                new ExportMetadataOptions(ExportMetadataMode.PreserveSource, ExportIccMode.Strip),
                snapshot);
            using (var image = new MagickImage(preserved.Bytes))
            {
                Assert.AreEqual("secret-source", image.GetAttribute("comment"));
                Assert.IsTrue(image.ProfileNames.Any(name =>
                    name.Equals("xmp", StringComparison.OrdinalIgnoreCase)));
            }

            var replaced = ExportMetadataService.Apply(
                sourceBytes,
                MagickFormat.Png,
                new ExportMetadataOptions(ExportMetadataMode.ReplaceWithSnapture, ExportIccMode.Strip),
                snapshot);
            using (var image = new MagickImage(replaced.Bytes))
            {
                Assert.AreNotEqual("secret-source", image.GetAttribute("comment"));
                string attributes = string.Join(",", image.AttributeNames);
                Assert.IsTrue(
                    (image.GetAttribute("comment") ?? string.Empty).Contains("Snapture export", StringComparison.Ordinal),
                    $"Replacement comment missing. Attributes: {attributes}");
            }

            var redacted = ExportMetadataService.Apply(
                sourceBytes,
                MagickFormat.Png,
                new ExportMetadataOptions(ExportMetadataMode.PreserveSource, ExportIccMode.Strip),
                snapshot,
                isRedacted: true);
            Assert.IsTrue(redacted.SourceMetadataSuppressed);
            using (var image = new MagickImage(redacted.Bytes))
                Assert.IsTrue(string.IsNullOrEmpty(image.GetAttribute("comment")));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [TestMethod]
    public void DisplayIccAndCompositeStatusAreIndependentFromOrdinaryMetadata()
    {
        byte[] source = CreateSourcePng();
        byte[] srgb = ColorProfiles.SRGB.ToByteArray();
        var options = new ExportMetadataOptions(ExportMetadataMode.Strip, ExportIccMode.EmbedDisplay);

        var embedded = ExportMetadataService.Apply(
            source,
            MagickFormat.Png,
            options,
            displayIccProfile: srgb,
            isComposite: true);
        Assert.IsTrue(embedded.IccEmbedded);
        Assert.IsFalse(embedded.IccUnavailableForComposite);
        Assert.IsTrue(HasPngChunk(embedded.Bytes, "iCCP"), "PNG output did not contain an iCCP chunk.");

        var unavailable = ExportMetadataService.Apply(
            source,
            MagickFormat.Png,
            options,
            isComposite: true);
        Assert.IsFalse(unavailable.IccEmbedded);
        Assert.IsTrue(unavailable.IccUnavailableForComposite);
    }

    [TestMethod]
    public void ApplyRoundTripsPngJpegAndWebp()
    {
        byte[] source = CreateSourcePng();
        foreach (var format in new[] { MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP })
        {
            var result = ExportMetadataService.Apply(
                source,
                format,
                new ExportMetadataOptions(ExportMetadataMode.Strip, ExportIccMode.Strip));
            using var image = new MagickImage(result.Bytes);
            Assert.AreEqual((uint)12, image.Width);
            Assert.AreEqual((uint)8, image.Height);
        }
    }

    [TestMethod]
    public void SidecarIsInspectableAndDoesNotClaimC2paAuthenticity()
    {
        string directory = CreateTempDirectory();
        try
        {
            string sourcePath = Path.Combine(directory, "source.png");
            string outputPath = Path.Combine(directory, "export.png");
            byte[] source = CreateSourcePng();
            File.WriteAllBytes(sourcePath, source);
            var snapshot = ExportMetadataService.TryReadSource(sourcePath);
            Assert.IsNotNull(snapshot);

            var options = new ExportMetadataOptions(
                ExportMetadataMode.Strip,
                ExportIccMode.Strip,
                ExportProvenanceMode.Sidecar);
            var result = ExportMetadataService.Apply(source, MagickFormat.Png, options, snapshot);
            File.WriteAllBytes(outputPath, result.Bytes);
            string? sidecarPath = ExportMetadataService.WriteProvenanceSidecar(
                outputPath,
                result.Bytes,
                MagickFormat.Png,
                options,
                result,
                sourcePath,
                isComposite: false,
                isRedacted: false,
                width: 12,
                height: 8);

            Assert.AreEqual(outputPath + ".provenance.json", sidecarPath);
            using var json = JsonDocument.Parse(File.ReadAllText(sidecarPath!));
            Assert.AreEqual("snapture-export-provenance", json.RootElement.GetProperty("kind").GetString());
            StringAssert.Contains(
                json.RootElement.GetProperty("authenticity").GetString()!,
                "not a C2PA signature");
            Assert.AreEqual("source.png", json.RootElement.GetProperty("source").GetProperty("fileName").GetString());
            Assert.AreEqual("Strip", json.RootElement.GetProperty("policy").GetProperty("metadata").GetString());
            Assert.AreEqual(64, json.RootElement.GetProperty("output").GetProperty("sha256").GetString()!.Length);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static byte[] CreateSourcePng()
    {
        using var bitmap = new Bitmap(12, 8, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(255, 50, 100, 150));
        using var raw = new MemoryStream();
        bitmap.Save(raw, ImageFormat.Png);
        using var image = new MagickImage(raw.ToArray());
        image.SetAttribute("comment", "secret-source");
        image.SetProfile(new ImageProfile("xmp", Encoding.UTF8.GetBytes("<source-secret>")));
        using var output = new MemoryStream();
        image.Write(output, MagickFormat.Png);
        return output.ToArray();
    }

    private static bool HasPngChunk(byte[] png, string chunkType)
    {
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type.Equals(chunkType, StringComparison.Ordinal))
                return true;
            offset = checked(offset + 12 + length);
        }
        return false;
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "SnaptureExportMetadata", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
