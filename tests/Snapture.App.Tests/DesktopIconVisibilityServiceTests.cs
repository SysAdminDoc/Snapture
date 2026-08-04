using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class DesktopIconVisibilityServiceTests
{
    [TestMethod]
    public void HidingVisibleIconListReturnsRestoringScope()
    {
        var controller = new FakeDesktopIconController();

        var scope = DesktopIconVisibilityService.TryHide(controller);

        Assert.IsNotNull(scope);
        Assert.IsFalse(controller.Visible);
        Assert.AreEqual(1, controller.SetVisibleCalls);

        scope.Dispose();
        scope.Dispose();

        Assert.IsTrue(controller.Visible);
        Assert.AreEqual(2, controller.SetVisibleCalls);
    }

    [TestMethod]
    public void AlreadyHiddenIconListIsLeftAlone()
    {
        var controller = new FakeDesktopIconController { Visible = false };

        var scope = DesktopIconVisibilityService.TryHide(controller);

        Assert.IsNull(scope);
        Assert.IsFalse(controller.Visible);
        Assert.AreEqual(0, controller.SetVisibleCalls);
    }

    [TestMethod]
    public void FailedHideRestoresIconListAndDoesNotReturnScope()
    {
        var controller = new FakeDesktopIconController { ThrowWhenHiding = true };

        var scope = DesktopIconVisibilityService.TryHide(controller);

        Assert.IsNull(scope);
        Assert.IsTrue(controller.Visible);
        Assert.AreEqual(2, controller.SetVisibleCalls);
    }

    [TestMethod]
    public void VisibilityThatDoesNotChangeIsRestoredAndDoesNotReturnScope()
    {
        var controller = new FakeDesktopIconController { IgnoreHide = true };

        var scope = DesktopIconVisibilityService.TryHide(controller);

        Assert.IsNull(scope);
        Assert.IsTrue(controller.Visible);
        Assert.AreEqual(2, controller.SetVisibleCalls);
    }

    [TestMethod]
    public void MissingIconListDoesNotAttemptToChangeVisibility()
    {
        var controller = new FakeDesktopIconController { WindowExists = false };

        var scope = DesktopIconVisibilityService.TryHide(controller);

        Assert.IsNull(scope);
        Assert.AreEqual(0, controller.SetVisibleCalls);
    }

    private sealed class FakeDesktopIconController : DesktopIconVisibilityService.IDesktopIconController
    {
        public bool Visible { get; set; } = true;
        public bool WindowExists { get; set; } = true;
        public bool ThrowWhenHiding { get; set; }
        public bool IgnoreHide { get; set; }
        public int SetVisibleCalls { get; private set; }

        public nint FindDesktopIconList() => new(42);

        public bool IsWindow(nint handle) => WindowExists;

        public bool IsVisible(nint handle) => Visible;

        public void SetVisible(nint handle, bool visible)
        {
            SetVisibleCalls++;
            if (!visible && ThrowWhenHiding)
                throw new InvalidOperationException("simulated shell failure");
            if (!visible && IgnoreHide)
                return;
            Visible = visible;
        }
    }
}
