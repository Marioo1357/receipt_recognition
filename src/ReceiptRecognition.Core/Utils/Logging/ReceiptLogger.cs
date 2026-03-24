using System.Diagnostics;
using System.Text.Json;
using ReceiptRecognition.Core.Models;

namespace ReceiptRecognition.Core.Utils.Logging;

/// <summary>
/// Centralized logger for receipt recognition.
///
/// Provides structured JSON logs and human-readable summaries of optimized
/// receipts. Logging is only active in debug mode when KRecogVerbose is true.
/// Includes helpers for generating compact keys for positions and groups.
/// </summary>
public static class ReceiptLogger
{
    /// <summary>Global toggle to enable or disable verbose recognition logs.</summary>
    public static bool KRecogVerbose { get; set; }

    /// <summary>
    /// Writes a structured one-liner log entry if verbose logging is enabled.
    /// Logs are JSON-encoded for easy parsing and are only printed in debug mode.
    /// </summary>
    [Conditional("DEBUG")]
    public static void Log(string cat, Dictionary<string, object?> data)
    {
        if (!KRecogVerbose) return;
        Debug.WriteLine($"\U0001f9fe[{cat}] {JsonSerializer.Serialize(data)}");
    }

    /// <summary>
    /// Generates a compact identifier string for a RecognizedPosition,
    /// including product text, price, and timestamp.
    /// </summary>
    public static string PosKey(RecognizedPosition p) =>
        $"{p.Product.FormattedValue}|{p.Price.Value:F2}|{p.Timestamp.Ticks}";

    /// <summary>
    /// Generates a compact identifier string for a RecognizedGroup,
    /// including its hash and current member count.
    /// </summary>
    public static string GrpKey(RecognizedGroup g) =>
        $"G{g.GetHashCode():x}({g.Members.Count})";

    /// <summary>
    /// Logs a detailed summary of the receipt and optional validation result.
    /// Prints store, positions, calculated sum, detected sum, and final sum label for debugging.
    /// </summary>
    [Conditional("DEBUG")]
    public static void LogReceipt(
        RecognizedReceipt receipt,
        ReceiptValidationResult? validation = null)
    {
        if (!receipt.IsNotEmpty) return;

        if (validation != null)
        {
            Debug.WriteLine($"✅ Validation status: {validation.Status}");
            Debug.WriteLine($"💬 Message: {validation.Message}");
        }

        Debug.WriteLine($"🏪 Supermarket: {receipt.Store?.FormattedValue ?? "N/A"}");
        Debug.WriteLine($"📅 Purchase datetime: {receipt.PurchaseDate?.FormattedValue ?? "N/A"}");

        const int padFullWidth = 40;
        const int padHalfWidth = padFullWidth / 2;
        const int padQuarterWidth = padFullWidth / 4;

        foreach (var position in receipt.Positions)
        {
            var product = position.Product.NormalizedText;
            var price = position.Price.FormattedValue;
            var confidence = position.ConfidenceScore;
            var stability = position.Stability;
            var distribution = string.Join(", ",
                position.Product.AlternativeTextPercentages
                    .Take(3)
                    .Select(e => $"{e.Key} {e.Value}%"));

            Debug.WriteLine(
                ("🛍️  " + product).PadRight(padFullWidth) +
                ("💰  " + price).PadRight(padQuarterWidth) +
                ("🗄  " + position.Product.ProductGroup).PadRight(padQuarterWidth) +
                ("🏷️  " + position.Product.Unit.Quantity.FormattedValue + " × " + position.Product.Unit.Price.FormattedValue).PadRight(padHalfWidth) +
                ("📈  Conf: " + confidence + " %").PadRight(padHalfWidth) +
                ("⚖️  Stab: " + stability + " %").PadRight(padHalfWidth) +
                ("📊  Dist: [" + distribution + "]").PadRight(padHalfWidth));
        }

        Debug.WriteLine($"🧮 Calculated total: {receipt.CalculatedTotal.FormattedValue}");
        Debug.WriteLine($"🧾 Total in receipt: {receipt.Total?.FormattedValue}");
        Debug.WriteLine($"📌 Optimizer final total label: {receipt.TotalLabel?.FormattedValue}");
    }
}
