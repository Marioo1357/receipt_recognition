using System.Text.RegularExpressions;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Utils.Configuration;

/// <summary>How user config should interact with built-in defaults.</summary>
public enum MergePolicy
{
    /// <summary>Keep defaults and add/override with user config (defaults ∪ user).</summary>
    Extend,

    /// <summary>Ignore defaults completely and use only user config.</summary>
    Override,
}

/// <summary>
/// Strongly-typed, user-configurable options for the parser with merge helpers.
/// </summary>
public sealed class ReceiptOptions
{
    /// <summary>A literal that won't ever occur in receipt text → safe never-match regex.</summary>
    public const string NeverMatchLiteral = @"(?!)";

    /// <summary>Map of store aliases to canonical names.</summary>
    public DetectionMap StoreNames { get; }

    /// <summary>Map of total labels to canonical label.</summary>
    public DetectionMap TotalLabels { get; }

    /// <summary>Keywords that should be ignored during parsing.</summary>
    public KeywordSet IgnoreKeywords { get; }

    /// <summary>Keywords that indicate parsing should stop.</summary>
    public KeywordSet StopKeywords { get; }

    /// <summary>Whitelist of product group keywords allowed as item candidates.</summary>
    public KeywordSet AllowedProductGroups { get; }

    /// <summary>Numeric/string tuning applied across the parser/optimizer.</summary>
    public ReceiptTuning Tuning { get; }

    /// <summary>
    /// The text recognition script used for OCR, which also determines
    /// which parsing algorithm is used to extract receipt entities.
    /// Null (default) = European-style geometric parser.
    /// "Japanese" = row-grouping parser for Japanese receipt layouts.
    /// </summary>
    public string? Script { get; }

    private ReceiptOptions(
        DetectionMap storeNames,
        DetectionMap totalLabels,
        KeywordSet ignoreKeywords,
        KeywordSet stopKeywords,
        KeywordSet allowedProductGroups,
        ReceiptTuning tuning,
        string? script = null)
    {
        StoreNames = storeNames;
        TotalLabels = totalLabels;
        IgnoreKeywords = ignoreKeywords;
        StopKeywords = stopKeywords;
        AllowedProductGroups = allowedProductGroups;
        Tuning = tuning;
        Script = script;
    }

    /// <summary>
    /// Public factory that mirrors the layered user config structure.
    /// extend: union-merged with defaults (user wins on duplicates).
    /// overrideConfig: fully replaces defaults per provided key.
    /// tuning: ALWAYS override-only (top-level, not part of extend/override).
    /// </summary>
    public static ReceiptOptions Create(
        Dictionary<string, object>? extend = null,
        Dictionary<string, object>? overrideConfig = null,
        Dictionary<string, object>? tuning = null)
    {
        var def = Defaults();

        var ext = extend ?? new Dictionary<string, object>();
        var ovw = overrideConfig ?? new Dictionary<string, object>();
        var tun = tuning ?? new Dictionary<string, object>();

        DetectionMap ResolveMap(DetectionMap defaults, string key)
        {
            if (ovw.TryGetValue(key, out var ovwVal))
                return DetectionMap.FromMap(PickStrMap(ovwVal));
            if (ext.TryGetValue(key, out var extVal))
                return DmMerge(defaults, DetectionMap.FromMap(PickStrMap(extVal)), MergePolicy.Extend);
            return defaults;
        }

        KeywordSet ResolveList(KeywordSet defaults, string key)
        {
            if (ovw.TryGetValue(key, out var ovwVal))
                return KeywordSet.FromList(PickStrList(ovwVal));
            if (ext.TryGetValue(key, out var extVal))
                return KsMerge(defaults, KeywordSet.FromList(PickStrList(extVal)), MergePolicy.Extend);
            return defaults;
        }

        var storeNames = ResolveMap(def.StoreNames, "storeNames");
        var totalLabels = ResolveMap(def.TotalLabels, "totalLabels");
        var ignoreKeywords = ResolveList(def.IgnoreKeywords, "ignoreKeywords");
        var stopKeywords = ResolveList(def.StopKeywords, "stopKeywords");
        var allowedProductGroups = ResolveList(def.AllowedProductGroups, "allowedProductGroups");

        var tuningResolved = tun.Count > 0 ? ReceiptTuning.FromJsonLike(tun) : def.Tuning;

        return new ReceiptOptions(
            storeNames, totalLabels, ignoreKeywords,
            stopKeywords, allowedProductGroups, tuningResolved);
    }

