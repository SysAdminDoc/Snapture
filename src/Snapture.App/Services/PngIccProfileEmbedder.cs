using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using Snapture.Capture;

namespace Snapture.App.Services;

/// <summary>Injects a standards-compliant compressed iCCP chunk into a PNG.</summary>
internal static class PngIccProfileEmbedder
{
    private static readonly byte[] PngSignature =
        [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IccpType = "iCCP"u8.ToArray();
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(Bitmap bitmap, byte[]? profile)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        byte[] png = output.ToArray();
        return profile is null ? png : Embed(png, profile);
    }

    public static byte[] Embed(ReadOnlySpan<byte> png, ReadOnlySpan<byte> profile)
    {
        if (!png[..Math.Min(png.Length, PngSignature.Length)].SequenceEqual(PngSignature))
            throw new InvalidDataException("The source is not a PNG.");
        if (!DisplayColorProfileProbe.IsIccProfile(profile))
            throw new InvalidDataException("The display profile is not a valid ICC profile.");

        using var output = new MemoryStream(png.Length + profile.Length / 2 + 256);
        output.Write(PngSignature);

        int offset = PngSignature.Length;
        bool sawHeader = false;
        bool inserted = false;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new InvalidDataException("The PNG chunk header is truncated.");

            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, sizeof(uint)));
            long chunkEnd = (long)offset + 12 + length;
            if (length > int.MaxValue || chunkEnd > png.Length)
                throw new InvalidDataException("The PNG chunk is truncated.");

            ReadOnlySpan<byte> type = png.Slice(offset + 4, 4);
            if (type.SequenceEqual(IccpType))
            {
                // Replace an existing profile rather than producing ambiguous metadata.
            }
            else
            {
                output.Write(png.Slice(offset, checked((int)(12 + length))));
                if (type.SequenceEqual("IHDR"u8))
                {
                    sawHeader = true;
                    WriteIccpChunk(output, profile);
                    inserted = true;
                }
            }

            offset = checked((int)chunkEnd);
        }

        if (!sawHeader || !inserted)
            throw new InvalidDataException("The PNG does not contain an IHDR chunk.");
        return output.ToArray();
    }

    private static void WriteIccpChunk(Stream output, ReadOnlySpan<byte> profile)
    {
        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(profile);
            compressed = compressedStream.ToArray();
        }

        byte[] name = Encoding.ASCII.GetBytes("Snapture Display");
        byte[] data = new byte[name.Length + 2 + compressed.Length];
        name.CopyTo(data, 0);
        data[name.Length] = 0;
        data[name.Length + 1] = 0; // Deflate compression method.
        compressed.CopyTo(data, name.Length + 2);
        WriteChunk(output, IccpType, data);
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
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
}
