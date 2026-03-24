namespace ReceiptRecognition.Core.Utils.Configuration;

/// <summary>
/// Placeholder for runtime configuration. Will be fully implemented later.
/// </summary>
public static class ReceiptRuntime
{
    public static ReceiptTuning Tuning { get; } = new();
}

/// <summary>
/// Placeholder tuning parameters.
/// </summary>
public class ReceiptTuning
{
    // TODO: Wire up actual tuning parameters
    public int OptimizerProductWeight { get; set; } = 1;
    public int OptimizerPriceWeight { get; set; } = 1;
    public int OptimizerMaxCacheSize { get; set; } = 16;
    public int OptimizerConfidenceThreshold { get; set; } = 60;
    public int OptimizerStabilityThreshold { get; set; } = 60;
}
