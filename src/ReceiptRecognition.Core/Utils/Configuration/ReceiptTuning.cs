namespace ReceiptRecognition.Core.Utils.Configuration;

/// <summary>
/// Numeric/string tuning knobs for parsing/optimization.
/// </summary>
public sealed class ReceiptTuning
{
    /// <summary>Total tolerance tight and more precise below 1 cent.</summary>
    public double OptimizerTotalTolerance { get; }

    /// <summary>Vertical tolerance (in pixels) for comparing bounding box alignment.</summary>
    public int OptimizerVerticalTolerance { get; }

    /// <summary>Max identical iterations before stopping optimization.</summary>
    public int OptimizerLoopThreshold { get; }

    /// <summary>Default maximum cache size for normal precision mode.</summary>
    public int OptimizerMaxCacheSize { get; }

    /// <summary>Minimum confidence score (0–100) for groups to be stable.</summary>
    public int OptimizerConfidenceThreshold { get; }

    /// <summary>Minimum stability score (0–100) required for groups.</summary>
    public int OptimizerStabilityThreshold { get; }

    /// <summary>EWMA smoothing factor for vertical order learning.</summary>
    public double OptimizerEwmaAlpha { get; }

    /// <summary>Pairwise count threshold before halving to avoid overflow.</summary>
    public int OptimizerAboveCountDecayThreshold { get; }

    /// <summary>Weight factor influencing product name stability during optimization.</summary>
    public int OptimizerProductWeight { get; }

    /// <summary>Weight factor influencing price consistency during optimization.</summary>
    public int OptimizerPriceWeight { get; }

    /// <summary>Default name used for unrecognized products.</summary>
    public string OptimizerUnrecognizedProductName { get; }

    private ReceiptTuning(
        double optimizerTotalTolerance,
        int optimizerVerticalTolerance,
        int optimizerLoopThreshold,
        int optimizerMaxCacheSize,
        int optimizerConfidenceThreshold,
        int optimizerStabilityThreshold,
        double optimizerEwmaAlpha,
        int optimizerAboveCountDecayThreshold,
        int optimizerProductWeight,
        int optimizerPriceWeight,
        string optimizerUnrecognizedProductName)
    {
        OptimizerTotalTolerance = optimizerTotalTolerance;
        OptimizerVerticalTolerance = optimizerVerticalTolerance;
        OptimizerLoopThreshold = optimizerLoopThreshold;
        OptimizerMaxCacheSize = optimizerMaxCacheSize;
        OptimizerConfidenceThreshold = optimizerConfidenceThreshold;
        OptimizerStabilityThreshold = optimizerStabilityThreshold;
        OptimizerEwmaAlpha = optimizerEwmaAlpha;
        OptimizerAboveCountDecayThreshold = optimizerAboveCountDecayThreshold;
        OptimizerProductWeight = optimizerProductWeight;
        OptimizerPriceWeight = optimizerPriceWeight;
        OptimizerUnrecognizedProductName = optimizerUnrecognizedProductName;
    }

    /// <summary>
    /// Public factory with optional named params. Omitted fields fall back to defaults.
    /// </summary>
    public static ReceiptTuning Create(
        double? optimizerTotalTolerance = null,
        double? optimizerEwmaAlpha = null,
        int? optimizerVerticalTolerance = null,
        int? optimizerLoopThreshold = null,
        int? optimizerMaxCacheSize = null,
        int? optimizerConfidenceThreshold = null,
        int? optimizerStabilityThreshold = null,
        int? optimizerAboveCountDecayThreshold = null,
        int? optimizerProductWeight = null,
        int? optimizerPriceWeight = null,
        string? optimizerUnrecognizedProductName = null)
    {
        var def = FromJsonLike(
            ReceiptDefaults.KReceiptDefaultOptions["tuning"] as Dictionary<string, object>);
        return new ReceiptTuning(
            optimizerTotalTolerance ?? def.OptimizerTotalTolerance,
            optimizerVerticalTolerance ?? def.OptimizerVerticalTolerance,
            optimizerLoopThreshold ?? def.OptimizerLoopThreshold,
            optimizerMaxCacheSize ?? def.OptimizerMaxCacheSize,
            optimizerConfidenceThreshold ?? def.OptimizerConfidenceThreshold,
            optimizerStabilityThreshold ?? def.OptimizerStabilityThreshold,
            optimizerEwmaAlpha ?? def.OptimizerEwmaAlpha,
            optimizerAboveCountDecayThreshold ?? def.OptimizerAboveCountDecayThreshold,
            optimizerProductWeight ?? def.OptimizerProductWeight,
            optimizerPriceWeight ?? def.OptimizerPriceWeight,
            optimizerUnrecognizedProductName ?? def.OptimizerUnrecognizedProductName);
    }

