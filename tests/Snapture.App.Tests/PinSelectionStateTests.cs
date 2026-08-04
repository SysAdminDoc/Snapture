using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PinSelectionStateTests
{
    [TestMethod]
    public void ToggleAndSelectOnlyMaintainExpectedSelection()
    {
        var first = new object();
        var second = new object();
        var state = new PinSelectionState<object>();

        Assert.IsTrue(state.Toggle(first));
        Assert.IsFalse(state.Toggle(first));
        Assert.AreEqual(0, state.Count);

        state.Toggle(first);
        state.Toggle(second);
        state.SelectOnly(second);

        Assert.AreEqual(1, state.Count);
        Assert.IsTrue(state.Contains(second));
        Assert.IsFalse(state.Contains(first));
    }

    [TestMethod]
    public void SelectAllAndRemoveKeepStateInSync()
    {
        var first = new object();
        var second = new object();
        var state = new PinSelectionState<object>();

        state.SelectAll(new[] { first, second });
        state.Remove(first);

        Assert.AreEqual(1, state.Count);
        Assert.IsFalse(state.Contains(first));
        Assert.IsTrue(state.Contains(second));

        state.Clear();
        Assert.AreEqual(0, state.Count);
    }

    [TestMethod]
    public void TargetsUseTheSelectedGroupOnlyWhenRequestedPinIsSelected()
    {
        var first = new object();
        var second = new object();
        var third = new object();
        var state = new PinSelectionState<object>();

        state.SelectAll(new[] { first, second });

        CollectionAssert.AreEquivalent(new[] { first, second }, state.TargetsFor(first).ToArray());
        CollectionAssert.AreEqual(new[] { third }, state.TargetsFor(third).ToArray());
    }
}
