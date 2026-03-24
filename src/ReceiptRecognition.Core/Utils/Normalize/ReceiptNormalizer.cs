using System.Text;
using System.Text.RegularExpressions;
using FuzzySharp;
using ReceiptRecognition.Core.Utils.Configuration;

namespace ReceiptRecognition.Core.Utils.Normalize;

/// <summary>
/// Utility for normalizing and standardizing recognized text from receipts.
/// </summary>
public static class ReceiptNormalizer
{
    /// <summary>All common Unicode whitespace characters.</summary>
    private static readonly Regex AllSpaces = new(
        @"[\u0009-\u000D\u0020\u0085\u00A0\u1680\u180E\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]+",
        RegexOptions.Compiled);

    /// <summary>Product group disallowed chars.</summary>
    private static readonly Regex DisallowedGroupChars = new(@"[^A-Za-z0-9]", RegexOptions.Compiled);

    /// <summary>Collapse all Unicode spaces to a normal space first.</summary>
    private static string NormalizeSpaces(string s) => AllSpaces.Replace(s, " ").Trim();

    /// <summary>
    /// Normalizes postfix text to a product group. If ReceiptRuntime.Options
    /// defines AllowedProductGroups and the normalized value is not contained,
    /// returns empty string.
    /// </summary>
    public static string NormalizeToProductGroup(string postfixText)
    {
        var cleaned = DisallowedGroupChars.Replace(postfixText, "");
        if (string.IsNullOrEmpty(cleaned)) return "";

        var allowed = ReceiptRuntime.Options.AllowedProductGroups.Keywords;
        if (allowed.Count > 0 && !allowed.Contains(cleaned))
            return "";

        return cleaned;
    }

    /// <summary>Normalizes all postfix texts to product groups.</summary>
    public static List<string> NormalizeToProductGroups(List<string> postfixTexts)
    {
        return postfixTexts.Select(NormalizeToProductGroup).ToList();
    }

    /// <summary>Normalizes postfix text by comparing multiple alternative recognitions.</summary>
    public static string? NormalizeByAlternativePostfixTexts(List<string> altPostfixTexts)
    {
        if (altPostfixTexts.Count == 0) return null;

        var normalized = NormalizeToProductGroups(altPostfixTexts);
        var mostFrequent = SortByFrequency(normalized);
        var bestResult = mostFrequent.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";

        return bestResult;
    }

    /// <summary>Normalizes text by comparing multiple alternative recognitions.</summary>
    public static string? NormalizeByAlternativeTexts(List<string> altTexts)
    {
        if (altTexts.Count == 0) return null;

        var mostFrequent = SortByFrequency(altTexts, CalculateTruncatedFrequency);
        var bestResult = mostFrequent.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? "";

        return bestResult;
    }

