namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Base class for any value that can be formatted for display.
/// </summary>
public abstract class Valuable<T>
{
    public T Value { get; }

    protected Valuable(T value)
    {
        Value = value;
    }

    public abstract string Format(T value);

    public string FormattedValue => Format(Value);
}

/// <summary>
/// Non-generic interface for recognized entities, enabling heterogeneous collections.
/// </summary>
public interface IRecognizedEntity
{
    object RawValue { get; }
    ReceiptTextLine Line { get; }
}

/// <summary>
/// A recognized entity that associates a typed value with the text line it was extracted from.
/// </summary>
public abstract class RecognizedEntity<T> : Valuable<T>, IRecognizedEntity
{
    public ReceiptTextLine Line { get; }

    object IRecognizedEntity.RawValue => Value!;

    protected RecognizedEntity(T value, ReceiptTextLine line) : base(value)
    {
        Line = line;
    }
}

/// <summary>
/// A recognized entity whose value could not be classified into a specific type.
/// </summary>
public sealed class RecognizedUnknown : RecognizedEntity<string>
{
    public RecognizedUnknown(string value, ReceiptTextLine line) : base(value, line) { }

    public override string Format(string value) => value;
}

/// <summary>
/// Tracks whether a position was added, updated, or unchanged.
/// </summary>
public enum Operation
{
    None,
    Added,
    Updated
}

/// <summary>
/// Represents a confidence score with an optional weight for weighted averaging.
/// </summary>
public sealed class Confidence : Valuable<int>
{
    public int Weight { get; }

    public Confidence(int value, int weight = 1) : base(value)
    {
        Weight = weight;
    }

    public override string Format(int value) => $"{value}%";
}
