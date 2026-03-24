using ReceiptRecognition.Core.Utils.Configuration;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// Recognized product name from a receipt line.
/// </summary>
public sealed class RecognizedProduct : RecognizedEntity<string>
{
    public Confidence? Confidence { get; set; }
    public RecognizedPosition? Position { get; set; }
    public ReceiptOptions Options { get; }

    public RecognizedProduct(
        string value, ReceiptTextLine line,
        Confidence? confidence = null,
        RecognizedPosition? position = null,
        ReceiptOptions? options = null)
        : base(value, line)
    {
        Confidence = confidence;
        Position = position;
        Options = options ?? ReceiptOptions.Defaults();
    }

    public static RecognizedProduct FromJson(Dictionary<string, object?> json, ReceiptOptions? options = null)
    {
        var rawValue = json.GetValueOrDefault("value");
        var value = rawValue is string s ? s : (rawValue ?? "").ToString()!;

        var rawConf = json.GetValueOrDefault("confidence");
        var confValue = rawConf is int i
            ? i
            : int.TryParse(rawConf?.ToString() ?? "0", out var iv) ? iv : 0;

        return new RecognizedProduct(
            value,
            new ReceiptTextLine(),
            confidence: new Confidence(confValue),
            options: options);
    }

    public RecognizedProduct CopyWith(
        string? value = null, ReceiptTextLine? line = null,
        Confidence? confidence = null, RecognizedPosition? position = null,
        ReceiptOptions? options = null)
    {
        return new RecognizedProduct(
            value ?? Value, line ?? Line,
            confidence: confidence ?? Confidence,
            position: position ?? Position,
            options: options ?? Options);
    }

    // TODO: Wire up ReceiptFormatter
    public override string Format(string value) => ReceiptFormatter.Trim(value);

    public string Text => FormattedValue;

    public RecognizedUnit Unit
    {
        get
        {
            if (Position == null)
                return RecognizedUnit.FromNumbers(0, 0, new ReceiptTextLine());

            var fallback = RecognizedUnit.FromNumbers(1, Position.Price.Value, Position.Price.Line);

            var prices = AlternativeUnits.Select(p => p.Price.FormattedValue).ToList();
            // TODO: Wire up ReceiptNormalizer
            var price = ReceiptNormalizer.SortByFrequency(prices).LastOrDefault();
            if (price == null) return fallback;

            var unit = AlternativeUnits.Where(p => p.Price.FormattedValue == price).LastOrDefault();
            if (unit == null) return fallback;

            return unit;
        }
    }

    // TODO: Wire up ReceiptNormalizer
    public string NormalizedText =>
        ReceiptNormalizer.NormalizeByAlternativeTexts(AlternativeTexts) ?? Text;

    // TODO: Wire up ReceiptFormatter
    public string PostfixText =>
        ReceiptFormatter.ToPostfixText(Position?.Price.Line.Text ?? "");

    // TODO: Wire up ReceiptNormalizer
    public string ProductGroup
    {
        get
        {
            var normText = ReceiptNormalizer.NormalizeToProductGroup(PostfixText);
            var alts = AlternativePostfixTexts;
            if (alts.Count == 0) return normText;
            var normalized = ReceiptNormalizer.NormalizeByAlternativePostfixTexts(alts);
            return string.IsNullOrEmpty(normalized) ? normText : normalized;
        }
    }

    public List<string> AlternativeTexts =>
        Position?.Group?.AlternativeTexts ?? [];

    public List<RecognizedUnit> AlternativeUnits =>
        Position?.Group?.AlternativeUnits ?? [];

    public List<string> AlternativePostfixTexts =>
        Position?.Group?.AlternativePostfixTexts ?? [];

    public int TextConsensusRatio
    {
        get
        {
            var alts = AlternativeTexts;
            if (alts.Count == 0) return 100;

            var counts = new Dictionary<string, int>();
            foreach (var t in alts)
            {
                counts[t] = counts.GetValueOrDefault(t) + 1;
            }

            var maxCount = 0;
            foreach (var v in counts.Values)
            {
                if (v > maxCount) maxCount = v;
            }

            return (int)Math.Round((double)maxCount * 100 / alts.Count);
        }
    }

    // TODO: Wire up ReceiptNormalizer
    public Dictionary<string, int> AlternativeTextPercentages =>
        ReceiptNormalizer.CalculateFrequency(AlternativeTexts);
}
