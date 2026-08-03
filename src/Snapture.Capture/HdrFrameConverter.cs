using System.Buffers.Binary;

namespace Snapture.Capture;

/// <summary>Converts an FP16 scRGB WGC surface to the SDR BGRA8 boundary.</summary>
public static class HdrFrameConverter
{
    public static void ConvertRgba16FloatToBgra(
        ReadOnlySpan<byte> source,
        int sourceRowPitch,
        Span<byte> destination,
        int destinationRowPitch,
        int sourceX,
        int sourceY,
        int width,
        int height)
    {
        if (sourceRowPitch <= 0 || destinationRowPitch <= 0
            || sourceX < 0 || sourceY < 0 || width < 0 || height < 0)
            throw new ArgumentOutOfRangeException();
        if (destinationRowPitch < width * 4)
            throw new ArgumentException("Destination rows are too narrow.", nameof(destinationRowPitch));
        if (width == 0 || height == 0) return;

        int lastSourceByte = checked((sourceY + height - 1) * sourceRowPitch
            + (sourceX + width) * 8);
        int lastDestinationByte = checked((height - 1) * destinationRowPitch + width * 4);
        if (height > 0 && (lastSourceByte > source.Length || lastDestinationByte > destination.Length))
            throw new ArgumentException("The supplied buffers do not contain the requested frame.");

        for (int y = 0; y < height; y++)
        {
            int sourceOffset = checked((sourceY + y) * sourceRowPitch + sourceX * 8);
            int destinationOffset = y * destinationRowPitch;
            for (int x = 0; x < width; x++)
            {
                int pixelOffset = sourceOffset + x * 8;
                float red = ReadHalf(source, pixelOffset);
                float green = ReadHalf(source, pixelOffset + 2);
                float blue = ReadHalf(source, pixelOffset + 4);
                float alpha = ReadHalf(source, pixelOffset + 6);

                int outputOffset = destinationOffset + x * 4;
                destination[outputOffset] = ToSrgbByte(blue);
                destination[outputOffset + 1] = ToSrgbByte(green);
                destination[outputOffset + 2] = ToSrgbByte(red);
                destination[outputOffset + 3] = ToAlphaByte(alpha);
            }
        }
    }

    private static float ReadHalf(ReadOnlySpan<byte> source, int offset)
        => (float)BitConverter.UInt16BitsToHalf(
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, sizeof(ushort))));

    private static byte ToSrgbByte(float linear)
    {
        if (!float.IsFinite(linear) || linear <= 0f) return 0;
        float toneMapped = linear / (1f + linear);
        float srgb = toneMapped <= 0.0031308f
            ? toneMapped * 12.92f
            : 1.055f * MathF.Pow(toneMapped, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(Math.Clamp(srgb, 0f, 1f) * 255f);
    }

    private static byte ToAlphaByte(float alpha)
        => !float.IsFinite(alpha)
            ? (byte)0
            : (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f);
}
