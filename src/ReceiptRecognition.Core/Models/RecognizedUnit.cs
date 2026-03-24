using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized unit price from a receipt line.
/// </summary>
public sealed class RecognizedUnitPrice : RecognizedEntity<double>
{
    public RecognizedUnitPrice(double value, ReceiptTextLine line) : base(value, line) { }

    // TODO: Wire up ReceiptFormatter
    public override string Format(double value) => ReceiptFormatter.Format(value);
}

/// <summary>
/// Recognized unit quantity from a receipt line.
/// </summary>
public sealed class RecognizedUnitQuantity : RecognizedEntity<int>
{
    public RecognizedUnitQuantity(int value, ReceiptTextLine line) : base(value, line) { }

    public override string Format(int value) => value.ToString();
}

/// <summary>
/// Combines a quantity and a unit price for a receipt line item.
/// </summary>
public sealed class RecognizedUnit
{
    public RecognizedUnitQuantity Quantity { get; set; }
    public RecognizedUnitPrice Price { get; set; }

    public RecognizedUnit(RecognizedUnitQuantity quantity, RecognizedUnitPrice price)
    {
        Quantity = quantity;
        Price = price;
    }

    public static RecognizedUnit FromNumbers(int quantity, double price, ReceiptTextLine line) =>
        new(
            new RecognizedUnitQuantity(quantity, line),
            new RecognizedUnitPrice(price, line));
}
