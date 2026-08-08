using System.Buffers.Binary;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Microsoft.Win32.SafeHandles;
using SkiaSharp;

namespace Snapture.App.Services;

public enum SafeImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    WebP,
    Tiff
}

public sealed record SafeImageInputInfo(
    string FullPath,
    long Length,
    int Width,
    int Height,
    SafeImageFormat Format);

public sealed class SafeImageInputException : Exception
{
    public SafeImageInputException(string message)
        : base(message)
    {
    }

    public SafeImageInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class SafeImageInputFile : IDisposable
{
    private FileStream? _stream;

    internal SafeImageInputFile(SafeImageInputInfo info, FileStream stream)
    {
        Info = info;
        _stream = stream;
    }

    public SafeImageInputInfo Info { get; }

    public FileStream Stream => _stream ?? throw new ObjectDisposedException(nameof(SafeImageInputFile));

    public void Dispose()
    {
        Interlocked.Exchange(ref _stream, null)?.Dispose();
    }
}

/// <summary>
/// The single file-backed image intake boundary. It validates the path and content while holding
/// one read-only handle, so callers pass the validated stream to their decoder instead of reopening
/// an untrusted path. Memory-backed images generated inside Snapture do not use this boundary.
/// </summary>
public static class SafeImageInput
{
    public const long MaxFileBytes = 100L * 1024 * 1024;
    public const int MaxDimension = 16_384;
    public const long MaxPixels = 100_000_000;
    public const int MaxAnimationFrames = 240;

    private const int PrefixProbeBytes = 64;
    private const int MaxTiffEntries = 4_096;
    private const uint GenericRead = 0x80000000;
    private const uint ShareRead = 0x00000001;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileFlagSequentialScan = 0x08000000;
    private static readonly IReadOnlyDictionary<string, SafeImageFormat> Formats =
        new Dictionary<string, SafeImageFormat>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = SafeImageFormat.Png,
            [".jpg"] = SafeImageFormat.Jpeg,
            [".jpeg"] = SafeImageFormat.Jpeg,
            [".gif"] = SafeImageFormat.Gif,
            [".bmp"] = SafeImageFormat.Bmp,
            [".webp"] = SafeImageFormat.WebP,
            [".tif"] = SafeImageFormat.Tiff,
            [".tiff"] = SafeImageFormat.Tiff
        };

    public static bool IsSupportedExtension(string? extension)
        => extension is not null && Formats.ContainsKey(extension);

    public static SafeImageInputInfo ValidateFile(string path)
    {
        using var input = Open(path);
        return input.Info;
    }

    public static SafeImageInputFile Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        string extension = Path.GetExtension(fullPath);
        if (!Formats.TryGetValue(extension, out var expectedFormat))
            throw new ArgumentException(
                $"Unsupported image extension '{extension}'. Supported extensions are png, jpg, jpeg, gif, bmp, webp, tif, and tiff.",
                nameof(path));

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("The source image does not exist.", fullPath);

        EnsureNoReparsePoints(fullPath);

        var fileInfo = new FileInfo(fullPath);
        long length = fileInfo.Length;
        if (length <= 0)
            throw new SafeImageInputException("The source image is empty.");
        if (length > MaxFileBytes)
            throw new SafeImageInputException($"The source image exceeds the {MaxFileBytes / (1024 * 1024)} MB safety limit.");