    /// <summary>Builds from a layered JSON map {extend, override, tuning}.</summary>
    public static ReceiptOptions FromLayeredJson(Dictionary<string, object>? json)
    {
        return Create(
            extend: json != null && json.TryGetValue("extend", out var e) ? e as Dictionary<string, object> : null,
            overrideConfig: json != null && json.TryGetValue("override", out var o) ? o as Dictionary<string, object> : null,
            tuning: json != null && json.TryGetValue("tuning", out var t) ? t as Dictionary<string, object> : null);
    }

    /// <summary>Returns a minimal config with all maps/lists empty (no matches).</summary>
    public static ReceiptOptions Empty() => new(
        DetectionMap.FromMap(new Dictionary<string, string>()),
        DetectionMap.FromMap(new Dictionary<string, string>()),
        KeywordSet.FromList(new List<string>()),
        KeywordSet.FromList(new List<string>()),
        KeywordSet.FromList(new List<string>()),
        ReceiptTuning.FromJsonLike(new Dictionary<string, object>()));

    /// <summary>Builds options from a flat JSON-like map (no layered rules).</summary>
    public static ReceiptOptions FromJsonLike(Dictionary<string, object> json)
    {
        return new ReceiptOptions(
            storeNames: DetectionMap.FromMap(PickStrMap(json.GetValueOrDefault("storeNames"))),
            totalLabels: DetectionMap.FromMap(PickStrMap(json.GetValueOrDefault("totalLabels"))),
            ignoreKeywords: KeywordSet.FromList(PickStrList(json.GetValueOrDefault("ignoreKeywords"))),
            stopKeywords: KeywordSet.FromList(PickStrList(json.GetValueOrDefault("stopKeywords"))),
            allowedProductGroups: KeywordSet.FromList(PickStrList(json.GetValueOrDefault("allowedProductGroups"))),
            tuning: ReceiptTuning.FromJsonLike(
                json.GetValueOrDefault("tuning") as Dictionary<string, object>));
    }

    /// <summary>Serializes options to a flat JSON-like dictionary.</summary>
    public Dictionary<string, object> ToJsonLike() => new()
    {
        ["storeNames"] = StoreNames.Mapping,
        ["totalLabels"] = TotalLabels.Mapping,
        ["ignoreKeywords"] = IgnoreKeywords.Keywords,
        ["stopKeywords"] = StopKeywords.Keywords,
        ["allowedProductGroups"] = AllowedProductGroups.Keywords,
        ["tuning"] = Tuning.ToJsonLike(),
    };

    /// <summary>Returns options built solely from built-in defaults (no user overrides).</summary>
    public static ReceiptOptions Defaults() =>
        FromJsonLike(ReceiptDefaults.KReceiptDefaultOptions);

    /// <summary>Returns default options for Japanese receipts.</summary>
    public static ReceiptOptions Japanese()
    {
        var baseOpts = FromJsonLike(ReceiptDefaults.KReceiptDefaultOptionsJa);
        return new ReceiptOptions(
            baseOpts.StoreNames, baseOpts.TotalLabels, baseOpts.IgnoreKeywords,
            baseOpts.StopKeywords, baseOpts.AllowedProductGroups, baseOpts.Tuning,
            script: "Japanese");
    }

    private static KeywordSet KsMerge(KeywordSet defaults, KeywordSet user, MergePolicy p)
    {
        if (p == MergePolicy.Override) return user;
        var merged = new HashSet<string>(defaults.Keywords);
        foreach (var k in user.Keywords) merged.Add(k);
        return KeywordSet.FromList(merged.ToList());
    }

    private static DetectionMap DmMerge(DetectionMap defaults, DetectionMap user, MergePolicy p)
    {
        if (p == MergePolicy.Override) return user;
        var merged = new Dictionary<string, string>(defaults.Mapping);
        foreach (var kv in user.Mapping) merged[kv.Key] = kv.Value;
        return DetectionMap.FromMap(merged);
    }

    internal static Dictionary<string, string> PickStrMap(object? v)
    {
        if (v is Dictionary<string, string> strMap) return strMap;
        if (v is Dictionary<string, object> objMap)
        {
            var result = new Dictionary<string, string>();
            foreach (var kv in objMap)
            {
                if (kv.Value is string s) result[kv.Key] = s;
            }
            return result;
        }
        return new Dictionary<string, string>();
    }

    internal static List<string> PickStrList(object? v)
    {
        if (v is List<string> strList) return strList;
        if (v is IEnumerable<object> objList)
            return objList.OfType<string>().ToList();
        return new List<string>();
    }
}

