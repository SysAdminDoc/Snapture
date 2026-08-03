using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class ScrollingCaptureServiceTests
{
    [TestMethod]
    public void ChromiumWindowClass_UsesDefaultUiABackend()
    {
        Assert.IsTrue(ScrollingCaptureService.IsChromiumWindowClass("Chrome_WidgetWin_1"));
        Assert.IsTrue(ScrollingCaptureService.IsChromiumWindowClass("chrome_widgetwin_0"));
        Assert.IsFalse(ScrollingCaptureService.IsChromiumWindowClass("CabinetWClass"));
        Assert.IsFalse(ScrollingCaptureService.IsChromiumWindowClass(null));
    }
}
