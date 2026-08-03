using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using ImageMagick;

namespace Snapture.App.Services;

internal readonly record struct GifFrameInput(Bitmap Bitmap, int DelayMs);

internal enum AnimatedImageFormat
{
    Gif,
    Apng,
    Avif
}

internal sealed record GifEncodingOptions(int Colors = 256, double FuzzPercent = 1.0)
{
    public static GifEncodingOptions Default { get; } = new();

    public void Validate()
    {
        if (Colors is < 2 or > 256)
            throw new ArgumentOutOfRangeException(nameof(Colors), "GIF palettes must contain between 2 and 256 colors.");
        if (double.IsNaN(FuzzPercent) || double.IsInfinity(FuzzPercent) || FuzzPercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(FuzzPercent), "GIF fuzz must be between 0 and 100 percent.");
    }
}

/// <summary>
/// Encodes the in-memory GIF frame model through ImageMagick's global palette and layer optimizer.
/// ColorFuzz is the managed equivalent of ImageMagick's -fuzz option and enables bounded lossy
/// optimization without changing the user's captured source bitmaps.
/// </summary>
internal static class GifEncoder
{
    private const int AnimationTicksPerSecond = 100;

    public static void Encode(
        string outputPath,
        IEnumerable<GifFrameInput> frames,
        GifEncodingOptions options,
        AnimatedImageFormat format = AnimatedImageFormat.Gif)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(frames);
        options.Validate();
        MagickFormat outputFormat = ToMagickFormat(format);
        if (!IsFormatSupported(format))
            throw new InvalidOperationException(
                $"The installed ImageMagick build cannot write animated {format} output.");

        var frameList = frames.ToList();
        if (frameList.Count == 0)
            throw new InvalidOperationException("No frames recorded.");

