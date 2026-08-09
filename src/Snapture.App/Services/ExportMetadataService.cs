using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using System.Drawing;
using System.IO;
using System.Text;
using ImageMagick;
using Snapture.Capture;

namespace Snapture.App.Services;

public enum ExportMetadataMode
{
    Strip,
    PreserveSource,
    ReplaceWithSnapture
}

public enum ExportIccMode
{
    Strip,
    PreserveSource,
    EmbedDisplay
}

public enum ExportProvenanceMode
{
    Disabled,
    Sidecar
}

public sealed record ExportMetadataOptions(
    ExportMetadataMode Metadata = ExportMetadataMode.Strip,
    ExportIccMode Icc = ExportIccMode.EmbedDisplay,
    ExportProvenanceMode Provenance = ExportProvenanceMode.Disabled)
{
    public static ExportMetadataOptions Default { get; } = new();

    public static ExportMetadataOptions FromSettings(SnaptureSettings settings) => new(
        settings.ExportMetadata,
        settings.ExportIcc,
        settings.ExportProvenance);
}

internal sealed record ExportMetadataSnapshot(
    IReadOnlyDictionary<string, byte[]> Profiles,
    IReadOnlyDictionary<string, string> Attributes,
    byte[]? IccProfile)
{
    public bool HasOrdinaryMetadata => Profiles.Count > 0 || Attributes.Count > 0;

    public bool HasAnyMetadata => HasOrdinaryMetadata || IccProfile is not null;
}

internal sealed record ExportMetadataResult(
    byte[] Bytes,
    bool SourceMetadataApplied,
    bool SourceMetadataSuppressed,
    bool IccEmbedded,
    bool IccUnavailableForComposite);

/// <summary>
/// Applies explicit export metadata decisions after the pixel encoder has produced a file.
/// Ordinary metadata, display ICC data, and the optional descriptive provenance sidecar are
/// deliberately independent controls. The sidecar is not a C2PA signature or authenticity claim.
/// </summary>
internal static class ExportMetadataService
{
    private const int MaximumAttributeCount = 64;
    private const int MaximumAttributeCharacters = 64 * 1024;
    private const int MaximumProfileBytes = 16 * 1024 * 1024;

    public static ExportMetadataOptions FromSettings(SnaptureSettings settings) =>
        ExportMetadataOptions.FromSettings(settings);

    public static bool IsComposite(Rectangle bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        try
        {
            int containing = MonitorEnumerator.Enumerate()
                .Count(monitor => monitor.Bounds.Contains(bounds));
            return containing != 1;
        }
        catch
        {
            // An unavailable monitor topology must not claim a single ICC profile.
            return true;
        }
    }

