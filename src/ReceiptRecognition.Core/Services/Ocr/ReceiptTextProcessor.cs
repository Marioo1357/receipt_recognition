using ReceiptRecognition.Core.Models;
using ReceiptRecognition.Core.Services.Ocr;
using ReceiptRecognition.Core.Services.Parser;
using ReceiptRecognition.Core.Utils.Configuration;

namespace ReceiptRecognition.Core.Services.Ocr;

/// <summary>
/// Parses OCR text into structured receipt data, optionally on a background thread.
/// </summary>
public sealed class ReceiptTextProcessor
{
    /// <summary>
    /// When true, bypasses Task.Run and runs parsing synchronously for testing.
    /// </summary>
    public static bool DebugRunSynchronouslyForTests { get; set; }

    /// <summary>
    /// Runs parsing (optionally off the UI thread) and returns a structured receipt.
    /// </summary>
    public static Task<RecognizedReceipt> ProcessText(OcrResult text, ReceiptOptions options)
    {
        if (DebugRunSynchronouslyForTests)
        {
            var result = ReceiptParser.ProcessText(text, options);
            return Task.FromResult(result);
        }

        return Task.Run(() => ReceiptParser.ProcessText(text, options));
    }
}
