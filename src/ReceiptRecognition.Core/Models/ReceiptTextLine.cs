using System.Drawing;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Represents a single line of text recognized from a receipt image.
/// </summary>
public class ReceiptTextLine
{
    public string Text { get; init; }
    public IReadOnlyList<string> Elements { get; init; }
    public BoundingBox BoundingBox { get; init; }
    public IReadOnlyList<string> RecognizedLanguages { get; init; }
    public IReadOnlyList<Point> CornerPoints { get; init; }
    public double? Confidence { get; init; }
    public double? Angle { get; init; }

    public ReceiptTextLine(
        string text = "",
        IReadOnlyList<string>? elements = null,
        BoundingBox boundingBox = default,
        IReadOnlyList<string>? recognizedLanguages = null,
        IReadOnlyList<Point>? cornerPoints = null,
        double? confidence = null,
        double? angle = null)
    {
        Text = text;
        Elements = elements ?? Array.Empty<string>();
        BoundingBox = boundingBox;
        RecognizedLanguages = recognizedLanguages ?? Array.Empty<string>();
        CornerPoints = cornerPoints ?? Array.Empty<Point>();
        Confidence = confidence;
        Angle = angle;
    }

    public static ReceiptTextLine FromLine(ReceiptTextLine line) => new(
        text: line.Text,
        elements: line.Elements,
        boundingBox: line.BoundingBox,
        recognizedLanguages: line.RecognizedLanguages,
        cornerPoints: line.CornerPoints,
        confidence: line.Confidence,
        angle: line.Angle);

    public ReceiptTextLine CopyWith(
        string? text = null,
        IReadOnlyList<string>? elements = null,
        BoundingBox? boundingBox = null,
        IReadOnlyList<string>? recognizedLanguages = null,
        IReadOnlyList<Point>? cornerPoints = null,
        double? confidence = null,
        double? angle = null)
    {
        return new ReceiptTextLine(
            text: text ?? Text,
            elements: elements ?? Elements,
            boundingBox: boundingBox ?? BoundingBox,
            recognizedLanguages: recognizedLanguages ?? RecognizedLanguages,
            cornerPoints: cornerPoints ?? CornerPoints,
            confidence: confidence ?? Confidence,
            angle: angle ?? Angle);
    }
}
