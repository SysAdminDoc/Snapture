using System.Collections;
using Windows.Graphics.Capture;

namespace Snapture.App.Services;

internal static class DirtyRegionInterop
{
    public static int? TryGetDirtyRegionCount(Direct3D11CaptureFrame frame)
    {
        try
        {
            var prop = frame.GetType().GetProperty("DirtyRegions");
            if (prop?.GetValue(frame) is not IEnumerable regions)
                return null;

            int count = 0;
            foreach (var _ in regions)
                count++;

            return count;
        }
        catch
        {
            return null;
        }
    }
}
