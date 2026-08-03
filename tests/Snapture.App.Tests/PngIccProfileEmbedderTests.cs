using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Text;
using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PngIccProfileEmbedderTests
{
    [TestMethod]
    public void Encode_InsertsCompressedIccpChunkWithOriginalProfile()
    {
        using var bitmap = new Bitmap(3, 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(Color.FromArgb(255, 60, 100, 140));

        byte[] profile = CreateProfile();
        byte[] png = PngIccProfileEmbedder.Encode(bitmap, profile);

        byte[] embedded = ReadEmbeddedProfile(png);
        CollectionAssert.AreEqual(profile, embedded);
    }

    [TestMethod]
    public void Embed_RejectsInvalidProfile()
    {
        using var bitmap = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);

        Assert.Throws<InvalidDataException>(() =>
            PngIccProfileEmbedder.Embed(png.ToArray(), new byte[128]));
    }

    private static byte[] CreateProfile()
    {
        var profile = new byte[128];
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(0, sizeof(uint)), (uint)profile.Length);
        "acsp"u8.CopyTo(profile.AsSpan(36, 4));
        return profile;
    }

    private static byte[] ReadEmbeddedProfile(byte[] png)
    {
        int offset = 8;
        while (offset + 12 <= png.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, sizeof(uint))));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "iCCP")
            {
                int dataStart = offset + 8;
                int nameEnd = Array.IndexOf(png, (byte)0, dataStart, length);
                Assert.IsGreaterThanOrEqualTo(0, nameEnd);
                Assert.AreEqual(0, png[nameEnd + 1]);

                using var compressed = new MemoryStream(
                    png, nameEnd + 2, length - (nameEnd - dataStart) - 2, writable: false);
                using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
                using var profile = new MemoryStream();
                zlib.CopyTo(profile);
                return profile.ToArray();
            }

            offset = checked(offset + 12 + length);
        }

        Assert.Fail("PNG did not contain an iCCP chunk.");
        return Array.Empty<byte>();
    }
}
