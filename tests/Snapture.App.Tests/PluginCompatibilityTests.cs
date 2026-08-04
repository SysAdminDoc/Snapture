using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class PluginCompatibilityTests
{
    private static readonly Version Host = new(0, 6, 0, 0);

    [TestMethod]
    public void UnboundedAndInclusiveRangesAreAccepted()
    {
        Assert.IsTrue(PluginCompatibility.TryValidate(null, null, Host, out _));
        Assert.IsTrue(PluginCompatibility.TryValidate("v0.6.0", "0.7.0", Host, out _));
        Assert.IsTrue(PluginCompatibility.TryValidate(null, "0.6.0", Host, out _));
    }

    [TestMethod]
    public void HostOutsideDeclaredRangeIsRejectedWithReason()
    {
        Assert.IsFalse(PluginCompatibility.TryValidate("0.6.1", null, Host, out var minimumReason));
        StringAssert.Contains(minimumReason, "requires Snapture");

        Assert.IsFalse(PluginCompatibility.TryValidate(null, "0.5.9", Host, out var maximumReason));
        StringAssert.Contains(maximumReason, "supports Snapture through");
    }

    [TestMethod]
    public void InvalidAndReversedRangesAreRejected()
    {
        Assert.IsFalse(PluginCompatibility.TryValidate("not-a-version", null, Host, out var invalidReason));
        StringAssert.Contains(invalidReason, "invalid minimum");

        Assert.IsFalse(PluginCompatibility.TryValidate("0.7.0", "0.6.0", Host, out var reversedReason));
        StringAssert.Contains(reversedReason, "greater than maximum");
    }
}
