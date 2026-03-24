using System.Globalization;
using System.Text.RegularExpressions;

namespace ReceiptRecognition.Core.Utils.Normalize;

/// <summary>
/// Utility for locale-aware number formatting and robust parsing/normalization of receipt text and dates.
/// </summary>
public static class ReceiptFormatter
{
    /// <summary>Collapses whitespace around comma/dot separators: "12 , 34" -> "12,34".</summary>
    private static readonly Regex ReCommaDotSpaces = new(@"(\d)\s*([,.])\s*(\d)", RegexOptions.Compiled);

    /// <summary>Detects amount prefix to convert into postfix text.</summary>
    private static readonly Regex AmountPostfixText = new(
        @"[-−–—]?\s*\d+\s*[.,‚،٫·]\s*\d{2}(?!\d)", RegexOptions.Compiled);

    /// <summary>Maps English and German month names and abbreviations (with umlauts) to numeric month values.</summary>
    public static readonly IReadOnlyDictionary<string, int> MonthMap = new Dictionary<string, int>
    {
        ["jan"] = 1, ["january"] = 1, ["januar"] = 1,
        ["feb"] = 2, ["february"] = 2, ["februar"] = 2,
        ["mar"] = 3, ["march"] = 3, ["märz"] = 3, ["marz"] = 3,
        ["apr"] = 4, ["april"] = 4,
        ["may"] = 5, ["mai"] = 5,
        ["jun"] = 6, ["june"] = 6, ["juni"] = 6,
        ["jul"] = 7, ["july"] = 7, ["juli"] = 7,
        ["aug"] = 8, ["august"] = 8,
        ["sep"] = 9, ["sept"] = 9, ["september"] = 9,
        ["oct"] = 10, ["okt"] = 10, ["october"] = 10, ["oktober"] = 10,
        ["nov"] = 11, ["november"] = 11,
        ["dec"] = 12, ["dez"] = 12, ["december"] = 12, ["dezember"] = 12,
    };

    /// <summary>Japanese era name to base Gregorian year mapping.</summary>
    public static readonly IReadOnlyDictionary<string, int> JapaneseEraMap = new Dictionary<string, int>
    {
        ["令和"] = 2018,
        ["平成"] = 1988,
        ["昭和"] = 1925,
        ["大正"] = 1911,
        ["明治"] = 1867,
    };

    /// <summary>
    /// Formats value to a string with the specified number of decimal digits.
    /// decimalDigits defaults to 2 for most currencies, use 0 for JPY.
    /// </summary>
    public static string Format(double value, int decimalDigits = 2)
    {
        return value.ToString($"F{decimalDigits}", CultureInfo.CurrentCulture);
    }

    /// <summary>Trims value and collapses spaces around commas/dots (e.g. "12 , 34" → "12,34").</summary>
    public static string Trim(string value)
    {
        return ReCommaDotSpaces.Replace(value.Trim(), "$1$2$3");
    }

