namespace Snapture.App.Services;

internal sealed class DirtyRegionFrameFilter
{
    public bool ReportingEnabled { get; private set; }
    public int SkippedFrameCount { get; private set; }

    private bool _hasReferenceFrame;

    public void Reset(bool reportingEnabled)
    {
        ReportingEnabled = reportingEnabled;
        SkippedFrameCount = 0;
        _hasReferenceFrame = false;
    }

    public void ForceNextFrame()
    {
        _hasReferenceFrame = false;
    }

    public bool ShouldEncode(int? dirtyRegionCount)
    {
        if (!ReportingEnabled || dirtyRegionCount is null)
        {
            _hasReferenceFrame = true;
            return true;
        }

        if (!_hasReferenceFrame)
        {
            _hasReferenceFrame = true;
            return true;
        }

        if (dirtyRegionCount.Value == 0)
        {
            SkippedFrameCount++;
            return false;
        }

        return true;
    }
}
