namespace ReceiptRecognition.Core.Utils.Normalize;

/// <summary>
/// Placeholder for receipt normalization utilities. Will be fully implemented later.
/// </summary>
public static class ReceiptNormalizer
{
    // TODO: Wire up full normalization logic
    public static int Similarity(string a, string b) => FuzzySharp.Fuzz.Ratio(a, b);

    /// <summary>
    /// Returns unique items sorted by ascending frequency (least frequent first, most frequent last).
    /// Callers use <c>.LastOrDefault()</c> to get the most frequent item.
    /// </summary>
    public static List<string> SortByFrequency(List<string> items)
    {
        return items.GroupBy(x => x).OrderBy(g => g.Count()).Select(g => g.Key).ToList();
    }

    public static string? NormalizeByAlternativeTexts(List<string> texts)
    {
        if (texts.Count == 0) return null;
        return SortByFrequency(texts).LastOrDefault();
    }

    // TODO: Implement actual product group normalization
    public static string NormalizeToProductGroup(string text) => text;

    public static string? NormalizeByAlternativePostfixTexts(List<string> texts)
    {
        if (texts.Count == 0) return null;
        return SortByFrequency(texts).LastOrDefault();
    }

    public static Dictionary<string, int> CalculateFrequency(List<string> items)
    {
        var total = items.Count;
        if (total == 0) return new Dictionary<string, int>();
        return items.GroupBy(x => x)
            .ToDictionary(g => g.Key, g => (int)Math.Round((double)g.Count() * 100 / total));
    }
}