    /// <summary>Builds tuning strictly from the defaults table.</summary>
    public static ReceiptTuning Defaults() =>
        FromJsonLike(ReceiptDefaults.KReceiptDefaultOptions["tuning"] as Dictionary<string, object>);

    /// <summary>
    /// Builds tuning from a JSON-like dictionary, falling back per-field to defaults.
    /// </summary>
    public static ReceiptTuning FromJsonLike(Dictionary<string, object>? json)
    {
        var d = ReceiptDefaults.KReceiptDefaultOptions["tuning"] as Dictionary<string, object>
                ?? new Dictionary<string, object>();
        var u = json ?? new Dictionary<string, object>();

        var merged = new Dictionary<string, object>(d);
        foreach (var kv in u)
            merged[kv.Key] = kv.Value;

        double NumDouble(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (merged.TryGetValue(k, out var v))
                {
                    if (v is double dv) return dv;
                    if (v is int iv) return iv;
                    if (v is float fv) return fv;
                    if (v is long lv) return lv;
                }
            }
            throw new InvalidOperationException($"Missing tuning value for keys: {string.Join(", ", keys)}");
        }

        int NumInt(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (merged.TryGetValue(k, out var v))
                {
                    if (v is int iv) return iv;
                    if (v is double dv) return (int)dv;
                    if (v is float fv) return (int)fv;
                    if (v is long lv) return (int)lv;
                }
            }
            throw new InvalidOperationException($"Missing tuning value for keys: {string.Join(", ", keys)}");
        }

        string Str(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (merged.TryGetValue(k, out var v) && v is string s && !string.IsNullOrWhiteSpace(s))
                    return s;
            }
            throw new InvalidOperationException($"Missing tuning value for keys: {string.Join(", ", keys)}");
        }

        return new ReceiptTuning(
            optimizerTotalTolerance: NumDouble("optimizerTotalTolerance"),
            optimizerVerticalTolerance: NumInt("optimizerVerticalTolerance"),
            optimizerLoopThreshold: NumInt("optimizerLoopThreshold"),
            optimizerMaxCacheSize: NumInt("optimizerMaxCacheSize"),
            optimizerConfidenceThreshold: NumInt("optimizerConfidenceThreshold"),
            optimizerStabilityThreshold: NumInt("optimizerStabilityThreshold"),
            optimizerEwmaAlpha: NumDouble("optimizerEwmaAlpha"),
            optimizerAboveCountDecayThreshold: NumInt("optimizerAboveCountDecayThreshold"),
            optimizerProductWeight: NumInt("optimizerProductWeight"),
            optimizerPriceWeight: NumInt("optimizerPriceWeight"),
            optimizerUnrecognizedProductName: Str("optimizerUnrecognizedProductName"));
    }

    /// <summary>Serializes tuning to a JSON-like dictionary.</summary>
    public Dictionary<string, object> ToJsonLike() => new()
    {
        ["optimizerTotalTolerance"] = OptimizerTotalTolerance,
        ["optimizerEwmaAlpha"] = OptimizerEwmaAlpha,
        ["optimizerVerticalTolerance"] = OptimizerVerticalTolerance,
        ["optimizerLoopThreshold"] = OptimizerLoopThreshold,
        ["optimizerMaxCacheSize"] = OptimizerMaxCacheSize,
        ["optimizerConfidenceThreshold"] = OptimizerConfidenceThreshold,
        ["optimizerStabilityThreshold"] = OptimizerStabilityThreshold,
        ["optimizerAboveCountDecayThreshold"] = OptimizerAboveCountDecayThreshold,
        ["optimizerProductWeight"] = OptimizerProductWeight,
        ["optimizerPriceWeight"] = OptimizerPriceWeight,
        ["optimizerUnrecognizedProductName"] = OptimizerUnrecognizedProductName,
    };
}
