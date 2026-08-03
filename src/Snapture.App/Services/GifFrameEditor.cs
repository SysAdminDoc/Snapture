using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

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

    public int Count => _frames.Count;

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
        _frames.Insert(index + 1, new EditableFrame(new Bitmap(source.Bitmap), source.DelayMs, source.IsDithered));
    }

    public void SetDelay(int index, int delayMs)
    {
        ThrowIfDisposed();
        _frames[index].DelayMs = Math.Clamp(delayMs, 20, 10_000);
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
        public EditableFrame(Bitmap bitmap, int delayMs, bool isDithered = false)
        {
            Bitmap = bitmap;
            DelayMs = delayMs;
            IsDithered = isDithered;
        }

        public Bitmap Bitmap { get; set; }
        public int DelayMs { get; set; }
        public bool IsDithered { get; set; }

        public GifFrameInfo Info(int index) => new(index, DelayMs, IsDithered);

        public void Dispose() => Bitmap.Dispose();
    }
}
