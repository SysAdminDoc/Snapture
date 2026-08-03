namespace Snapture.Capture;

public enum HdrToneMapOperator
{
    Reinhard,
    Aces,
    Hable
}

public static class HdrToneMapOperators
{
    public const string DefaultKey = "reinhard";

    public static HdrToneMapOperator Parse(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "aces" => HdrToneMapOperator.Aces,
            "hable" => HdrToneMapOperator.Hable,
            _ => HdrToneMapOperator.Reinhard
        };

    public static string ToKey(HdrToneMapOperator value)
        => value switch
        {
            HdrToneMapOperator.Aces => "aces",
            HdrToneMapOperator.Hable => "hable",
            _ => DefaultKey
        };

    public static string DisplayName(HdrToneMapOperator value)
        => value switch
        {
            HdrToneMapOperator.Aces => "ACES",
            HdrToneMapOperator.Hable => "Hable",
            _ => "Reinhard"
        };
}
