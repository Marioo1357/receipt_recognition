namespace ReceiptRecognition.Core.Services.Ocr;

using ReceiptRecognition.Core.Models;

/// <summary>
/// Simple replacement for Google ML Kit's RecognizedText.
/// Holds a list of <see cref="OcrTextBlock"/>s, each containing lines.
/// </summary>
public sealed class OcrResult
{
    public IReadOnlyList<OcrTextBlock> Blocks { get; }

    public OcrResult(IReadOnlyList<OcrTextBlock> blocks)
    {
        Blocks = blocks;
    }

    /// <summary>Creates an empty result with no blocks.</summary>
    public static OcrResult Empty() => new([]);

    /// <summary>Flattens all blocks into a single list of lines.</summary>
    public IReadOnlyList<ReceiptTextLine> AllLines()
    {
        var lines = new List<ReceiptTextLine>();
        foreach (var block in Blocks)
            lines.AddRange(block.Lines);
        return lines;
    }
}

/// <summary>
/// Simple replacement for Google ML Kit's TextBlock.
/// Groups related <see cref="ReceiptTextLine"/>s within a block of text.
/// </summary>
public sealed class OcrTextBlock
{
    public IReadOnlyList<ReceiptTextLine> Lines { get; }

    public OcrTextBlock(IReadOnlyList<ReceiptTextLine> lines)
    {
        Lines = lines;
    }
}