    public static bool TryGetFormat(string path, out MagickFormat format)
    {
        format = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => MagickFormat.Png,
            ".jpg" or ".jpeg" => MagickFormat.Jpeg,
            ".bmp" => MagickFormat.Bmp,
            ".webp" => MagickFormat.WebP,
            ".svg" => MagickFormat.Svg,
            _ => MagickFormat.Unknown
        };
        return format != MagickFormat.Unknown;
    }

    public static byte[]? TryGetDisplayIccProfile(Rectangle bounds, out string? profilePath)
    {
        profilePath = null;
        try
        {
            if (!DisplayColorProfileProbe.TryGetForBounds(bounds, out var profile))
                return null;
            profilePath = profile.ProfilePath;
            return profile.Data;
        }
        catch
        {
            return null;
        }
    }

    public static ExportMetadataSnapshot? TryReadSource(string path)
    {
        try
        {
            using var input = SafeImageInput.Open(path);
            using var image = new MagickImage(input.Stream);
            return ReadFromImage(image);
        }
        catch
        {
            return null;
        }
    }

    public static ExportMetadataResult Apply(
        byte[] encoded,
        MagickFormat format,
        ExportMetadataOptions options,
        ExportMetadataSnapshot? sourceMetadata = null,
        byte[]? displayIccProfile = null,
        bool isComposite = false,
        bool isRedacted = false)
    {
        ArgumentNullException.ThrowIfNull(encoded);
        ArgumentNullException.ThrowIfNull(options);
        if (encoded.Length == 0)
            throw new ArgumentException("Encoded image bytes are required.", nameof(encoded));

        using var image = new MagickImage(encoded);
        bool preserveSource = options.Metadata == ExportMetadataMode.PreserveSource && !isRedacted;
        bool replace = options.Metadata == ExportMetadataMode.ReplaceWithSnapture;
        bool suppressSource = isRedacted && options.Metadata == ExportMetadataMode.PreserveSource;

        if (!preserveSource || replace)
            image.Strip();

        bool sourceApplied = false;
        if (preserveSource && sourceMetadata is not null)
        {
            foreach (var profile in sourceMetadata.Profiles)
            {
                if (profile.Value.Length is 0 or > MaximumProfileBytes)
                    continue;

                try
                {
                    image.SetProfile(new ImageProfile(profile.Key, profile.Value));
                    sourceApplied = true;
                }
                catch
                {
                    // A profile that this format cannot carry is reported by the sidecar
                    // policy rather than turning a successful pixel export into a failure.
                }
            }

            foreach (var attribute in sourceMetadata.Attributes)
            {
                try
                {
                    if (attribute.Key.Equals("comment", StringComparison.OrdinalIgnoreCase))
                        image.Comment = attribute.Value;
                    image.SetAttribute(attribute.Key, attribute.Value);
                    sourceApplied = true;
                }
                catch
                {
                    // Attribute names are format-specific; unsupported names are skipped.
                }
            }
        }

        if (replace)
        {
            image.Comment = "Snapture export; source metadata replaced by the application policy.";
            image.SetAttribute("software", "Snapture");
            image.SetAttribute(
                "comment",
                "Snapture export; source metadata replaced by the application policy.");
        }

        RemoveIccProfiles(image);
        byte[]? iccToEmbed = options.Icc switch
        {
            ExportIccMode.PreserveSource when !isRedacted => sourceMetadata?.IccProfile,
            ExportIccMode.EmbedDisplay => displayIccProfile,
            _ => null
        };

        bool iccApplied = iccToEmbed is { Length: > 0 } && TrySetIccProfile(image, iccToEmbed);

        if (format == MagickFormat.Jpeg)
            image.Quality = 92;
        else if (format == MagickFormat.WebP)
            image.Quality = 88;

        using var output = new MemoryStream();
        image.Write(output, format);
        byte[] outputBytes = output.ToArray();
        var textAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (preserveSource && sourceMetadata is not null)
        {
            foreach (var attribute in sourceMetadata.Attributes)
                textAttributes[attribute.Key] = attribute.Value;
        }
        if (replace)
        {
            textAttributes["comment"] = "Snapture export; source metadata replaced by the application policy.";
            textAttributes["software"] = "Snapture";
        }
        if (format == MagickFormat.Png)
            outputBytes = EmbedPngTextAttributes(outputBytes, textAttributes);

        bool iccEmbedded = false;
        if (iccApplied && format == MagickFormat.Png)
        {
            try
            {
                outputBytes = PngIccProfileEmbedder.Embed(outputBytes, iccToEmbed!);
                iccEmbedded = true;
            }
            catch
            {
                // The output remains usable, but the sidecar must report that ICC was not carried.
            }
        }
        else if (iccApplied)
        {
            try
            {
                using var verification = new MagickImage(outputBytes);
                iccEmbedded = verification.GetColorProfile() is not null
                    || verification.ProfileNames.Any(IsIccName);
            }
            catch { }
        }
        return new ExportMetadataResult(
            outputBytes,
            sourceApplied,
            suppressSource,
            iccEmbedded,
            isComposite && options.Icc == ExportIccMode.EmbedDisplay && !iccEmbedded);
    }

    public static string? WriteProvenanceSidecar(
        string outputPath,
        ReadOnlySpan<byte> outputBytes,
        MagickFormat format,
        ExportMetadataOptions options,
        ExportMetadataResult result,
        string? sourcePath,
        bool isComposite,
        bool isRedacted,
        int width,
        int height)
    {
        if (options.Provenance != ExportProvenanceMode.Sidecar)
            return null;

        string sidecarPath = outputPath + ".provenance.json";
        var source = isRedacted
            ? new
            {
                metadata = "suppressed for redacted output",
                fileName = (string?)null,
                sha256 = (string?)null
            }
            : new
            {
                metadata = sourcePath is null ? "not available" : "source file hash only",
                fileName = sourcePath is null ? null : Path.GetFileName(sourcePath),
                sha256 = sourcePath is null ? null : TryHashFile(sourcePath)
            };

        var document = new
        {
            schemaVersion = 1,
            kind = "snapture-export-provenance",
            authenticity = "descriptive local sidecar; not a C2PA signature",
            application = "Snapture",
            applicationVersion = typeof(ExportMetadataService).Assembly.GetName().Version?.ToString(3),
            generatedAtUtc = DateTime.UtcNow,
            output = new
            {
                fileName = Path.GetFileName(outputPath),
                format = format.ToString().ToUpperInvariant(),
                width,
                height,
                sha256 = Convert.ToHexString(SHA256.HashData(outputBytes)).ToLowerInvariant()
            },
            policy = new
            {
                metadata = options.Metadata.ToString(),
                icc = options.Icc.ToString(),
                provenance = options.Provenance.ToString(),
                sourceMetadataApplied = result.SourceMetadataApplied,
                sourceMetadataSuppressed = result.SourceMetadataSuppressed,
                iccEmbedded = result.IccEmbedded,
                iccStatus = result.IccUnavailableForComposite
                    ? "not embedded; composite has no single display ICC profile"
                    : result.IccEmbedded ? "embedded" : "not embedded"
            },
            composite = isComposite,
            redacted = isRedacted,
            source
        };

        File.WriteAllText(
            sidecarPath,
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        return sidecarPath;
    }

    internal static bool TryParseMetadataMode(string? value, out ExportMetadataMode mode)
    {
        if (TryParseEnum(value, "source", ExportMetadataMode.PreserveSource, out mode)
            || TryParseEnum(value, "replace", ExportMetadataMode.ReplaceWithSnapture, out mode))
            return true;
        return TryParseEnum(value, alias: null, ExportMetadataMode.Strip, out mode);
    }

    internal static bool TryParseIccMode(string? value, out ExportIccMode mode)
    {
        if (string.Equals(value, "source", StringComparison.OrdinalIgnoreCase))
            value = nameof(ExportIccMode.PreserveSource);
        else if (string.Equals(value, "display", StringComparison.OrdinalIgnoreCase))
            value = nameof(ExportIccMode.EmbedDisplay);
        if (TryParseEnum(value, "source", ExportIccMode.PreserveSource, out mode)
            || TryParseEnum(value, "display", ExportIccMode.EmbedDisplay, out mode))
            return true;
        return TryParseEnum(value, alias: null, ExportIccMode.Strip, out mode);
    }

    internal static bool TryParseProvenanceMode(string? value, out ExportProvenanceMode mode)
    {
        if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
            value = nameof(ExportProvenanceMode.Disabled);
        if (TryParseEnum(value, "off", ExportProvenanceMode.Disabled, out mode))
            return true;
        return TryParseEnum(value, alias: null, ExportProvenanceMode.Disabled, out mode);
    }

    private static bool TryParseEnum<T>(
        string? value,
        string? alias,
        T aliasValue,
        out T result)
        where T : struct, Enum
    {
        if (alias is not null && string.Equals(value, alias, StringComparison.OrdinalIgnoreCase))
        {
            result = aliasValue;
            return true;
        }

        return Enum.TryParse(value, ignoreCase: true, out result)
            && Enum.IsDefined(typeof(T), result);
    }

    private static ExportMetadataSnapshot ReadFromImage(MagickImage image)
    {
        var profiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? icc = null;
        foreach (var name in image.ProfileNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var profile = image.GetProfile(name);
                if (profile is null)
                    continue;
                var data = profile.ToByteArray();
                if (data.Length is 0 or > MaximumProfileBytes)
                    continue;
                if (IsIccName(name))
                    icc = data;
                else
                    profiles[name] = data;
            }
            catch
            {
                // Some formats expose profile names that cannot be read through the
                // current delegate. Preserve the rest of the snapshot.
            }
        }

        if (icc is null)
        {
            try
            {
                var colorProfile = image.GetColorProfile();
                if (colorProfile is not null)
                    icc = colorProfile.ToByteArray();
            }
            catch { }
        }

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in image.AttributeNames.Take(MaximumAttributeCount))
        {
            try
            {
                var value = image.GetAttribute(name);
                if (!string.IsNullOrEmpty(value))
                    attributes[name] = value.Length <= MaximumAttributeCharacters
                        ? value
                        : value[..MaximumAttributeCharacters];
            }
            catch { }
        }

        return new ExportMetadataSnapshot(profiles, attributes, icc);
    }

    private static bool TrySetIccProfile(MagickImage image, byte[] profile)
    {
        if (profile.Length > MaximumProfileBytes || !DisplayColorProfileProbe.IsIccProfile(profile))
            return false;

        try
        {
            image.SetProfile(new ColorProfile(profile));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void RemoveIccProfiles(MagickImage image)
    {
        try { image.RemoveProfile("icc"); } catch { }
        try { image.RemoveProfile("icm"); } catch { }
    }

    private static bool IsIccName(string name) =>
        name.Equals("icc", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("icm", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("color", StringComparison.OrdinalIgnoreCase);

    private static byte[] EmbedPngTextAttributes(
        byte[] png,
        IReadOnlyDictionary<string, string> attributes)
    {
        var text = attributes
            .Select(attribute => (Keyword: ToPngTextKeyword(attribute.Key), attribute.Value))
            .Where(item => item.Keyword is not null && !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => (item.Keyword!, item.Value))
            .GroupBy(item => item.Item1, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        if (text.Length == 0)
            return png;

        using var output = new MemoryStream(png.Length + text.Length * 128);
        output.Write(PngIccProfileEmbedderSignature, 0, PngIccProfileEmbedderSignature.Length);
        int offset = PngIccProfileEmbedderSignature.Length;
        bool inserted = false;
        var keywords = text.Select(item => item.Item1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
            int chunkSize = checked(12 + length);
            if (offset + chunkSize > png.Length)
                throw new InvalidDataException("The PNG chunk is truncated.");

            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            bool removeExisting = type is "tEXt" or "iTXt" or "zTXt"
                && keywords.Contains(ReadPngTextKeyword(png.AsSpan(offset + 8, length)) ?? string.Empty);
            if (type == "IEND")
            {
                foreach (var item in text)
                    WritePngTextChunk(output, item.Item1, item.Item2);
                output.Write(png, offset, chunkSize);
                inserted = true;
                break;
            }
            if (!removeExisting)
                output.Write(png, offset, chunkSize);
            offset += chunkSize;
        }

        if (!inserted)
            throw new InvalidDataException("The PNG does not contain an IEND chunk.");
        return output.ToArray();
    }

    private static string? ToPngTextKeyword(string name) => name.ToLowerInvariant() switch
    {
        "comment" => "comment",
        "software" => "software",
        "author" => "author",
        "description" => "description",
        "title" => "title",
        "copyright" => "copyright",
        _ => null
    };

    private static string? ReadPngTextKeyword(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        if (end <= 0)
            return null;
        return Encoding.Latin1.GetString(data[..end]);
    }

    private static void WritePngTextChunk(Stream output, string keyword, string value)
    {
        byte[] key = Encoding.Latin1.GetBytes(keyword);
        byte[] text = Encoding.Latin1.GetBytes(value);
        byte[] data = new byte[key.Length + 1 + text.Length];
        key.CopyTo(data, 0);
        text.CopyTo(data, key.Length + 1);
        WritePngChunk(output, "tEXt"u8, data);
    }

    private static void WritePngChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        byte[] crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(type.Length));
        Span<byte> crc = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(crcInput));
        output.Write(crc);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFF_FFFF;
        foreach (byte value in data)
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        return ~crc;
    }

    private static readonly byte[] PngIccProfileEmbedderSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : 0xEDB8_8320 ^ (value >> 1);
            table[i] = value;
        }
        return table;
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
