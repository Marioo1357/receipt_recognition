namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized store name from a receipt.
/// </summary>
public class RecognizedStore : RecognizedEntity<string>
{
    public RecognizedStore(string value, ReceiptTextLine line) : base(value, line) { }

    public RecognizedStore CopyWith(string? value = null, ReceiptTextLine? line = null) =>
        new(value ?? Value, line ?? Line);

    public override string Format(string value) => value.ToUpperInvariant();
}

/// <summary>
/// Recognized company name from a receipt (alias for store).
/// </summary>
public sealed class RecognizedCompany : RecognizedStore
{
    public RecognizedCompany(string value, ReceiptTextLine line) : base(value, line) { }
}
