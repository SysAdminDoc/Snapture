using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkiaSharp;

namespace Snapture.App.Editor;

/// <summary>
/// Vector annotation document. Background pixels live in <see cref="Background"/>; every
/// shape on top stays editable until the user explicitly flattens on raster export.
/// </summary>
public sealed class AnnotationDocument
{
    public ObservableCollection<Shape> Shapes { get; } = new();

    /// <summary>Pixel-baked background (the original capture or an opened image).</summary>
    public SKBitmap Background { get; set; }

    /// <summary>Image-space size in pixels.</summary>
    public int Width => Background.Width;
    public int Height => Background.Height;

    public AnnotationDocument(SKBitmap background)
    {
        Background = background ?? throw new ArgumentNullException(nameof(background));
    }

    public void ReplaceBackground(SKBitmap newBackground)
    {
        var old = Background;
        Background = newBackground ?? throw new ArgumentNullException(nameof(newBackground));
        old.Dispose();
    }

    public void Render(SKCanvas canvas, bool flattenForExport)
    {
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(Background, 0, 0);
        foreach (var shape in Shapes)
        {
            shape.Render(canvas, this);
            shape.RenderCategoryTag(canvas, Width, Height);
        }
    }

    /// <summary>Flatten to a 32bpp BGRA SKBitmap. Caller owns the result.</summary>
    public SKBitmap RenderToBitmap()
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        using var canvas = new SKCanvas(bmp);
        Render(canvas, flattenForExport: true);
        canvas.Flush();
        return bmp;
    }

    public string SerializeShapes()
    {
        return JsonSerializer.Serialize(Shapes, ShapeJsonOptions);
    }

    public void DeserializeShapes(string json)
    {
        Shapes.Clear();
        if (string.IsNullOrWhiteSpace(json)) return;
        var loaded = JsonSerializer.Deserialize<List<Shape>>(json, ShapeJsonOptions);
        if (loaded is null) return;
        foreach (var s in loaded) Shapes.Add(s);
    }

    public static readonly JsonSerializerOptions ShapeJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
