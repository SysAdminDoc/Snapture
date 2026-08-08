using Snapture.App.Services;

namespace Snapture.App.Tests;

[TestClass]
public sealed class OutboundDataFlowAuditTests
{
    [TestMethod]
    public void InventoryContainsEveryKnownBoundaryWithCompleteMetadata()
    {
        var report = OutboundDataFlowAudit.Audit();

        Assert.IsTrue(report.IsComplete, string.Join(Environment.NewLine, report.Issues));
        CollectionAssert.AreEquivalent(
            OutboundDataFlowAudit.KnownBoundaryIds.ToArray(),
            OutboundDataFlowAudit.Entries.Select(entry => entry.Id).ToArray());
        Assert.HasCount(OutboundDataFlowAudit.KnownBoundaryIds.Count, OutboundDataFlowAudit.Entries);
    }

    [TestMethod]
    public void SensitiveHeadersTokensAndQueryValuesAreRedacted()
    {
        string redacted = OutboundDataFlowAudit.RedactSensitive(
            "Authorization: Bearer abc123 x-api-key=secret-value https://example.test/upload?token=query-secret");

        StringAssert.Contains(redacted, "Authorization: [redacted]");
        StringAssert.Contains(redacted, "x-api-key: [redacted]");
        StringAssert.Contains(redacted, "?token=[redacted]");
        Assert.IsFalse(redacted.Contains("abc123", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("secret-value", StringComparison.Ordinal));
        Assert.IsFalse(redacted.Contains("query-secret", StringComparison.Ordinal));
    }

    [TestMethod]
    public void DisplayFormattingKeepsSensitiveSourceTextBounded()
    {
        string value = OutboundDataFlowAudit.TrimForDisplay(
            "window token=secret-value " + new string('x', 200),
            40);

        Assert.IsLessThanOrEqualTo(41, value.Length);
        Assert.IsFalse(value.Contains("secret-value", StringComparison.Ordinal));
        StringAssert.EndsWith(value, "…");
    }
}
