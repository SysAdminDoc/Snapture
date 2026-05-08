using System.Drawing;

namespace Snapture.Capture;

public sealed record CaptureResult(
    Bitmap Bitmap,
    Rectangle SourceBounds,
    DateTime CapturedAtUtc,
    string Source,
    nint? SourceWindow = null);