    /// <summary>Removes a leading amount pattern from text, returning the remainder.</summary>
    public static string ToPostfixText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var trimmed = text.Trim();
        var m = AmountPostfixText.Match(trimmed);
        if (!m.Success || m.Index != 0) return "";
        return trimmed[m.Length..].Trim();
    }

    /// <summary>Parses YYYY-MM-DD (or mixed separators) into DateTime(UTC); returns null if invalid.</summary>
    public static DateTime? ParseNumericYMD(string token)
    {
        var matches = Regex.Matches(token, @"\d{1,4}");
        var parts = matches.Select(m => m.Value).ToList();
        if (parts.Count < 3) return null;
        var y = int.TryParse(parts[0], out var yv) ? yv : (int?)null;
        var mo = int.TryParse(parts[1], out var mv) ? mv : (int?)null;
        var d = int.TryParse(parts[2], out var dv) ? dv : (int?)null;
        return YmdUtc(y, mo, d);
    }

    /// <summary>Parses DD-MM-YY(YY) (or mixed separators) into DateTime(UTC), normalizing 2-digit years to 2000–2099.</summary>
    public static DateTime? ParseNumericDMY(string token)
    {
        var matches = Regex.Matches(token, @"\d{1,4}");
        var parts = matches.Select(m => m.Value).ToList();
        if (parts.Count < 3) return null;
        var d = int.TryParse(parts[0], out var dv) ? dv : (int?)null;
        var mo = int.TryParse(parts[1], out var mv) ? mv : (int?)null;
        var y = NormalizeYear(int.TryParse(parts[2], out var yv) ? yv : (int?)null);
        return YmdUtc(y, mo, d);
    }

    /// <summary>
    /// Parses day–month-name–year ("1. September 2025", "1 Sep 25", EN/DE) into DateTime(UTC); returns null if invalid.
    /// </summary>
    public static DateTime? ParseNameDMY(string token)
    {
        var re = new Regex(@"^\s*(\d{1,2})[.\s\-]+([A-Za-zÄÖÜäöüß.]+)[, ]+(\d{2,4})\s*$", RegexOptions.IgnoreCase);
        var m = re.Match(token);
        if (!m.Success) return null;
        var d = int.TryParse(m.Groups[1].Value, out var dv) ? dv : (int?)null;
        var mon = MonthFromName(m.Groups[2].Value);
        var y = NormalizeYear(int.TryParse(m.Groups[3].Value, out var yv) ? yv : (int?)null);
        return YmdUtc(y, mon, d);
    }

    /// <summary>
    /// Parses month-name–day–year ("September 1, 2025", "Sep 1 25") into DateTime(UTC); returns null if invalid.
    /// </summary>
    public static DateTime? ParseNameMDY(string token)
    {
        var re = new Regex(@"^\s*([A-Za-zÄÖÜäöüß.]+)\s*\.?\s*(\d{1,2}),?\s*(\d{2,4})\s*$", RegexOptions.IgnoreCase);
        var m = re.Match(token);
        if (!m.Success) return null;
        var mon = MonthFromName(m.Groups[1].Value);
        var d = int.TryParse(m.Groups[2].Value, out var dv) ? dv : (int?)null;
        var y = NormalizeYear(int.TryParse(m.Groups[3].Value, out var yv) ? yv : (int?)null);
        return YmdUtc(y, mon, d);
    }

    /// <summary>Parses "2025年1月15日" into DateTime(UTC).</summary>
    public static DateTime? ParseKanjiDate(string token)
    {
        var re = new Regex(@"(\d{4})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日");
        var m = re.Match(token);
        if (!m.Success) return null;
        var y = int.TryParse(m.Groups[1].Value, out var yv) ? yv : (int?)null;
        var mon = int.TryParse(m.Groups[2].Value, out var mv) ? mv : (int?)null;
        var d = int.TryParse(m.Groups[3].Value, out var dv) ? dv : (int?)null;
        return YmdUtc(y, mon, d);
    }

    /// <summary>Parses "令和7年1月15日" into DateTime(UTC) (和暦→西暦変換).</summary>
    public static DateTime? ParseJapaneseEraDate(string token)
    {
        var re = new Regex(@"(令和|平成|昭和|大正|明治)\s*(\d{1,2})\s*年\s*(\d{1,2})\s*月\s*(\d{1,2})\s*日");
        var m = re.Match(token);
        if (!m.Success) return null;
        var era = m.Groups[1].Value;
        var eraYear = int.TryParse(m.Groups[2].Value, out var ev) ? ev : (int?)null;
        var mon = int.TryParse(m.Groups[3].Value, out var mv) ? mv : (int?)null;
        var d = int.TryParse(m.Groups[4].Value, out var dv) ? dv : (int?)null;
        if (eraYear == null) return null;
        if (!JapaneseEraMap.TryGetValue(era, out var baseYear)) return null;
        var y = baseYear + eraYear.Value;
        return YmdUtc(y, mon, d);
    }

    /// <summary>Normalizes 2-digit years to 2000–2099; returns 4-digit years unchanged; null if invalid.</summary>
    private static int? NormalizeYear(int? y)
    {
        if (y == null) return null;
        if (y >= 1000) return y;
        if (y >= 0 && y <= 99) return 2000 + y;
        return null;
    }

    /// <summary>Constructs DateTime(UTC) if the triple is a valid calendar date; otherwise null.</summary>
    private static DateTime? YmdUtc(int? y, int? m, int? d)
    {
        if (y == null || m == null || d == null) return null;
        if (m < 1 || m > 12) return null;
        if (d < 1 || d > 31) return null;
        try
        {
            return new DateTime(y.Value, m.Value, d.Value, 0, 0, 0, DateTimeKind.Utc);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Converts EN/DE month names and abbreviations (with optional dot/umlauts) to a 1–12 month number.</summary>
    private static int? MonthFromName(string raw)
    {
        var n = raw.Trim().ToLowerInvariant().Replace(".", "");

        if (MonthMap.TryGetValue(n, out var month)) return month;
        if (n.Length >= 3)
        {
            var k = n[..3];
            if (MonthMap.TryGetValue(k, out var month3)) return month3;
        }
        return null;
    }
}
