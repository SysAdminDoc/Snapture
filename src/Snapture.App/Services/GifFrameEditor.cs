using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ImageMagick;

namespace Snapture.App.Services;

internal readonly record struct GifFrameInfo(int Index, int DelayMs, bool IsDithered);

/// <summary>
/// Owns editable GIF frame copies. The recorder's capture buffers remain untouched so
/// cancelling the editor never destroys the in-memory recording.
/// </summary>
internal sealed class GifFrameEditor : IDisposable
{
    private static readonly int[,] Bayer4 =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 }
    };

    private readonly List<EditableFrame> _frames;
    private readonly GifLosslessSource? _losslessSource;
    private bool _disposed;

    public GifFrameEditor(IEnumerable<Bitmap> frames, int defaultDelayMs)
        : this(frames, defaultDelayMs, takeOwnership: false)
    {
    }

    internal GifFrameEditor(IEnumerable<Bitmap> frames, int defaultDelayMs, bool takeOwnership)
    {
        int delay = Math.Clamp(defaultDelayMs, 20, 10_000);
        _frames = frames.Select(frame => new EditableFrame(
            takeOwnership ? frame : new Bitmap(frame), delay)).ToList();
        if (_frames.Count == 0)
            throw new ArgumentException("At least one frame is required.", nameof(frames));
    }

    private GifFrameEditor(List<EditableFrame> frames, GifLosslessSource losslessSource)
    {
        _frames = frames;
        _losslessSource = losslessSource;
    }

    internal static GifFrameEditor LoadGif(string path)
    {
        var source = GifLosslessSource.Load(path);
        using var images = new MagickImageCollection(path);
        if (images.Count != source.Frames.Count)
        {
            throw new InvalidDataException(
                $"The GIF decoder found {images.Count} frames but the source contains {source.Frames.Count}.");
        }

        images.Coalesce();
        var frames = new List<EditableFrame>(images.Count);
        for (int index = 0; index < images.Count; index++)
        {
            using var stream = new MemoryStream();
            images[index].Write(stream, MagickFormat.Png);
            stream.Position = 0;
            using var decoded = new Bitmap(stream);
            frames.Add(new EditableFrame(
                new Bitmap(decoded),
                source.Frames[index].DelayMs,
                sourceFrameIndex: index));
        }

        return new GifFrameEditor(frames, source);
    }

    public int Count => _frames.Count;

    internal bool CanSaveLosslessly
        => _losslessSource is not null && _frames.All(frame => frame.CanPreserveSource);

    public GifFrameInfo GetInfo(int index)
    {
        ThrowIfDisposed();
        return _frames[index].Info(index);
    }

    public Bitmap CloneFrame(int index)
    {
        ThrowIfDisposed();
        return new Bitmap(_frames[index].Bitmap);
    }

    public void Delete(int index)
    {
        ThrowIfDisposed();
        if (_frames.Count == 1)
            throw new InvalidOperationException("A GIF must keep at least one frame.");

        _frames[index].Dispose();
        _frames.RemoveAt(index);
    }

    public void Duplicate(int index)
    {
        ThrowIfDisposed();
        var source = _frames[index];
        _frames.Insert(index + 1, new EditableFrame(
            new Bitmap(source.Bitmap),
            source.DelayMs,
            source.IsDithered));
    }

    public void SetDelay(int index, int delayMs)
    {
        ThrowIfDisposed();
        var frame = _frames[index];
        frame.DelayMs = Math.Clamp(delayMs, 20, 10_000);
        frame.DelayWasEdited = true;
    }

    public void ApplyDither(int index)
    {
        ThrowIfDisposed();
        var frame = _frames[index];
        using var source = frame.Bitmap;
        var dithered = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(dithered))
        {
            graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImageUnscaled(source, 0, 0);
        }

        for (int y = 0; y < dithered.Height; y++)
        {
            for (int x = 0; x < dithered.Width; x++)
            {
                Color pixel = dithered.GetPixel(x, y);
                int adjustment = (Bayer4[y & 3, x & 3] - 7) * 4;
                dithered.SetPixel(x, y, Color.FromArgb(
                    pixel.A,
                    Quantize(pixel.R, adjustment),
                    Quantize(pixel.G, adjustment),
                    Quantize(pixel.B, adjustment)));
            }
        }

        frame.Bitmap = dithered;
        frame.IsDithered = true;
    }

    public void SaveAs(string outputPath)
        => SaveAs(outputPath, AnimatedImageFormat.Gif);

    internal void SaveAs(string outputPath, AnimatedImageFormat format)
    {
        ThrowIfDisposed();
        GifEncoder.Encode(
            outputPath,
            _frames.Select(frame => new GifFrameInput(frame.Bitmap, frame.DelayMs)),
            GifEncodingOptions.Default,
            format);
    }

    internal void SaveLossless(string outputPath)
    {
        ThrowIfDisposed();
        if (!CanSaveLosslessly)
        {
            throw new InvalidOperationException(
                "Lossless save is available only for imported GIFs with deletion-only edits.");
        }

        _losslessSource!.Save(
            outputPath,
            _frames.Select(frame => frame.SourceFrameIndex!.Value));
    }

    private static byte Quantize(byte value, int adjustment)
    {
        int adjusted = Math.Clamp(value + adjustment, 0, 255);
        return (byte)Math.Clamp(((adjusted + 16) / 32) * 32, 0, 255);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var frame in _frames)
            frame.Dispose();
        _frames.Clear();
    }

    private sealed class EditableFrame : IDisposable
    {
        public EditableFrame(
            Bitmap bitmap,
            int delayMs,
            bool isDithered = false,
            int? sourceFrameIndex = null)
        {
            Bitmap = bitmap;
            DelayMs = delayMs;
            IsDithered = isDithered;
            SourceFrameIndex = sourceFrameIndex;
        }

        public Bitmap Bitmap { get; set; }
        public int DelayMs { get; set; }
        public bool IsDithered { get; set; }
        public int? SourceFrameIndex { get; }
        public bool DelayWasEdited { get; set; }
        public bool CanPreserveSource
            => SourceFrameIndex.HasValue && !DelayWasEdited && !IsDithered;

        public GifFrameInfo Info(int index) => new(index, DelayMs, IsDithered);

        public void Dispose() => Bitmap.Dispose();
    }
}
