using Serilog.Core;
using Serilog.Events;

namespace Snapture.App.Services;

/// <summary>
/// Serilog destructuring policy that redacts PII-adjacent properties
/// (WindowTitle, ProcessPath, FilePath, AdapterIp) from log lines at
/// Information level and below. Debug/Verbose levels pass values through
/// so <c>--verbose</c> produces full diagnostics.
/// </summary>
public sealed class LogRedactionEnricher : ILogEventEnricher
{
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowTitle", "ProcessPath", "FilePath", "AdapterIp",
        "OutputFolder", "SourceApp"
    };

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Level <= LogEventLevel.Debug) return;

        var keys = logEvent.Properties.Keys.ToList();
        foreach (var key in keys)
        {
            if (RedactedProperties.Contains(key))
                logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue("[redacted]")));
        }
    }
}
