using ReceiptRecognition.Core.Models;
using ReceiptRecognition.Core.Services.Ocr;
using ReceiptRecognition.Core.Services.Optimizer;
using ReceiptRecognition.Core.Utils.Configuration;

namespace ReceiptRecognition.Core.Services.Ocr;

/// <summary>
/// Main orchestrator for recognizing receipts from images.
/// Coordinates OCR, parsing, optimization, validation, and progress callbacks.
/// </summary>
public sealed class ReceiptRecognizer : IDisposable
{
    private readonly ITextRecognizer _textRecognizer;
    private readonly IOptimizer _optimizer;
    private readonly ReceiptOptions _options;
    private readonly bool _singleScan;
    private readonly int _nearlyCompleteThreshold;
    private readonly TimeSpan _scanInterval;
    private readonly TimeSpan _scanTimeout;
    private readonly TimeSpan _scanCompleteDelay;

    private readonly Action<RecognizedScanProgress>? _onScanUpdate;
    private readonly Action<RecognizedReceipt>? _onScanComplete;
    private readonly Action<RecognizedReceipt>? _onScanTimeout;

    private CancellationTokenSource? _scanTimeoutCts;
    private bool _shouldInitialize;
    private DateTime? _initializedScan;
    private DateTime? _lastScan;
    private RecognizedReceipt _lastReceipt;

    public ReceiptRecognizer(
        ITextRecognizer textRecognizer,
        IOptimizer? optimizer = null,
        TextRecognitionScript script = TextRecognitionScript.Latin,
        ReceiptOptions? options = null,
        bool singleScan = false,
        int nearlyCompleteThreshold = 95,
        TimeSpan? scanInterval = null,
        TimeSpan? scanTimeout = null,
        TimeSpan? scanCompleteDelay = null,
        Action<RecognizedScanProgress>? onScanUpdate = null,
        Action<RecognizedReceipt>? onScanComplete = null,
        Action<RecognizedReceipt>? onScanTimeout = null)
    {
        _textRecognizer = textRecognizer;
        _optimizer = optimizer ?? new ReceiptOptimizer();
        _options = options ?? DefaultOptionsForScript(script);
        _singleScan = singleScan;
        _nearlyCompleteThreshold = nearlyCompleteThreshold;
        _scanInterval = scanInterval ?? TimeSpan.FromMilliseconds(50);
        _scanTimeout = scanTimeout ?? TimeSpan.FromSeconds(20);
        _scanCompleteDelay = scanCompleteDelay ?? TimeSpan.Zero;
        _onScanUpdate = onScanUpdate;
        _onScanComplete = onScanComplete;
        _onScanTimeout = onScanTimeout;
        _lastReceipt = RecognizedReceipt.Empty();
    }

    /// <summary>
    /// Returns the default <see cref="ReceiptOptions"/> for the given script.
    /// </summary>
    public static ReceiptOptions DefaultOptionsForScript(TextRecognitionScript script) =>
        script switch
        {
            TextRecognitionScript.Japanese => ReceiptOptions.Japanese(),
            _ => ReceiptOptions.Defaults(),
        };

    /// <summary>
    /// Processes a file at the given path and returns a recognized receipt.
    /// </summary>
    public Task<RecognizedReceipt> ProcessFilePathAsync(string path) =>
        ProcessImageAsync(path);

    /// <summary>
    /// Processes an image and returns a recognized receipt.
    /// </summary>
    public async Task<RecognizedReceipt> ProcessImageAsync(string imagePath)
    {
        InitializeIfNeeded();

        var now = DateTime.UtcNow;
        if (ShouldThrottle(now)) return _lastReceipt;
        _lastScan = now;

        var receipt = await RecognizeReceiptAsync(imagePath, _options);

        var optimized = _optimizer.Optimize(receipt, _options, singleScan: _singleScan);

        var validation = ValidateReceipt(optimized);
        var accepted = HandleValidationResult(now, optimized, validation);

        if (accepted.IsValid && accepted.IsConfirmed)
        {
            _lastReceipt = accepted;
            if (_scanCompleteDelay > TimeSpan.Zero)
                await Task.Delay(_scanCompleteDelay);
            return accepted;
        }

        _lastReceipt = optimized;
        return optimized;
    }

    /// <summary>
    /// Processes an already-obtained OcrResult directly (useful for testing).
    /// </summary>
    public async Task<RecognizedReceipt> ProcessOcrResultAsync(OcrResult ocrResult)
    {
        InitializeIfNeeded();

        var now = DateTime.UtcNow;
        if (ShouldThrottle(now)) return _lastReceipt;
        _lastScan = now;

        var receipt = await ReceiptTextProcessor.ProcessText(ocrResult, _options);

        var optimized = _optimizer.Optimize(receipt, _options, singleScan: _singleScan);

        var validation = ValidateReceipt(optimized);
        var accepted = HandleValidationResult(now, optimized, validation);

        if (accepted.IsValid && accepted.IsConfirmed)
        {
            _lastReceipt = accepted;
            if (_scanCompleteDelay > TimeSpan.Zero)
                await Task.Delay(_scanCompleteDelay);
            return accepted;
        }

        _lastReceipt = optimized;
        return optimized;
    }