        FileStream? stream = null;
        try
        {
            stream = OpenReadHandle(fullPath);

            if (stream.Length != length)
                throw new SafeImageInputException("The source image changed while it was being opened.");

            var info = Inspect(stream, fullPath, length, expectedFormat);
            stream.Position = 0;
            return new SafeImageInputFile(info, stream);
        }
        catch
        {
            stream?.Dispose();
            throw;
        }
    }

    public static BitmapImage LoadBitmapImage(string path, int decodePixelWidth = 0)
    {
        using var input = Open(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodePixelWidth > 0)
            image.DecodePixelWidth = decodePixelWidth;
        image.StreamSource = input.Stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    public static async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        using var input = Open(path);
        using var buffer = new MemoryStream(checked((int)input.Info.Length));
        await input.Stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static SafeImageInputInfo Inspect(
        FileStream stream,
        string fullPath,
        long length,
        SafeImageFormat expectedFormat)
    {
        try
        {
            byte[] prefix = new byte[PrefixProbeBytes];
            int prefixLength = ReadAtMost(stream, prefix);
            if (!TryDetectFormat(prefix.AsSpan(0, prefixLength), out var actualFormat))
                throw new SafeImageInputException("The source image does not have a supported image signature.");
            if (actualFormat != expectedFormat)
                throw new SafeImageInputException(
                    $"The source image extension does not match its content ({expectedFormat} expected, {actualFormat} found).");

            int width;
            int height;
            if (actualFormat == SafeImageFormat.Tiff)
            {
                if (!TryReadTiffDimensions(stream, prefix.AsSpan(0, prefixLength), out ulong tiffWidth, out ulong tiffHeight))
                    throw new SafeImageInputException("The TIFF header or image directory is invalid.");
                (width, height) = ValidateDimensions(tiffWidth, tiffHeight);
            }
            else
            {
                var headerDimensions = TryReadFixedHeaderDimensions(prefix.AsSpan(0, prefixLength), actualFormat);
                if (headerDimensions is { } fixedDimensions)
                    ValidateDimensions((ulong)fixedDimensions.Width, (ulong)fixedDimensions.Height);

                stream.Position = 0;
                using var codecInput = new NonDisposingReadStream(stream);
                using var codec = SKCodec.Create(codecInput, out var codecResult);
                if (codec is null || codecResult != SKCodecResult.Success)
                    throw new SafeImageInputException(
                        $"The source image failed decoder validation ({codecResult}).");
                if (!TryMapCodecFormat(codec.EncodedFormat, out var codecFormat) || codecFormat != actualFormat)
                    throw new SafeImageInputException("The source image decoder reported a different format than its signature.");
                if (codec.FrameCount > MaxAnimationFrames)
                    throw new SafeImageInputException($"The source image contains more than {MaxAnimationFrames} animation frames.");

                var decodedInfo = codec.Info;
                (width, height) = ValidateDimensions((ulong)decodedInfo.Width, (ulong)decodedInfo.Height);
                if (headerDimensions is { } && (width != headerDimensions.Value.Width || height != headerDimensions.Value.Height))
                    throw new SafeImageInputException("The source image contains inconsistent dimension headers.");
            }

            stream.Position = 0;
            return new SafeImageInputInfo(fullPath, length, width, height, actualFormat);
        }
        catch (SafeImageInputException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new SafeImageInputException("The source image could not be validated by the decoder.", ex);
        }
    }

    private static int ReadAtMost(Stream stream, byte[] buffer)
    {
        stream.Position = 0;
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static (int Width, int Height)? TryReadFixedHeaderDimensions(
        ReadOnlySpan<byte> prefix,
        SafeImageFormat format)
    {
        if (format == SafeImageFormat.Png)
        {
            if (prefix.Length < 24)
                throw new SafeImageInputException("The PNG header is truncated.");
            return (checked((int)BinaryPrimitives.ReadUInt32BigEndian(prefix[16..20])),
                checked((int)BinaryPrimitives.ReadUInt32BigEndian(prefix[20..24])));
        }

        if (format == SafeImageFormat.Gif)
        {
            if (prefix.Length < 10)
                throw new SafeImageInputException("The GIF header is truncated.");
            return (BinaryPrimitives.ReadUInt16LittleEndian(prefix[6..8]),
                BinaryPrimitives.ReadUInt16LittleEndian(prefix[8..10]));
        }

        if (format == SafeImageFormat.Bmp)
        {
            if (prefix.Length < 26)
                throw new SafeImageInputException("The BMP header is truncated.");
            long width = BinaryPrimitives.ReadInt32LittleEndian(prefix[18..22]);
            long height = BinaryPrimitives.ReadInt32LittleEndian(prefix[22..26]);
            return (checked((int)Math.Abs(width)), checked((int)Math.Abs(height)));
        }

        return null;
    }

    private static bool TryDetectFormat(ReadOnlySpan<byte> prefix, out SafeImageFormat format)
    {
        if (prefix.Length >= 8
            && prefix[0] == 0x89 && prefix[1] == 0x50 && prefix[2] == 0x4e && prefix[3] == 0x47
            && prefix[4] == 0x0d && prefix[5] == 0x0a && prefix[6] == 0x1a && prefix[7] == 0x0a)
        {
            format = SafeImageFormat.Png;
            return true;
        }
        if (prefix.Length >= 3 && prefix[0] == 0xff && prefix[1] == 0xd8 && prefix[2] == 0xff)
        {
            format = SafeImageFormat.Jpeg;
            return true;
        }
        if (prefix.Length >= 6 && (prefix[..6].SequenceEqual("GIF87a"u8) || prefix[..6].SequenceEqual("GIF89a"u8)))
        {
            format = SafeImageFormat.Gif;
            return true;
        }
        if (prefix.Length >= 2 && prefix[..2].SequenceEqual("BM"u8))
        {
            format = SafeImageFormat.Bmp;
            return true;
        }
        if (prefix.Length >= 12 && prefix[..4].SequenceEqual("RIFF"u8) && prefix[8..12].SequenceEqual("WEBP"u8))
        {
            format = SafeImageFormat.WebP;
            return true;
        }
        if (prefix.Length >= 4
            && ((prefix[0] == 'I' && prefix[1] == 'I' && prefix[2] == 42 && prefix[3] == 0)
                || (prefix[0] == 'M' && prefix[1] == 'M' && prefix[2] == 0 && prefix[3] == 42)
                || (prefix[0] == 'I' && prefix[1] == 'I' && prefix[2] == 43 && prefix[3] == 0)
                || (prefix[0] == 'M' && prefix[1] == 'M' && prefix[2] == 0 && prefix[3] == 43)))
        {
            format = SafeImageFormat.Tiff;
            return true;
        }

        format = default;
        return false;
    }

    private static bool TryMapCodecFormat(SKEncodedImageFormat format, out SafeImageFormat mapped)
    {
        mapped = format switch
        {
            SKEncodedImageFormat.Png => SafeImageFormat.Png,
            SKEncodedImageFormat.Jpeg => SafeImageFormat.Jpeg,
            SKEncodedImageFormat.Gif => SafeImageFormat.Gif,
            SKEncodedImageFormat.Bmp => SafeImageFormat.Bmp,
            SKEncodedImageFormat.Webp => SafeImageFormat.WebP,
            _ => default
        };
        return format is SKEncodedImageFormat.Png
            or SKEncodedImageFormat.Jpeg
            or SKEncodedImageFormat.Gif
            or SKEncodedImageFormat.Bmp
            or SKEncodedImageFormat.Webp;
    }

    private static (int Width, int Height) ValidateDimensions(ulong width, ulong height)
    {
        if (width == 0 || height == 0)
            throw new SafeImageInputException("The source image has empty dimensions.");
        if (width > MaxDimension || height > MaxDimension)
            throw new SafeImageInputException($"The source image dimensions exceed the {MaxDimension}-pixel safety limit.");
        if (width > (ulong)MaxPixels / height)
            throw new SafeImageInputException($"The source image exceeds the {MaxPixels:N0}-pixel safety limit.");
        if (width > int.MaxValue || height > int.MaxValue)
            throw new SafeImageInputException("The source image dimensions are outside the supported range.");
        return ((int)width, (int)height);
    }

    private static void EnsureNoReparsePoints(string fullPath)
    {
        string? current = fullPath;
        while (current is not null)
        {
            FileAttributes attributes = File.GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Image paths may not contain reparse points.");

            string? parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }
    }

    private static FileStream OpenReadHandle(string fullPath)
    {
        SafeFileHandle handle = CreateFile(
            fullPath,
            GenericRead,
            ShareRead,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal | FileFlagOpenReparsePoint | FileFlagSequentialScan,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new Win32Exception(error, "The source image could not be opened for safe reading.");
        }

        return new FileStream(handle, FileAccess.Read, 64 * 1024, isAsync: false);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    private static bool TryReadTiffDimensions(
        FileStream stream,
        ReadOnlySpan<byte> prefix,
        out ulong width,
        out ulong height)
    {
        width = 0;
        height = 0;
        if (prefix.Length < 8)
            return false;

        bool littleEndian = prefix[0] == (byte)'I';
        bool bigTiff = prefix[2] == 43;
        if (bigTiff)
        {
            if (prefix.Length < 16 || !TryReadUInt16(prefix[4..6], littleEndian, out ushort offsetSize) || offsetSize != 8
                || !TryReadUInt64(prefix[8..16], littleEndian, out ulong bigIfdOffset))
                return false;
            return TryReadTiffDirectory(stream, littleEndian, true, bigIfdOffset, out width, out height);
        }

        if (!TryReadUInt32(prefix[4..8], littleEndian, out uint ifdOffset))
            return false;
        return TryReadTiffDirectory(stream, littleEndian, false, ifdOffset, out width, out height);
    }

    private static bool TryReadTiffDirectory(
        FileStream stream,
        bool littleEndian,
        bool bigTiff,
        ulong directoryOffset,
        out ulong width,
        out ulong height)
    {
        width = 0;
        height = 0;
        int entrySize = bigTiff ? 20 : 12;
        if (directoryOffset > (ulong)long.MaxValue)
            return false;

        long offset = (long)directoryOffset;
        ulong count;
        if (bigTiff)
        {
            if (!TryReadUInt64At(stream, offset, littleEndian, out count))
                return false;
            offset = checked(offset + 8);
        }
        else
        {
            if (!TryReadUInt16At(stream, offset, littleEndian, out ushort shortCount))
                return false;
            count = shortCount;
            offset = checked(offset + 2);
        }

        if (count == 0 || count > MaxTiffEntries)
            return false;

        bool widthFound = false;
        bool heightFound = false;
        for (ulong index = 0; index < count; index++)
        {
            long entryOffset = checked(offset + checked((long)index * entrySize));
            byte[] entry = new byte[entrySize];
            if (!TryReadAt(stream, entryOffset, entry))
                return false;
            bool isWidth = ReadUInt16(entry.AsSpan(0, 2), littleEndian) == 256;
            bool isHeight = ReadUInt16(entry.AsSpan(0, 2), littleEndian) == 257;
            if (!isWidth && !isHeight)
                continue;

            ushort type = ReadUInt16(entry.AsSpan(2, 2), littleEndian);
            ulong valueCount = bigTiff
                ? ReadUInt64(entry.AsSpan(4, 8), littleEndian)
                : ReadUInt32(entry.AsSpan(4, 4), littleEndian);
            if (valueCount != 1 || !TryReadTiffValue(entry, type, littleEndian, bigTiff, out ulong value))
                return false;
            if (isWidth)
            {
                width = value;
                widthFound = true;
            }
            else
            {
                height = value;
                heightFound = true;
            }
        }

        return widthFound && heightFound;
    }

    private static bool TryReadTiffValue(
        ReadOnlySpan<byte> entry,
        ushort type,
        bool littleEndian,
        bool bigTiff,
        out ulong value)
    {
        value = 0;
        ReadOnlySpan<byte> inlineValue = entry[8..];
        switch (type)
        {
            case 1:
                value = inlineValue[0];
                return true;
            case 3:
                value = ReadUInt16(inlineValue, littleEndian);
                return true;
            case 4:
                value = ReadUInt32(inlineValue, littleEndian);
                return true;
            case 6:
                value = inlineValue[0];
                return true;
            case 8:
                short signedShort = littleEndian
                    ? BinaryPrimitives.ReadInt16LittleEndian(inlineValue)
                    : BinaryPrimitives.ReadInt16BigEndian(inlineValue);
                if (signedShort <= 0) return false;
                value = (ulong)signedShort;
                return true;
            case 9:
                int signedLong = littleEndian
                    ? BinaryPrimitives.ReadInt32LittleEndian(inlineValue)
                    : BinaryPrimitives.ReadInt32BigEndian(inlineValue);
                if (signedLong <= 0) return false;
                value = (ulong)signedLong;
                return true;
            case 16 when bigTiff:
                value = ReadUInt64(inlineValue, littleEndian);
                return true;
            case 17 when bigTiff:
                long signedLong8 = littleEndian
                    ? BinaryPrimitives.ReadInt64LittleEndian(inlineValue)
                    : BinaryPrimitives.ReadInt64BigEndian(inlineValue);
                if (signedLong8 <= 0) return false;
                value = (ulong)signedLong8;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadAt(Stream stream, long offset, byte[] destination)
    {
        if (offset < 0 || offset > stream.Length - destination.Length)
            return false;
        stream.Position = offset;
        int total = 0;
        while (total < destination.Length)
        {
            int read = stream.Read(destination, total, destination.Length - total);
            if (read == 0)
                return false;
            total += read;
        }
        return true;
    }

    private static bool TryReadUInt16At(Stream stream, long offset, bool littleEndian, out ushort value)
    {
        byte[] bytes = new byte[2];
        if (!TryReadAt(stream, offset, bytes))
        {
            value = 0;
            return false;
        }
        value = ReadUInt16(bytes, littleEndian);
        return true;
    }

    private static bool TryReadUInt64At(Stream stream, long offset, bool littleEndian, out ulong value)
    {
        byte[] bytes = new byte[8];
        if (!TryReadAt(stream, offset, bytes))
        {
            value = 0;
            return false;
        }
        value = ReadUInt64(bytes, littleEndian);
        return true;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian, out ushort value)
    {
        if (bytes.Length < 2)
        {
            value = 0;
            return false;
        }
        value = ReadUInt16(bytes, littleEndian);
        return true;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian, out uint value)
    {
        if (bytes.Length < 4)
        {
            value = 0;
            return false;
        }
        value = littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
        return true;
    }

    private static bool TryReadUInt64(ReadOnlySpan<byte> bytes, bool littleEndian, out ulong value)
    {
        if (bytes.Length < 8)
        {
            value = 0;
            return false;
        }
        value = ReadUInt64(bytes, littleEndian);
        return true;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian)
        => littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian)
        => littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, bool littleEndian)
        => littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt64BigEndian(bytes);

    private sealed class NonDisposingReadStream : Stream
    {
        private readonly Stream _inner;

        public NonDisposingReadStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override int Read(Span<byte> buffer)
            => _inner.Read(buffer);

        public override long Seek(long offset, SeekOrigin origin)
            => _inner.Seek(offset, origin);

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            // The caller owns the validated file handle.
        }
    }
}
