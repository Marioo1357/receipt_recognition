namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Axis-aligned bounding box defined by left, top, right, and bottom edges.
/// </summary>
public readonly record struct BoundingBox(float Left, float Top, float Right, float Bottom)
{
    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public float CenterX => (Left + Right) / 2f;
    public float CenterY => (Top + Bottom) / 2f;

    public static BoundingBox FromLTRB(float left, float top, float right, float bottom) =>
        new(left, top, right, bottom);

    public override string ToString() => $"BoundingBox(L={Left}, T={Top}, R={Right}, B={Bottom})";
}
