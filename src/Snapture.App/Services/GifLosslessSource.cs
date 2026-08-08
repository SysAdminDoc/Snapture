using System.IO;

namespace Snapture.App.Services;

internal sealed record GifLosslessFrame(int Index, int DelayMs, byte[] EncodedBlock);

/// <summary>
/// Keeps the original GIF container and frame blocks so deletion-only edits can be saved
/// without decoding, quantizing, or recompressing the source image data.
/// </summary>
internal sealed class GifLosslessSource
{
    private readonly byte[] _prefix;
    private readonly byte[] _suffix;

    private GifLosslessSource(
        byte[] prefix,
        IReadOnlyList<GifLosslessFrame> frames,
        byte[] suffix)
    {
        _prefix = prefix;
        Frames = frames;
        _suffix = suffix;
    }

    public IReadOnlyList<GifLosslessFrame> Frames { get; }

    public static GifLosslessSource Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var input = SafeImageInput.Open(path);
        return Load(input.Stream);
    }

    internal static GifLosslessSource Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return Parse(buffer.ToArray());
    }

    public void Save(string outputPath, IEnumerable<int> sourceFrameIndices)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(sourceFrameIndices);

        var indices = sourceFrameIndices.ToArray();
        if (indices.Length == 0)
            throw new InvalidOperationException("A lossless GIF clip must keep at least one frame.");

        if (indices.Any(index => index < 0 || index >= Frames.Count))
            throw new ArgumentOutOfRangeException(nameof(sourceFrameIndices));
        if (indices.Distinct().Count() != indices.Length)
            throw new ArgumentException("A lossless GIF clip cannot contain duplicate source frames.", nameof(sourceFrameIndices));

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(_prefix);
        foreach (int index in indices)
            output.Write(Frames[index].EncodedBlock);
        output.Write(_suffix);
    }

    internal byte[] GetEncodedFrameBlock(int index) => (byte[])Frames[index].EncodedBlock.Clone();

    private static GifLosslessSource Parse(byte[] bytes)
    {
        if (bytes.Length < 14 || !bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8) &&
            !bytes.AsSpan(0, 6).SequenceEqual("GIF87a"u8))
        {
            throw new InvalidDataException("The selected file is not a GIF image.");
        }

        int cursor = 13;
        byte packed = bytes[10];
        if ((packed & 0x80) != 0)
            cursor = checked(cursor + 3 * (1 << ((packed & 0x07) + 1)));
        if (cursor > bytes.Length)
            throw new InvalidDataException("The GIF color table is truncated.");

        var frames = new List<GifLosslessFrame>();
        int currentFrameStart = -1;
        int prefixEnd = -1;
        int suffixStart = -1;
        bool seenFrame = false;

        while (cursor < bytes.Length)
        {
            byte marker = bytes[cursor];
            if (marker == 0x21)
            {
                int extensionStart = cursor;
                bool isGraphicControl = cursor + 1 < bytes.Length && bytes[cursor + 1] == 0xf9;
                cursor = SkipExtension(bytes, cursor);
                if (isGraphicControl || seenFrame)
                    currentFrameStart = currentFrameStart < 0 ? extensionStart : currentFrameStart;
                continue;
            }

            if (marker == 0x2c)
            {
                int imageStart = cursor;
                cursor = SkipImage(bytes, cursor);
                int frameStart = currentFrameStart >= 0 ? currentFrameStart : imageStart;
                if (!seenFrame)
                    prefixEnd = frameStart;

                frames.Add(new GifLosslessFrame(
                    frames.Count,
                    ReadDelayMs(bytes, frameStart, imageStart),
                    bytes[frameStart..cursor]));
                currentFrameStart = -1;
                seenFrame = true;
                continue;
            }

            if (marker == 0x3b)
            {
                if (!seenFrame)
                    throw new InvalidDataException("The GIF does not contain any image frames.");
                suffixStart = currentFrameStart >= 0 ? currentFrameStart : cursor;
                break;
            }

            throw new InvalidDataException($"The GIF contains an unknown block marker 0x{marker:x2}.");
        }

        if (prefixEnd < 0 || suffixStart < 0 || frames.Count == 0)
            throw new InvalidDataException("The GIF is missing a trailer or image frame.");

        return new GifLosslessSource(
            bytes[..prefixEnd],
            frames,
            bytes[suffixStart..]);
    }

    private static int SkipExtension(byte[] bytes, int start)
    {
        int cursor = checked(start + 2);
        if (cursor > bytes.Length)
            throw new InvalidDataException("The GIF extension is truncated.");

        while (cursor < bytes.Length)
        {
            int blockLength = bytes[cursor++];
            if (blockLength == 0)
                return cursor;
            cursor = checked(cursor + blockLength);
            if (cursor > bytes.Length)
                throw new InvalidDataException("The GIF extension data is truncated.");
        }

        throw new InvalidDataException("The GIF extension is missing its terminator.");
    }

    private static int SkipImage(byte[] bytes, int start)
    {
        int cursor = checked(start + 10);
        if (cursor > bytes.Length)
            throw new InvalidDataException("The GIF image descriptor is truncated.");

        byte packed = bytes[start + 9];
        if ((packed & 0x80) != 0)
            cursor = checked(cursor + 3 * (1 << ((packed & 0x07) + 1)));
        if (cursor >= bytes.Length)
            throw new InvalidDataException("The GIF local color table is truncated.");

        cursor++;
        while (cursor < bytes.Length)
        {
            int blockLength = bytes[cursor++];
            if (blockLength == 0)
                return cursor;
            cursor = checked(cursor + blockLength);
            if (cursor > bytes.Length)
                throw new InvalidDataException("The GIF image data is truncated.");
        }

        throw new InvalidDataException("The GIF image data is missing its terminator.");
    }

    private static int ReadDelayMs(byte[] bytes, int start, int imageStart)
    {
        for (int cursor = start; cursor + 7 < imageStart; cursor++)
        {
            if (bytes[cursor] != 0x21 || bytes[cursor + 1] != 0xf9 || bytes[cursor + 2] != 4)
                continue;

            int centiseconds = bytes[cursor + 4] | (bytes[cursor + 5] << 8);
            return Math.Clamp(centiseconds * 10, 20, 10_000);
        }

        return 100;
    }
}
