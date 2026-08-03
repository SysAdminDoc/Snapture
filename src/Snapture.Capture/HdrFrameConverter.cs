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
        int height,
        HdrToneMapOperator toneMapOperator = HdrToneMapOperator.Reinhard,
        bool applyColorCorrection = true)
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
                destination[outputOffset] = ToSrgbByte(blue, toneMapOperator, applyColorCorrection);
                destination[outputOffset + 1] = ToSrgbByte(green, toneMapOperator, applyColorCorrection);
                destination[outputOffset + 2] = ToSrgbByte(red, toneMapOperator, applyColorCorrection);
                destination[outputOffset + 3] = ToAlphaByte(alpha);
            }
        }
    }

    private static float ReadHalf(ReadOnlySpan<byte> source, int offset)
        => (float)BitConverter.UInt16BitsToHalf(
            BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(offset, sizeof(ushort))));

    private static byte ToSrgbByte(
        float linear,
        HdrToneMapOperator toneMapOperator,
        bool applyColorCorrection)
    {
        if (!float.IsFinite(linear) || linear <= 0f) return 0;
        // The corrected path compresses scRGB highlights before sRGB encoding.
        // Turning the corrector off intentionally uses a direct clamp: this keeps
        // the uncorrected compositor values visible, while making highlight clipping
        // explicit at the unavoidable BGRA8 boundary.
        float normalized = applyColorCorrection
            ? ToneMap(linear, toneMapOperator)
            : Math.Clamp(linear, 0f, 1f);
        float srgb = normalized <= 0.0031308f
            ? normalized * 12.92f
            : 1.055f * MathF.Pow(normalized, 1f / 2.4f) - 0.055f;
        return (byte)MathF.Round(Math.Clamp(srgb, 0f, 1f) * 255f);
    }

    private static byte ToAlphaByte(float alpha)
        => !float.IsFinite(alpha)
            ? (byte)0
            : (byte)MathF.Round(Math.Clamp(alpha, 0f, 1f) * 255f);

    private static float ToneMap(float linear, HdrToneMapOperator toneMapOperator)
    {
        return toneMapOperator switch
        {
            HdrToneMapOperator.Aces => Aces(linear),
            HdrToneMapOperator.Hable => Hable(linear),
            _ => linear / (1f + linear)
        };
    }

    private static float Aces(float linear)
    {
        const float a = 2.51f;
        const float b = 0.03f;
        const float c = 2.43f;
        const float d = 0.59f;
        const float e = 0.14f;
        return Math.Clamp((linear * (a * linear + b))
            / (linear * (c * linear + d) + e), 0f, 1f);
    }

    private static float Hable(float linear)
    {
        const float a = 0.15f;
        const float b = 0.50f;
        const float c = 0.10f;
        const float d = 0.20f;
        const float e = 0.02f;
        const float f = 0.30f;
        const float white = 11.2f;

        static float Curve(float value, float a, float b, float c, float d, float e, float f)
            => ((value * (a * value + c * b) + d * e)
                / (value * (a * value + b) + d * f)) - e / f;

        float whiteScale = 1f / Curve(white, a, b, c, d, e, f);
        return Math.Clamp(Curve(linear, a, b, c, d, e, f) * whiteScale, 0f, 1f);
    }
}