    /// <summary>
    /// Like CalculateFrequency, but merges:
    ///  - truncated leading-token alternatives, and
    ///  - single-space-variant alternatives (e.g. "Hello wor ld" -> "Hello world")
    /// into their more frequent counterparts before counting.
    /// </summary>
    public static Dictionary<string, int> CalculateTruncatedFrequency(List<string> values)
    {
        if (values.Count == 0) return new Dictionary<string, int>();

        var normalized = values.Select(s => NormalizeSpaces(s)).ToList();
        var trimmed = normalized.Select(s => s.Trim()).ToList();

        var initialCounts = new Dictionary<string, int>();
        foreach (var s in trimmed)
            initialCounts[s] = initialCounts.GetValueOrDefault(s) + 1;

        var repIndex = new Dictionary<int, int>();

        for (int i = 0; i < normalized.Count; i++)
        {
            int representative = i;

            var candidateTrimmed = trimmed[i];
            var candidateCount = initialCounts.GetValueOrDefault(candidateTrimmed);

            for (int j = 0; j < normalized.Count; j++)
            {
                if (i == j) continue;

                var otherTrimmed = trimmed[j];
                var otherCount = initialCounts.GetValueOrDefault(otherTrimmed);

                if (candidateCount >= otherCount) continue;

                bool isPrefixMerge = false;
                bool isSingleSpaceMerge = false;

                if (otherTrimmed.Length > candidateTrimmed.Length &&
                    otherTrimmed.StartsWith(candidateTrimmed, StringComparison.Ordinal))
                {
                    var nextChar = otherTrimmed[candidateTrimmed.Length];
                    if (nextChar == ' ')
                        isPrefixMerge = true;
                }

                if (!isPrefixMerge)
                {
                    if (candidateTrimmed.Length == otherTrimmed.Length + 1)
                    {
                        for (int k = 0; k < candidateTrimmed.Length; k++)
                        {
                            if (candidateTrimmed[k] == ' ')
                            {
                                var merged = candidateTrimmed[..k] + candidateTrimmed[(k + 1)..];
                                if (merged == otherTrimmed)
                                {
                                    isSingleSpaceMerge = true;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (isPrefixMerge || isSingleSpaceMerge)
                {
                    representative = j;
                    break;
                }
            }

            repIndex[i] = representative;
        }

        var remapped = new List<string>();
        for (int i = 0; i < values.Count; i++)
        {
            var idx = repIndex[i];
            remapped.Add(values[idx]);
        }

        return CalculateFrequency(remapped);
    }

    /// <summary>Map of each unique alternative text to its percentage frequency.</summary>
    public static Dictionary<string, int> CalculateFrequency(List<string> values)
    {
        if (values.Count == 0) return new Dictionary<string, int>();

        var total = values.Count;
        var counts = new Dictionary<string, int>();
        foreach (var t in values)
            counts[t] = counts.GetValueOrDefault(t) + 1;

        var result = new Dictionary<string, int>();
        foreach (var kv in counts)
            result[kv.Key] = (int)Math.Round((double)kv.Value / total * 100);
        return result;
    }

    /// <summary>Sorts a list of strings by frequency of occurrence in ascending order.</summary>
    public static List<string> SortByFrequency(
        List<string> values,
        Func<List<string>, Dictionary<string, int>>? frequencyCalculator = null)
    {
        var freq = (frequencyCalculator ?? CalculateFrequency)(values);
        var entries = freq.ToList();
        entries.Sort((a, b) => a.Value.CompareTo(b.Value));
        return entries.Select(e => e.Key).ToList();
    }

    /// <summary>
    /// Returns the best fuzzy match score (0–100) between two strings.
    /// Uses simple, partial and token-set ratios for substring and token-based matching.
    /// </summary>
    public static int Similarity(string a, string b)
    {
        var aNoSpaces = AllSpaces.Replace(a, "");
        var bNoSpaces = AllSpaces.Replace(b, "");
        var ratios = new[]
        {
            Fuzz.Ratio(aNoSpaces, bNoSpaces),
            Fuzz.PartialRatio(aNoSpaces, bNoSpaces),
            Fuzz.TokenSetRatio(aNoSpaces, bNoSpaces),
        };
        return ratios.Max();
    }

    /// <summary>
    /// Returns a merge-friendly similarity in [0,1].
    /// Wraps Similarity (0–100) and scales for thresholding in grouping/merging.
    /// </summary>
    public static double StringSimilarity(string a, string b)
    {
        return Similarity(a, b) / 100.0;
    }

    /// <summary>Tokenizes for matching; lowercase, diacritic-free, alnum-only.</summary>
    public static HashSet<string> TokensForMatch(string s)
    {
        var n = Regex.Replace(NormalizeSpaces(s), @"[^a-z0-9]+", " ").Trim();
        return n.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    }

    /// <summary>Returns a simple specificity score favoring longer, richer strings.</summary>
    public static int Specificity(string s)
    {
        var t = TokensForMatch(s);
        var chars = string.Join("", t).Length;
        return t.Count * 10 + chars;
    }

    /// <summary>Remove all Unicode whitespace and lowercase for stable lookup keys.</summary>
    public static string NormalizeKey(string s) =>
        Regex.Replace(s, @"\s+", "").ToLowerInvariant();

    /// <summary>
    /// Converts fullwidth characters to halfwidth equivalents.
    /// Fullwidth ASCII (U+FF01-U+FF5E) -> halfwidth (U+0021-U+007E)
    /// Fullwidth space (U+3000) -> ASCII space
    /// Fullwidth yen sign (U+FFE5) -> halfwidth yen sign (U+00A5)
    /// </summary>
    public static string NormalizeFullWidth(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var rune in s.EnumerateRunes())
        {
            var code = rune.Value;
            var mapped = code switch
            {
                >= 0xFF01 and <= 0xFF5E => code - 0xFEE0,
                0x3000 => 0x20,
                0xFFE5 => 0x00A5,
                _ => code,
            };
            sb.Append(char.ConvertFromUtf32(mapped));
        }
        return sb.ToString();
    }
}
