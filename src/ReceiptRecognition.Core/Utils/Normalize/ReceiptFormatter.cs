namespace ReceiptRecognition.Core.Utils.Normalize;

/// <summary>
/// Placeholder for receipt text formatting utilities. Will be fully implemented later.
/// </summary>
public static class ReceiptFormatter
{
    // TODO: Implement actual formatting logic
    public static string Format(double value) => value.ToString("F2");

    public static string Trim(string value) => value.Trim();

    // TODO: Implement actual postfix text extraction
    public static string ToPostfixText(string text) => text;

    public static DateTime? ParseNumericYMD(object? raw)
    {
        if (raw == null) return null;
        return DateTime.TryParse(raw.ToString(), out var dt) ? dt : null;
    }
}
