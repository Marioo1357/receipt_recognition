namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized bounding box and optional skew angle for a receipt.
/// </summary>
public sealed class RecognizedBounds : RecognizedEntity<BoundingBox>
{
    public RecognizedBounds(BoundingBox value, ReceiptTextLine line) : base(value, line) { }

    public BoundingBox BoundingBox => Value;

    public double? SkewAngle => Line.Angle;

    public RecognizedBounds CopyWith(double? skewAngle = null)
    {
        return new RecognizedBounds(
            Value,
            new ReceiptTextLine(boundingBox: Value, angle: skewAngle));
    }

    public override string Format(BoundingBox value) => value.ToString();
}