/// <summary>
/// The regex matches each configured label allowing optional single spaces between characters,
/// and lookups are normalized by stripping whitespace and lowercasing.
/// </summary>
public sealed class DetectionMap
{
    /// <summary>Precompiled alternation regex matching any label (case-insensitive).</summary>
    public Regex Regexp { get; }

    /// <summary>Lowercased, space-stripped label→canonical mapping used for lookups.</summary>
    public IReadOnlyDictionary<string, string> Mapping { get; }

    private DetectionMap(Regex regexp, IReadOnlyDictionary<string, string> mapping)
    {
        Regexp = regexp;
        Mapping = mapping;
    }

    /// <summary>
    /// Builds a case-insensitive mapping and one tolerant regex; returns a never-match regex if empty.
    /// </summary>
    public static DetectionMap FromMap(Dictionary<string, string> map)
    {
        if (map.Count == 0)
            return new DetectionMap(new Regex(ReceiptOptions.NeverMatchLiteral), new Dictionary<string, string>());

        var patterns = new List<string>();
        var normalized = new Dictionary<string, string>();

        foreach (var kv in map)
        {
            var k = kv.Key.Trim();
            if (string.IsNullOrEmpty(k)) continue;
            var p = RegexPatternHelper.OptionalSpacesPattern(k);
            if (string.IsNullOrEmpty(p)) continue;
            patterns.Add(p);
            normalized[ReceiptNormalizer.NormalizeKey(k)] = kv.Value;
        }

        var pattern = $"({string.Join("|", patterns)})";
        return new DetectionMap(
            new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled),
            normalized);
    }

    /// <summary>Returns the canonical value if any label in text matches; otherwise null.</summary>
    public string? Detect(string text)
    {
        var m = Regexp.Match(text);
        if (!m.Success) return null;
        return Mapping.TryGetValue(ReceiptNormalizer.NormalizeKey(m.Value), out var val) ? val : null;
    }

    /// <summary>Returns the alternation pattern string used by Regexp.</summary>
    public string Pattern => Regexp.ToString();

    /// <summary>Returns true if text contains any of the configured labels.</summary>
    public bool HasMatch(string s) => Regexp.IsMatch(s);
}

/// <summary>
/// Typed wrapper for simple keyword lists compiled into a single tolerant regex.
/// Each keyword is matched allowing optional single spaces between characters.
/// </summary>
public sealed class KeywordSet
{
    /// <summary>Original keyword list (as provided).</summary>
    public IReadOnlyList<string> Keywords { get; }

    /// <summary>Precompiled alternation regex matching any keyword (case-insensitive).</summary>
    public Regex Regexp { get; }

    private KeywordSet(IReadOnlyList<string> keywords, Regex regexp)
    {
        Keywords = keywords;
        Regexp = regexp;
    }

    /// <summary>
    /// Builds a case-insensitive keyword set and one tolerant alternation regex (never-match if empty).
    /// </summary>
    public static KeywordSet FromList(List<string> list)
    {
        if (list.Count == 0)
            return new KeywordSet(Array.Empty<string>(), new Regex(ReceiptOptions.NeverMatchLiteral));

        var patterns = list
            .Select(k => RegexPatternHelper.OptionalSpacesPattern(k.Trim()))
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (patterns.Count == 0)
            return new KeywordSet(Array.Empty<string>(), new Regex(ReceiptOptions.NeverMatchLiteral));

        var pattern = $"({string.Join("|", patterns)})";
        return new KeywordSet(
            list.AsReadOnly(),
            new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
    }

    /// <summary>Returns true if text contains any keyword.</summary>
    public bool HasMatch(string text) => Regexp.IsMatch(text);
}

/// <summary>
/// Builds a regex pattern that allows zero-or-one space between each character of the provided literal.
/// Example: "Aldi" → A\s?l\s?d\s?i
/// </summary>
internal static class RegexPatternHelper
{
    public static string OptionalSpacesPattern(string literal)
    {
        var stripped = Regex.Replace(literal, @"\s+", "");
        if (string.IsNullOrEmpty(stripped)) return "";

        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(stripped);
        var elements = new List<string>();
        while (enumerator.MoveNext())
            elements.Add(enumerator.GetTextElement());

        var parts = new List<string>();
        for (int i = 0; i < elements.Count; i++)
        {
            parts.Add(Regex.Escape(elements[i]));
            if (i < elements.Count - 1) parts.Add(@"\s?");
        }
        return string.Join("", parts);
    }
}
