using ReceiptRecognition.Core.Utils.Configuration;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Full recognized receipt aggregating positions, total, store, date, and metadata.
/// </summary>
public class RecognizedReceipt
{
    public List<RecognizedPosition> Positions { get; set; }
    public DateTime Timestamp { get; set; }
    public RecognizedTotal? Total { get; set; }
    public RecognizedTotalLabel? TotalLabel { get; set; }
    public RecognizedStore? Store { get; set; }
    public RecognizedPurchaseDate? PurchaseDate { get; set; }
    public RecognizedBounds? Bounds { get; set; }
    public List<IRecognizedEntity>? Entities { get; set; }

    public RecognizedReceipt(
        List<RecognizedPosition> positions,
        DateTime timestamp,
        RecognizedTotal? total = null,
        RecognizedTotalLabel? totalLabel = null,
        RecognizedStore? store = null,
        RecognizedPurchaseDate? purchaseDate = null,
        RecognizedBounds? bounds = null,
        List<IRecognizedEntity>? entities = null)
    {
        Positions = positions;
        Timestamp = timestamp;
        Total = total;
        TotalLabel = totalLabel;
        Store = store;
        PurchaseDate = purchaseDate;
        Bounds = bounds;
        Entities = entities;
    }

    public static RecognizedReceipt Empty() =>
        new([], DateTime.Now, entities: []);

    public static RecognizedReceipt FromJson(Dictionary<string, object?> json)
    {
        var rawPositions = json.GetValueOrDefault("positions") as IEnumerable<object?>;
        var positions = (rawPositions ?? [])
            .OfType<Dictionary<string, object?>>()
            .Select(RecognizedPosition.FromJson)
            .ToList();

        RecognizedTotal? total = null;
        if (json.GetValueOrDefault("total") is Dictionary<string, object?> rawTotal)
        {
            var v = rawTotal.GetValueOrDefault("value");
            var doubleValue = v is double d
                ? d
                : double.TryParse(v?.ToString() ?? "", out var dv) ? dv : 0;
            total = new RecognizedTotal(doubleValue, new ReceiptTextLine());
        }

        RecognizedPurchaseDate? purchaseDate = null;
        var rawPd = json.GetValueOrDefault("purchase_date");
        if (rawPd != null)
        {
            // TODO: Wire up ReceiptFormatter
            var parsedDate = ReceiptFormatter.ParseNumericYMD(rawPd.ToString() ?? "") ?? DateTime.Now;
            purchaseDate = new RecognizedPurchaseDate(parsedDate, new ReceiptTextLine());
        }

        var ts = DateTime.TryParse(json.GetValueOrDefault("timestamp")?.ToString() ?? "", out var tsv)
            ? tsv
            : DateTime.Now;

        return new RecognizedReceipt(positions, ts, total: total, purchaseDate: purchaseDate, entities: []);
    }

    public RecognizedReceipt CopyWith(
        RecognizedStore? store = null, RecognizedTotal? total = null,
        RecognizedTotalLabel? totalLabel = null, RecognizedPurchaseDate? purchaseDate = null,
        RecognizedBounds? bounds = null, List<IRecognizedEntity>? entities = null,
        List<RecognizedPosition>? positions = null, DateTime? timestamp = null)
    {
        return new RecognizedReceipt(
            positions ?? Positions,
            timestamp ?? Timestamp,
            total: total ?? Total,
            totalLabel: totalLabel ?? TotalLabel,
            store: store ?? Store,
            purchaseDate: purchaseDate ?? PurchaseDate,
            bounds: bounds ?? Bounds,
            entities: entities ?? Entities);
    }

    [Obsolete("Use Store instead")]
    public RecognizedCompany? Company =>
        Store != null ? new RecognizedCompany(Store.Value, Store.Line) : null;

    [Obsolete("Use Total instead")]
    public RecognizedSum? Sum =>
        Total != null ? new RecognizedSum(Total.Value, Total.Line) : null;

    [Obsolete("Use TotalLabel instead")]
    public RecognizedSumLabel? SumLabel =>
        TotalLabel != null ? new RecognizedSumLabel(TotalLabel.Value, TotalLabel.Line) : null;

    public string Fingerprint
    {
        get
        {
            var positionsHash = string.Join(",",
                Positions.Select(p => $"{p.Product.FormattedValue}:{p.Price.Value}"));
            var totalValue = Total?.FormattedValue ?? "";
            return $"{positionsHash}|{totalValue}";
        }
    }

