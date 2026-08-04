using SkiaSharp;

namespace Snapture.App.Editor;

internal static class CropMath
{
    public static SKRectI NormalizeAndSnap(SKRect selection, int canvasWidth, int canvasHeight,
        bool snapToEdges, float snapThreshold = 16f)
    {
        float left = Math.Clamp(Math.Min(selection.Left, selection.Right), 0, canvasWidth);
        float top = Math.Clamp(Math.Min(selection.Top, selection.Bottom), 0, canvasHeight);
        float right = Math.Clamp(Math.Max(selection.Left, selection.Right), 0, canvasWidth);
        float bottom = Math.Clamp(Math.Max(selection.Top, selection.Bottom), 0, canvasHeight);
        if (snapToEdges)
        {
            if (left <= snapThreshold) left = 0;
            if (top <= snapThreshold) top = 0;
            if (canvasWidth - right <= snapThreshold) right = canvasWidth;
            if (canvasHeight - bottom <= snapThreshold) bottom = canvasHeight;
        }

        int x = Math.Clamp((int)MathF.Floor(left), 0, canvasWidth);
        int y = Math.Clamp((int)MathF.Floor(top), 0, canvasHeight);
        int r = Math.Clamp((int)MathF.Ceiling(right), x, canvasWidth);
        int b = Math.Clamp((int)MathF.Ceiling(bottom), y, canvasHeight);
        return new SKRectI(x, y, r, b);
    }
}