    /// <summary>
    /// Manually accepts a receipt and resets internal state.
    /// </summary>
    public RecognizedReceipt AcceptReceipt(RecognizedReceipt receipt)
    {
        _optimizer.Accept(receipt, _options);
        Init();
        return receipt;
    }

    /// <summary>
    /// Marks the recognizer for reinitialization on next recognition.
    /// </summary>
    public void Init()
    {
        _scanTimeoutCts?.Cancel();
        _scanTimeoutCts?.Dispose();
        _scanTimeoutCts = null;
        _shouldInitialize = true;
    }

    /// <summary>
    /// Releases all resources used by the recognizer.
    /// </summary>
    public void Dispose()
    {
        _textRecognizer.Dispose();
        _optimizer.Dispose();
        _scanTimeoutCts?.Cancel();
        _scanTimeoutCts?.Dispose();
    }

    private void InitializeIfNeeded()
    {
        if (!_shouldInitialize) return;
        _initializedScan = null;
        _scanTimeoutCts?.Cancel();
        _scanTimeoutCts?.Dispose();
        _scanTimeoutCts = null;
        _lastScan = null;
        _lastReceipt = RecognizedReceipt.Empty();
        _optimizer.Init();
        _shouldInitialize = false;
    }

    private void ScheduleTimeoutIfNeeded(DateTime now)
    {
        _initializedScan ??= now;
        if (_scanTimeoutCts != null) return;

        _scanTimeoutCts = new CancellationTokenSource();
        var token = _scanTimeoutCts.Token;
        _ = Task.Delay(_scanTimeout, token).ContinueWith(_ =>
        {
            if (token.IsCancellationRequested) return;
            var receipt = _lastReceipt;
            Init();
            _onScanTimeout?.Invoke(receipt);
        }, token);
    }

    private async Task<RecognizedReceipt> RecognizeReceiptAsync(
        string imagePath, ReceiptOptions options)
    {
        var text = await _textRecognizer.ProcessImageAsync(imagePath);
        return await ReceiptTextProcessor.ProcessText(text, options);
    }

    private RecognizedReceipt HandleValidationResult(
        DateTime now, RecognizedReceipt receipt, ReceiptValidationResult validation)
    {
        return validation.Status switch
        {
            ReceiptCompleteness.Complete => HandleValidReceipt(receipt, validation),
            _ => HandleIncompleteReceipt(now, receipt, validation),
        };
    }

    private ReceiptValidationResult ValidateReceipt(RecognizedReceipt receipt)
    {
        if (receipt.Positions.Count == 0 || receipt.Total == null)
        {
            return new ReceiptValidationResult(
                ReceiptCompleteness.Invalid, 0, "Receipt missing critical information");
        }

        var pct = CalculateMatchPercentage(receipt);
        if (pct == 100)
        {
            return new ReceiptValidationResult(
                ReceiptCompleteness.Complete, 100, "Receipt complete");
        }
        if (pct >= _nearlyCompleteThreshold)
        {
            return new ReceiptValidationResult(
                ReceiptCompleteness.NearlyComplete, pct, $"Receipt nearly complete ({pct}%)");
        }
        return new ReceiptValidationResult(
            ReceiptCompleteness.Incomplete, pct, $"Receipt incomplete ({pct}%)");
    }

    private static int CalculateMatchPercentage(RecognizedReceipt receipt)
    {
        var calc = (double)receipt.CalculatedTotal.Value;
        var decl = (double)(receipt.Total?.Value ?? 0);
        if (calc <= 0 || decl <= 0) return 0;

        var ratio = calc < decl ? calc / decl : decl / calc;
        return (int)Math.Clamp(ratio * 100, 0.0, 100.0);
    }

    private bool ShouldThrottle(DateTime now) =>
        _lastScan.HasValue && now - _lastScan.Value < _scanInterval;

    private RecognizedReceipt HandleValidReceipt(
        RecognizedReceipt receipt, ReceiptValidationResult validation)
    {
        HandleOnScanUpdate(receipt, validation);
        if (receipt.IsValid && receipt.IsConfirmed)
        {
            Init();
            _onScanComplete?.Invoke(receipt);
        }
        return receipt;
    }

    private RecognizedReceipt HandleIncompleteReceipt(
        DateTime now, RecognizedReceipt receipt, ReceiptValidationResult validation)
    {
        HandleOnScanUpdate(receipt, validation);
        _lastReceipt = receipt;
        ScheduleTimeoutIfNeeded(now);
        return receipt;
    }

    private void HandleOnScanUpdate(
        RecognizedReceipt receipt, ReceiptValidationResult validation)
    {
        var changed = receipt.Positions
            .Where(p => p.Operation != Operation.None).ToList();
        var added = changed.Where(p => p.Operation == Operation.Added).ToList();
        var updated = changed.Where(p => p.Operation == Operation.Updated).ToList();

        _onScanUpdate?.Invoke(new RecognizedScanProgress(
            positions: changed,
            addedPositions: added,
            updatedPositions: updated,
            validationResult: validation,
            estimatedPercentage: validation.MatchPercentage,
            mergedReceipt: receipt));
    }
}
