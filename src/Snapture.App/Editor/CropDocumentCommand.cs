using SkiaSharp;

namespace Snapture.App.Editor;

public sealed class CropDocumentCommand : AnnotationCommand
{
    private readonly SKBitmap _originalBackground;
    private readonly Shape[] _originalShapes;
    private readonly SKRectI _crop;

    public CropDocumentCommand(AnnotationDocument document, SKRectI crop)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (crop.Width < 1 || crop.Height < 1)
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop bounds must have positive dimensions.");
        if (crop.Left < 0 || crop.Top < 0 || crop.Right > document.Width || crop.Bottom > document.Height)
            throw new ArgumentOutOfRangeException(nameof(crop), "Crop bounds must fit within the document.");

        _originalBackground = CopyBitmap(document.Background);
        _originalShapes = document.Shapes.Select(shape => shape.Clone()).ToArray();
        _crop = crop;
    }

    public override void Apply(AnnotationDocument document)
    {
        document.ReplaceBackground(CropBitmap(_originalBackground, _crop));
        document.Shapes.Clear();
        foreach (var shape in CroppedShapes())
            document.Shapes.Add(shape);
    }

    public override void Revert(AnnotationDocument document)
    {
        document.ReplaceBackground(CopyBitmap(_originalBackground));
        document.Shapes.Clear();
        foreach (var shape in _originalShapes.Select(shape => shape.Clone()))
            document.Shapes.Add(shape);
    }

    private IEnumerable<Shape> CroppedShapes()
    {
        var cropBounds = new SKRect(_crop.Left, _crop.Top, _crop.Right, _crop.Bottom);
        foreach (var source in _originalShapes)
        {
            if (!Intersects(source.GetBounds(), cropBounds))
                continue;

            var shape = source.Clone();
            shape.Offset(-_crop.Left, -_crop.Top);
            yield return shape;
        }
    }

    private static bool Intersects(SKRect first, SKRect second) =>
        first.Right > second.Left && first.Left < second.Right &&
        first.Bottom > second.Top && first.Top < second.Bottom;

    private static SKBitmap CropBitmap(SKBitmap source, SKRectI crop)
    {
        var result = new SKBitmap(new SKImageInfo(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source,
            new SKRect(crop.Left, crop.Top, crop.Right, crop.Bottom),
            new SKRect(0, 0, crop.Width, crop.Height));
        return result;
    }

    private static SKBitmap CopyBitmap(SKBitmap source)
    {
        var result = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
