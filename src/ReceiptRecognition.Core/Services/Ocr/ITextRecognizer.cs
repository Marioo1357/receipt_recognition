using ReceiptRecognition.Core.Services.Ocr;

namespace ReceiptRecognition.Core.Services.Ocr;

/// <summary>
/// Supported OCR script types.
/// </summary>
public enum TextRecognitionScript
{
    Latin,
    Chinese,
    Devanagari,
    Japanese,
    Korean
}

/// <summary>
/// Abstraction over the platform OCR engine (e.g. Google ML Kit on Android).
/// </summary>
public interface ITextRecognizer : IDisposable
{
    /// <summary>
    /// Processes an image file and returns OCR results.
    /// </summary>
    Task<OcrResult> ProcessImageAsync(string filePath);
}
