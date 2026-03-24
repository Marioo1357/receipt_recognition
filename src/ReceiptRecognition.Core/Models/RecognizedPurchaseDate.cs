namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized purchase date from a receipt.
/// </summary>
public sealed class RecognizedPurchaseDate : RecognizedEntity<DateTime>
{
    public RecognizedPurchaseDate(DateTime value, ReceiptTextLine line) : base(value, line) { }

    public RecognizedPurchaseDate CopyWith(DateTime? value = null, ReceiptTextLine? line = null) =>
        new(value ?? Value, line ?? Line);

    public DateTime? ParsedDateTime => Value;

    public override string Format(DateTime value) => value.ToString("O");
}