    public CalculatedTotal CalculatedTotal => new(
        Positions.Sum(p => (int)Math.Round(p.Price.Value * 100)) / 100.0);

    [Obsolete("Use CalculatedTotal instead")]
    public CalculatedSum CalculatedSum => new(CalculatedTotal.Value);

    public bool IsValid =>
        CalculatedTotal.FormattedValue == Total?.FormattedValue && CalculatedTotal.Value > 0.0;

    public bool IsNotEmpty => !IsEmpty;

    public bool IsEmpty =>
        (Entities == null || Entities.Count == 0)
        && Positions.Count == 0
        && Total == null
        && Store == null
        && PurchaseDate == null
        && (Bounds == null || Bounds.BoundingBox == default(BoundingBox));

    // TODO: Wire up ReceiptRuntime
    public bool IsConfirmed
    {
        get
        {
            var t = ReceiptRuntime.Tuning;
            var half = t.OptimizerMaxCacheSize / 2;
            var minSize = half < 4 ? 4 : half > 8 ? 8 : half;

            var confThr = t.OptimizerConfidenceThreshold;
            var stabThr = t.OptimizerStabilityThreshold;

            var minPassing = Positions.Count(p =>
            {
                var enoughMembers = (p.Group?.Members.Count ?? 0) >= minSize / 2;
                var enoughStability = p.Stability >= stabThr / 2;
                var enoughConfidence = p.ConfidenceScore >= confThr / 2;
                var enoughUnits = p.Product.AlternativeUnits.Count == 0
                                  || p.Product.AlternativeUnits.Count >= minSize / 2;
                return enoughMembers && enoughStability && enoughConfidence && enoughUnits;
            });

            var passing = Positions.Count(p =>
            {
                var enoughMembers = (p.Group?.Members.Count ?? 0) >= minSize;
                var enoughStability = p.Stability >= stabThr;
                var enoughConfidence = p.ConfidenceScore >= confThr;
                var enoughUnits = p.Product.AlternativeUnits.Count == 0
                                  || p.Product.AlternativeUnits.Count >= minSize;
                return enoughMembers && enoughStability && enoughConfidence && enoughUnits;
            });

            var minNeed = Positions.Count;
            var part = Positions.Count / 4;
            var need = minNeed - part;
            return minPassing >= minNeed && passing >= need;
        }
    }
}

/// <summary>
/// Progress snapshot of an ongoing receipt scan.
/// </summary>
public sealed class RecognizedScanProgress
{
    public List<RecognizedPosition> Positions { get; }
    public List<RecognizedPosition> AddedPositions { get; }
    public List<RecognizedPosition> UpdatedPositions { get; }
    public ReceiptValidationResult ValidationResult { get; }
    public int EstimatedPercentage { get; }
    public RecognizedReceipt MergedReceipt { get; }

    public RecognizedScanProgress(
        List<RecognizedPosition> positions,
        List<RecognizedPosition> addedPositions,
        List<RecognizedPosition> updatedPositions,
        ReceiptValidationResult validationResult,
        int estimatedPercentage,
        RecognizedReceipt mergedReceipt)
    {
        Positions = positions;
        AddedPositions = addedPositions;
        UpdatedPositions = updatedPositions;
        ValidationResult = validationResult;
        EstimatedPercentage = estimatedPercentage;
        MergedReceipt = mergedReceipt;
    }

    public static RecognizedScanProgress Empty() => new(
        positions: [],
        addedPositions: [],
        updatedPositions: [],
        validationResult: new ReceiptValidationResult(
            ReceiptCompleteness.Invalid, 0, ""),
        estimatedPercentage: 0,
        mergedReceipt: RecognizedReceipt.Empty());
}

/// <summary>
/// Completeness level of a recognized receipt.
/// </summary>
public enum ReceiptCompleteness
{
    Complete,
    NearlyComplete,
    Incomplete,
    Invalid
}

/// <summary>
/// Validation result for a recognized receipt.
/// </summary>
public class ReceiptValidationResult
{
    public ReceiptCompleteness Status { get; }
    public int MatchPercentage { get; }
    public string Message { get; }

    public ReceiptValidationResult(ReceiptCompleteness status, int matchPercentage, string message)
    {
        Status = status;
        MatchPercentage = matchPercentage;
        Message = message;
    }
}
