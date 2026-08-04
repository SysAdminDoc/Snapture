namespace Snapture.App.Services;

internal static class PluginCompatibility
{
    public static bool TryValidate(
        string? minimum,
        string? maximum,
        Version hostVersion,
        out string reason)
    {
        reason = string.Empty;
        Version host = Normalize(hostVersion);
        Version? min = ParseConstraint(minimum, "minimum", out reason);
        if (reason.Length > 0) return false;
        Version? max = ParseConstraint(maximum, "maximum", out reason);
        if (reason.Length > 0) return false;

        if (min is not null && max is not null && min > max)
        {
            reason = $"minimum host version {min} is greater than maximum host version {max}";
            return false;
        }
        if (min is not null && host < min)
        {
            reason = $"requires Snapture {min} or newer (host is {host})";
            return false;
        }
        if (max is not null && host > max)
        {
            reason = $"supports Snapture through {max} (host is {host})";
            return false;
        }
        return true;
    }

    private static Version? ParseConstraint(string? text, string label, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(text)) return null;
        string normalized = text.Trim();
        if (normalized.StartsWith('v')) normalized = normalized[1..];
        if (!Version.TryParse(normalized, out var parsed))
        {
            reason = $"has an invalid {label} host version '{text}'";
            return null;
        }
        return Normalize(parsed);
    }

    private static Version Normalize(Version version) => new(
        version.Major,
        Math.Max(0, version.Minor),
        Math.Max(0, version.Build),
        Math.Max(0, version.Revision));
}
