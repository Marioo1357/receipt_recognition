using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized price value from a receipt line.
/// </summary>
public sealed class RecognizedPrice : RecognizedEntity<double>
{
    public Confidence? Confidence { get; set; }
    public RecognizedPosition? Position { get; set; }

    public RecognizedPrice(double value, ReceiptTextLine line,
        Confidence? confidence = null, RecognizedPosition? position = null)
        : base(value, line)
    {
        Confidence = confidence;
        Position = position;
    }

    public static RecognizedPrice FromJson(Dictionary<string, object?> json)
    {
        var rawValue = json.GetValueOrDefault("value");
        var parsedValue = rawValue is double d
            ? d
            : double.TryParse(rawValue?.ToString() ?? "0", out var dv) ? dv : 0;

        var rawConf = json.GetValueOrDefault("confidence");
        var confValue = rawConf is int i
            ? i
            : int.TryParse(rawConf?.ToString() ?? "0", out var iv) ? iv : 0;

        return new RecognizedPrice(parsedValue, new ReceiptTextLine(),
            confidence: new Confidence(confValue));
    }

    public RecognizedPrice CopyWith(
        double? value = null, ReceiptTextLine? line = null,
        Confidence? confidence = null, RecognizedPosition? position = null)
    {
        return new RecognizedPrice(
            value ?? Value, line ?? Line,
            confidence: confidence ?? Confidence,
            position: position ?? Position);
    }

    // TODO: Wire up ReceiptFormatter
    public override string Format(double value) => ReceiptFormatter.Format(value);
}

/// <summary>
/// Recognized amount (quantity × unit price) from a receipt line.
/// </summary>
public sealed class RecognizedAmount : RecognizedEntity<double>
{
    public RecognizedAmount(double value, ReceiptTextLine line) : base(value, line) { }

    // TODO: Wire up ReceiptFormatter
    public override string Format(double value) => ReceiptFormatter.Format(value);
}

/// <summary>
/// Recognized total value from a receipt.
/// </summary>
public class RecognizedTotal : RecognizedEntity<double>
{
    public RecognizedTotal(double value, ReceiptTextLine line) : base(value, line) { }

    public RecognizedTotal CopyWith(double? value = null, ReceiptTextLine? line = null) =>
        new(value ?? Value, line ?? Line);

    // TODO: Wire up ReceiptFormatter
    public override string Format(double value) => ReceiptFormatter.Format(value);
}

/// <summary>
/// Recognized sum value from a receipt (variant of total).
/// </summary>
public sealed class RecognizedSum : RecognizedTotal
{
    public RecognizedSum(double value, ReceiptTextLine line) : base(value, line) { }
}

/// <summary>
/// Calculated total derived by summing recognized positions.
/// </summary>
public class CalculatedTotal : Valuable<double>
{
    public CalculatedTotal(double value) : base(value) { }

    // TODO: Wire up ReceiptFormatter
    public override string Format(double value) => ReceiptFormatter.Format(value);
}

/// <summary>
/// Calculated sum (alias for calculated total).
/// </summary>
public sealed class CalculatedSum : CalculatedTotal
{
    public CalculatedSum(double value) : base(value) { }
}

/// <summary>
/// Recognized label text associated with the total line on a receipt.
/// </summary>
public class RecognizedTotalLabel : RecognizedEntity<string>
{
    public RecognizedTotalLabel(string value, ReceiptTextLine line) : base(value, line) { }

    public RecognizedTotalLabel CopyWith(string? value = null, ReceiptTextLine? line = null) =>
        new(value ?? Value, line ?? Line);

    public override string Format(string value) => value.ToUpperInvariant();
}

/// <summary>
/// Recognized label text associated with the sum line on a receipt.
/// </summary>
public sealed class RecognizedSumLabel : RecognizedTotalLabel
{
    public RecognizedSumLabel(string value, ReceiptTextLine line) : base(value, line) { }
}
