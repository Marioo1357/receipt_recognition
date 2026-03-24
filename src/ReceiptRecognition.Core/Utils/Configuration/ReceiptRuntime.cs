namespace ReceiptRecognition.Core.Utils.Configuration;

/// <summary>
/// Process-wide runtime that exposes the active options and tuning knobs.
/// Uses AsyncLocal for thread-safe scoped overrides.
/// </summary>
public sealed class ReceiptRuntime
{
    private ReceiptRuntime() { }

    /// <summary>Global singleton instance.</summary>
    public static readonly ReceiptRuntime Instance = new();

    private static readonly AsyncLocal<ReceiptOptions?> ScopedOptions = new();

    private ReceiptOptions _options = ReceiptOptions.Defaults();

    /// <summary>Current effective options (merged user/defaults).</summary>
    public static ReceiptOptions Options => ScopedOptions.Value ?? Instance._options;

    /// <summary>Current effective tuning (shortcut to options.tuning).</summary>
    public static ReceiptTuning Tuning => Options.Tuning;

    /// <summary>Replace the active options globally.</summary>
    public static void SetOptions(ReceiptOptions options)
    {
        Instance._options = options;
    }

    /// <summary>Run fn with options temporarily active (thread-safe via AsyncLocal).</summary>
    public static T RunWithOptions<T>(ReceiptOptions options, Func<T> fn)
    {
        var prev = ScopedOptions.Value;
        ScopedOptions.Value = options;
        try
        {
            return fn();
        }
        finally
        {
            ScopedOptions.Value = prev;
        }
    }
}