        var firstFrame = frameList[0].Bitmap;
        foreach (var frame in frameList)
        {
            if (frame.Bitmap.Width != firstFrame.Width || frame.Bitmap.Height != firstFrame.Height)
                throw new InvalidOperationException("All GIF frames must have the same dimensions.");
            if (frame.DelayMs < 20)
                throw new ArgumentOutOfRangeException(nameof(frames), "GIF frame delays must be at least 20 milliseconds.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        if (format == AnimatedImageFormat.Apng)
        {
            EncodeApng(outputPath, frameList);
            return;
        }

        using var images = new MagickImageCollection();
        for (int frameIndex = 0; frameIndex < frameList.Count; frameIndex++)
        {
            var frame = frameList[frameIndex];
            using var png = new MemoryStream();
            frame.Bitmap.Save(png, ImageFormat.Png);
            png.Position = 0;

            // The ImageMagick AVIF delegate currently writes frame durations in reverse
            // collection order. Pre-reverse the metadata so playback timing stays attached
            // to the corresponding pixels.
            int delayMs = format == AnimatedImageFormat.Avif
                ? frameList[frameList.Count - frameIndex - 1].DelayMs
                : frame.DelayMs;

            var image = new MagickImage(png)
            {
                AnimationTicksPerSecond = AnimationTicksPerSecond,
                AnimationDelay = (uint)Math.Max(2, Math.Round(delayMs / 10.0)),
            };
            if (format == AnimatedImageFormat.Gif)
            {
                image.GifDisposeMethod = GifDisposeMethod.Previous;
                image.ColorFuzz = new Percentage(options.FuzzPercent);
            }
            else if (format == AnimatedImageFormat.Avif)
            {
                image.Quality = 90;
            }
            images.Add(image);
        }

        if (format == AnimatedImageFormat.Gif)
        {
            images.Quantize(new QuantizeSettings
            {
                Colors = (uint)options.Colors,
                DitherMethod = DitherMethod.FloydSteinberg
            });
            images.Optimize();
        }

        images.Write(outputPath, outputFormat);
    }

    public static bool IsFormatSupported(AnimatedImageFormat format)
    {
        if (format == AnimatedImageFormat.Apng)
            return true;

        MagickFormat outputFormat = ToMagickFormat(format);
        return MagickNET.SupportedFormats.Any(info =>
            info.Format == outputFormat &&
            info.SupportsWriting &&
            info.SupportsMultipleFrames);
    }

    private static MagickFormat ToMagickFormat(AnimatedImageFormat format)
        => format switch
        {
            AnimatedImageFormat.Gif => MagickFormat.Gif,
            AnimatedImageFormat.Apng => MagickFormat.APng,
            AnimatedImageFormat.Avif => MagickFormat.Avif,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    public static string GetExtension(AnimatedImageFormat format)
        => format switch
        {
            AnimatedImageFormat.Gif => ".gif",
            AnimatedImageFormat.Apng => ".apng",
            AnimatedImageFormat.Avif => ".avif",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    public static string GetDisplayName(AnimatedImageFormat format)
        => format switch
        {
            AnimatedImageFormat.Gif => "Animated GIF",
            AnimatedImageFormat.Apng => "Animated PNG",
            AnimatedImageFormat.Avif => "Animated AVIF",
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

    private static void EncodeApng(string outputPath, IReadOnlyList<GifFrameInput> frames)
    {
        var pngFrames = frames.Select(frame => ReadPngFrame(EncodePng(frame.Bitmap))).ToList();
        var first = pngFrames[0];
        foreach (var frame in pngFrames.Skip(1))
        {
            if (!first.Header.SequenceEqual(frame.Header))
                throw new InvalidOperationException("All APNG frames must use the same PNG color format.");
        }

        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(PngSignature);
        WriteChunk(output, "IHDR", first.Header);
        foreach (var chunk in first.BeforeImageData.Where(chunk => chunk.Type != "IHDR"))
            WriteChunk(output, chunk.Type, chunk.Data);

        WriteChunk(output, "acTL", CreateAnimationControl(frames.Count));
        uint sequence = 0;
        for (int frameIndex = 0; frameIndex < pngFrames.Count; frameIndex++)
        {
            WriteChunk(output, "fcTL", CreateFrameControl(
                sequence++,
                first.Width,
                first.Height,
                frames[frameIndex].DelayMs));

            if (frameIndex == 0)
            {
                foreach (var chunk in pngFrames[frameIndex].ImageData)
                    WriteChunk(output, "IDAT", chunk.Data);
            }
            else
            {
                foreach (var chunk in pngFrames[frameIndex].ImageData)
                {
                    byte[] frameData = new byte[sizeof(uint) + chunk.Data.Length];
                    BinaryPrimitives.WriteUInt32BigEndian(frameData, sequence++);
                    chunk.Data.CopyTo(frameData, sizeof(uint));
                    WriteChunk(output, "fdAT", frameData);
                }
            }
        }

        WriteChunk(output, "IEND", Array.Empty<byte>());
    }

    private static byte[] EncodePng(Bitmap source)
    {
        using var normalized = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(normalized))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        using var stream = new MemoryStream();
        normalized.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static PngFrame ReadPngFrame(byte[] bytes)
    {
        if (!bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            throw new InvalidOperationException("The generated frame is not a PNG image.");

        var beforeImageData = new List<PngChunk>();
        var imageData = new List<PngChunk>();
        byte[]? header = null;
        int offset = PngSignature.Length;
        while (offset + 12 <= bytes.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, sizeof(uint)));
            if (length > int.MaxValue || offset + 12L + length > bytes.Length)
                throw new InvalidOperationException("The generated PNG frame is truncated.");

            string type = Encoding.ASCII.GetString(bytes, offset + sizeof(uint), 4);
            byte[] data = bytes.AsSpan(offset + 8, (int)length).ToArray();
            var chunk = new PngChunk(type, data);
            if (type == "IHDR")
                header = data;
            else if (type == "IDAT")
                imageData.Add(chunk);
            else if (imageData.Count == 0 && type != "IEND")
                beforeImageData.Add(chunk);

            offset += 12 + (int)length;
            if (type == "IEND")
                break;
        }

        if (header is null || header.Length != 13 || imageData.Count == 0)
            throw new InvalidOperationException("The generated PNG frame does not contain image data.");

        uint width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, sizeof(uint)));
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(sizeof(uint), sizeof(uint)));
        if (width == 0 || height == 0)
            throw new InvalidOperationException("The generated PNG frame has invalid dimensions.");

        return new PngFrame(header, beforeImageData, imageData, width, height);
    }

    private static byte[] CreateAnimationControl(int frameCount)
    {
        byte[] data = new byte[sizeof(uint) * 2];
        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)frameCount);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(sizeof(uint)), 0);
        return data;
    }

    private static byte[] CreateFrameControl(uint sequence, uint width, uint height, int delayMs)
    {
        byte[] data = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(data, sequence);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), height);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(20), (ushort)Math.Max(2, Math.Round(delayMs / 10.0)));
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), 100);
        data[24] = 0;
        data[25] = 0;
        return data;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);

        Span<byte> typeBytes = stackalloc byte[4];
        Encoding.ASCII.GetBytes(type, typeBytes);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = CalculateCrc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xffffffff;
        foreach (byte value in type)
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        foreach (byte value in data)
            crc = CrcTable[(crc ^ value) & 0xff] ^ (crc >> 8);
        return ~crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : 0xedb88320 ^ (value >> 1);
            table[index] = value;
        }

        return table;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();
    private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    private readonly record struct PngChunk(string Type, byte[] Data);

    private sealed record PngFrame(
        byte[] Header,
        IReadOnlyList<PngChunk> BeforeImageData,
        IReadOnlyList<PngChunk> ImageData,
        uint Width,
        uint Height);
}
