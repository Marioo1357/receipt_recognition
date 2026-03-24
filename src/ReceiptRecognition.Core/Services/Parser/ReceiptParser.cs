using System.Text.RegularExpressions;
using FuzzySharp;
using ReceiptRecognition.Core.Models;
using ReceiptRecognition.Core.Services.Ocr;
using ReceiptRecognition.Core.Utils.Configuration;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Services.Parser;

/// <summary>
/// Parses OCR output into a structured receipt by extracting entities, ordering by
/// vertical position, filtering outliers, and assembling positions, total, store, and bounds.
/// </summary>
public static class ReceiptParser
{
    // ─── Geometry helpers ──────────────────────────────────────────

    private static float CyL(ReceiptTextLine l) => l.BoundingBox.CenterY;
    private static float CxL(ReceiptTextLine l) => l.BoundingBox.CenterX;
    private static float LeftL(ReceiptTextLine l) => l.BoundingBox.Left;
    private static float RightL(ReceiptTextLine l) => l.BoundingBox.Right;
    private static float TopL(ReceiptTextLine l) => l.BoundingBox.Top;
    private static float BottomL(ReceiptTextLine l) => l.BoundingBox.Bottom;
    private static float HeightL(ReceiptTextLine l) => l.BoundingBox.Height;

    private static float Cy(IRecognizedEntity e) => CyL(e.Line);
    private static float Cx(IRecognizedEntity e) => CxL(e.Line);
    private static float Left(IRecognizedEntity e) => LeftL(e.Line);
    private static float Right(IRecognizedEntity e) => RightL(e.Line);
    private static float Top(IRecognizedEntity e) => TopL(e.Line);
    private static float Bottom(IRecognizedEntity e) => BottomL(e.Line);

    private static float CyR(BoundingBox r) => r.CenterY;

    private static float Dy(ReceiptTextLine a, ReceiptTextLine b) => CyL(a) - CyL(b);

    private static int CmpCyThenCx(ReceiptTextLine a, ReceiptTextLine b)
    {
        var c = CyL(a).CompareTo(CyL(b));
        return c != 0 ? c : CxL(a).CompareTo(CxL(b));
    }

    private static int CmpTopThenLeft(ReceiptTextLine a, ReceiptTextLine b)
    {
        var c = TopL(a).CompareTo(TopL(b));
        return c != 0 ? c : LeftL(a).CompareTo(LeftL(b));
    }

    // ─── Entity converters ────────────────────────────────────────

    private static RecognizedAmount ToAmount(RecognizedTotal total) =>
        new(total.Value, total.Line);

    private static RecognizedTotal ToTotal(RecognizedAmount amount) =>
        new(amount.Value, amount.Line);

    // ─── Date Regex Patterns ──────────────────────────────────────

    /// <summary>ISO date (YYYY-MM-DD) directly before a time like "T08:50".</summary>
    private static readonly Regex DateIsoYMD = new(
        @"(?<!\d)(\d{4}([-\u2013\u2014])\d{1,2}\2\d{1,2})(?=T\d{1,2}[:.]\\d{2}(?::\d{2})?(?:[.,]\d+)?\b)",
        RegexOptions.Compiled);

    /// <summary>Y-M-D with exactly 0 or 1 space around separators.</summary>
    private static readonly Regex DateYearMonthDayNumeric = new(
        @"(?<!\d)(\d{4} ?([./\-\u2013\u2014]) ?\d{1,2} ?\2 ?\d{1,2})(?:(?=[T\s]\d{1,2}[:.]\\d{2})|(?![0-9A-Za-z]))",
        RegexOptions.Compiled);

    /// <summary>D-M-Y with exactly 0 or 1 space around separators.</summary>
    private static readonly Regex DateDayMonthYearNumeric = new(
        @"(?<!\d)(\d{1,2} ?([./\-\u2013\u2014]) ?\d{1,2} ?\2 ?\d{2,4})(?:(?=\s+\d{1,2}[:.]\\d{2})|(?![0-9A-Za-z]))",
        RegexOptions.Compiled);

    /// <summary>English dates: "1. September 2025", "1 Sep 25".</summary>
    private static readonly Regex DateDayMonthYearEn = new(
        @"\b(\d{1,2}(?:\.\s*|\s+)(Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|" +
        @"Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|" +
        @"Dec(?:ember)?)\.? ,?\s+\d{2,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>U.S. English dates: "September 1, 2025", "Sep 1 25".</summary>
    private static readonly Regex DateMonthDayYearEn = new(
        @"\b((Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|" +
        @"Jul(?:y)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Oct(?:ober)?|Nov(?:ember)?|" +
        @"Dec(?:ember)?)\.?\s*\d{1,2},?\s*\d{2,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>German dates: "1. September 2025", "1 September 2025".</summary>
    private static readonly Regex DateDayMonthYearDe = new(
        @"\b(\d{1,2}(?:\.\s*|\s+)(Jan(?:uar)?|Feb(?:ruar)?|M\u00e4r(?:z)?|Apr(?:il)?|Mai|Jun(?:i)?|" +
        @"Jul(?:i)?|Aug(?:ust)?|Sep(?:t(?:ember)?)?|Okt(?:ober)?|Nov(?:ember)?|" +
        @"Dez(?:ember)?)\.? ,?\s+\d{2,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Kanji date: "2025年1月15日".</summary>
    private static readonly Regex DateKanji = new(
        @"(\d{4}\s*年\s*\d{1,2}\s*月\s*\d{1,2}\s*日)",
        RegexOptions.Compiled);

    /// <summary>Japanese era date: "令和7年1月15日".</summary>
    private static readonly Regex DateJapaneseEra = new(
        @"((令和|平成|昭和|大正|明治)\s*\d{1,2}\s*年\s*\d{1,2}\s*月\s*\d{1,2}\s*日)",
        RegexOptions.Compiled);

    // ─── Amount / Quantity Regex Patterns ─────────────────────────

    /// <summary>
    /// Matches a monetary amount:
    /// Two-decimal pattern: "1,99", "-€4.00", "0,49 €"
    /// Japanese integer yen pattern: "¥198", "￥1,280", "198円", "1,280"
    /// </summary>
    private static readonly Regex AmountPattern = new(
        @"(?:[-\u2212\u2013\u2014]\s*)?(?:[$\u20ac\u00a3\u00a5\uffe5\u20bd\u20b9\u20a9\u20ba\u20ab\u20aa\u20b4\u20a6\u20b1\u20b2\u20b5\u20a1]\s*)?\d[\d,]*\s*[.,\u201a\u060c\u066b\u00b7]\s*\d{2}(?!\d)\s*(?:[$\u20ac\u00a3\u00a5\uffe5\u20bd\u20b9\u20a9\u20ba\u20ab\u20aa\u20b4\u20a6\u20b1\u20b2\u20b5\u20a1])?" +
        @"|(?:[-\u2212\u2013\u2014]\s*)?" +
        @"[\u00a5\uffe5]\s*\d[\d,]*(?:\u5186)?" +
        @"|(?:[-\u2212\u2013\u2014]\s*)?" +
        @"\d[\d,]*\u5186",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches a quantity expression like "3 x 0,49 €" or "2kg × item".
    /// </summary>
    private static readonly Regex QuantityPattern = new(
        @"(?<![\d.,\u201a\u060c\u066b\u00b7])(\d+)(?!\s*[.,\u201a\u060c\u066b\u00b7]\s*\d)\s*(\S{1,3})?\s*[xX\u00d7*](?=\s|$)\s*([^\n\r]*?)(?=\s{2,}|$)",
        RegexOptions.Compiled);

    /// <summary>Regex to extract first integer not part of a decimal.</summary>
    private static readonly Regex IntegerExtractPattern = new(
        @"(?<![\d.,])\d+(?!\s*[.,]\s*\d)",
        RegexOptions.Compiled);

    /// <summary>Shorthand for the active options.</summary>
    private static ReceiptOptions Options => ReceiptRuntime.Options;

    // ─── Entry point ──────────────────────────────────────────────

    /// <summary>
    /// Parses <paramref name="text"/> and returns a structured <see cref="RecognizedReceipt"/>.
    /// When <see cref="ReceiptOptions.Script"/> is "Japanese", delegates to <see cref="ReceiptParserJa"/>.
    /// </summary>
    public static RecognizedReceipt ProcessText(OcrResult text, ReceiptOptions options)
    {
        if (string.Equals(options.Script, "Japanese", StringComparison.OrdinalIgnoreCase))
            return ReceiptParserJa.ProcessText(text, options);

        return ReceiptRuntime.RunWithOptions(options, () =>
        {
            if (text.Blocks.Count == 0) return RecognizedReceipt.Empty();

            var lines = ConvertText(text);
            lines.Sort(CmpCyThenCx);
            var parsed = ParseLines(lines);
            var entities = FilterEntities(parsed);
            return BuildReceipt(entities);
        });
    }

    // ─── Text conversion ──────────────────────────────────────────

    private static List<ReceiptTextLine> ConvertText(OcrResult text)
    {
        var lines = new List<ReceiptTextLine>();
        foreach (var block in text.Blocks)
            lines.AddRange(block.Lines);
        lines.Sort(CmpTopThenLeft);
        return lines;
    }

    // ─── Line parsing pipeline ────────────────────────────────────

    private static List<IRecognizedEntity> ParseLines(List<ReceiptTextLine> lines)
    {
        var parsed = new List<IRecognizedEntity>();
        if (lines.Count == 0) return parsed;

        ApplyPurchaseDate(lines, parsed);
        ApplyBounds(lines, parsed);

        var bounds = FindBounds(parsed);
        if (bounds == null) return parsed;

        var left = LeftL(bounds.Line);
        var right = RightL(bounds.Line);
        var diff = right - left;
        var rightBound = left + (3f / 4f) * diff;
        var centerBound = left + (1f / 2f) * diff;

        foreach (var line in lines)
        {
            if (TryIdentifyTotal(line, parsed, rightBound)) continue;
            if (ShouldStopIfTotalConfirmed(line, parsed)) break;
            if (ShouldStopIfStopWord(line)) break;
            if (ShouldIgnoreLine(line, parsed)) continue;
            if (TryParseTotalLabel(line, parsed, rightBound)) continue;
            if (TryParseStore(line, parsed)) continue;
            if (TryParseAmount(line, parsed, rightBound)) continue;
            if (TryParseUnit(line, parsed, rightBound, centerBound)) continue;
            if (TryParseUnknown(line, parsed, centerBound)) continue;
        }

        return parsed;
    }

    // ─── Should-ignore / should-stop ──────────────────────────────

    private static bool ShouldIgnoreLine(ReceiptTextLine line, List<IRecognizedEntity> parsed)
    {
        var purchaseDateLine = FindPurchaseDate(parsed)?.Line;
        if (purchaseDateLine != null && ReferenceEquals(purchaseDateLine, line)) return true;

        var boundsLine = FindBounds(parsed)?.Line;
        if (boundsLine != null && ReferenceEquals(boundsLine, line)) return true;

        return Options.IgnoreKeywords.HasMatch(line.Text);
    }

    private static bool ShouldStopIfTotalConfirmed(ReceiptTextLine line, List<IRecognizedEntity> parsed)
    {
        var totalLabel = FindTotalLabel(parsed);
        if (totalLabel == null) return false;

        var total = FindTotal(parsed);
        if (total == null) return false;

        var amounts = parsed.OfType<RecognizedAmount>().ToList();
        if (amounts.Count == 0) return false;

        var amountsTotal = amounts.Aggregate(0.0, (a, b) => a + b.Value);
        var calculatedTotal = new CalculatedTotal(amountsTotal);

        return total.FormattedValue == calculatedTotal.FormattedValue;
    }

    private static bool ShouldStopIfStopWord(ReceiptTextLine line) =>
        Options.StopKeywords.HasMatch(line.Text);

    // ─── Total label ──────────────────────────────────────────────

    private static bool TryParseTotalLabel(
        ReceiptTextLine line, List<IRecognizedEntity> parsed, float rightBound)
    {
        if (RightL(line) > rightBound) return false;
        if (Options.TotalLabels.Mapping.Count == 0) return false;

        var label = FindTotalLabelLike(line.Text);
        var canonical = label != null && Options.TotalLabels.Mapping.TryGetValue(label, out var v)
            ? v : label;

        if (canonical != null)
        {
            parsed.RemoveAll(e => e is RecognizedTotalLabel);
            parsed.Add(new RecognizedTotalLabel(canonical, line));
            return true;
        }
        return false;
    }

    private static bool IsTotalLabelLike(string text) => FindTotalLabelLike(text) != null;

    private static string? FindTotalLabelLike(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        string? bestLabel = null;
        int bestScore = 0;
        foreach (var label in Options.TotalLabels.Mapping.Keys)
        {
            var normText = ReceiptNormalizer.NormalizeKey(text).ToLowerInvariant();
            if (normText.StartsWith(label, StringComparison.Ordinal) &&
                (label.Length > 5 || normText.Length < label.Length << 1))
            {
                return label;
            }

            var s = Fuzz.Ratio(normText, label);
            if (s > bestScore && normText.Length >= label.Length)
            {
                bestScore = s;
                bestLabel = label;
            }
        }
        if (bestLabel == null) return null;
        var threshold = bestLabel.Length > 5 ? 90 : 95;
        if (bestScore < threshold) return null;
        return bestLabel;
    }

    // ─── Store ────────────────────────────────────────────────────

    private static bool TryParseStore(ReceiptTextLine line, List<IRecognizedEntity> parsed)
    {
        var store = FindStore(parsed);
        if (store != null) return false;
        var amount = FindAmount(parsed);
        if (amount != null) return false;
        var text = ReceiptFormatter.Trim(line.Text);
        var customStore = Options.StoreNames.Detect(text);
        if (customStore == null) return false;
        parsed.Add(new RecognizedStore(customStore, line));
        return true;
    }

    // ─── Total identification ─────────────────────────────────────

    private static bool TryIdentifyTotal(
        ReceiptTextLine line, List<IRecognizedEntity> parsed, float rightBound)
    {
        var totalLabel = FindTotalLabel(parsed);
        if (totalLabel == null) return false;
        var amounts = parsed.OfType<RecognizedAmount>().ToList();
        var closestAmount = FindClosestTotalAmount(totalLabel, amounts);
        if (closestAmount != null)
        {
            ReplaceWhere(parsed,
                e => e is RecognizedTotal,
                e => ToAmount((RecognizedTotal)e));
            ReplaceWhere(parsed,
                e => ReferenceEquals(e, closestAmount),
                e => ToTotal((RecognizedAmount)e));
        }
        return false;
    }

    // ─── Amount parsing ───────────────────────────────────────────

    private static bool TryParseAmount(
        ReceiptTextLine line, List<IRecognizedEntity> parsed, float rightBound)
    {
        if (RightL(line) <= rightBound) return false;
        var normalizedText = ReceiptNormalizer.NormalizeFullWidth(line.Text);
        var match = AmountPattern.Match(normalizedText);
        if (!match.Success) return false;
        var amount = match.Value;
        var value = ConvertToDouble(amount);
        if (value == null || value == 0) return false;
        parsed.Add(new RecognizedAmount(value.Value, line));
        return true;
    }

    // ─── Unit parsing ─────────────────────────────────────────────

    private static bool TryParseUnit(
        ReceiptTextLine line, List<IRecognizedEntity> parsed,
        float rightBound, float centerBound)
    {
        if (RightL(line) > rightBound) return false;
        var text = ReceiptNormalizer.NormalizeFullWidth(line.Text);
        var qMatch = QuantityPattern.Match(text);
        var aMatch = AmountPattern.Match(text);
        var quantity = qMatch.Success ? qMatch.Value : null;
        var amount = aMatch.Success ? aMatch.Value : null;

        int? unitQuantity = null;
        if (quantity != null)
            unitQuantity = ConvertToInteger(quantity);

        double? unitPrice = null;
        if (amount != null)
            unitPrice = ConvertToDouble(amount);

        if (unitQuantity == null && unitPrice == null) return false;

        if (unitQuantity != null)
            parsed.Add(new RecognizedUnitQuantity(unitQuantity.Value, line));

        if (unitPrice != null)
            parsed.Add(new RecognizedUnitPrice(unitPrice.Value, line));

        var qStart = qMatch.Success ? qMatch.Index : text.Length;
        var aStart = aMatch.Success ? aMatch.Index : text.Length;
        var aEnd = aMatch.Success ? aMatch.Index + aMatch.Length : text.Length;

        var leadingPart = qStart == 0 && aMatch.Success
            ? text[aEnd..].Trim()
            : text[..Math.Min(qStart, aStart)].Trim();

        var minLen = unitPrice?.ToString().Length ?? 0;
        var hasMatch = aMatch.Success;
        var isTrailing = hasMatch && text[aEnd..].Trim().Length == text.Trim().Length;
        var isBetween = hasMatch
            && text[..aStart].Trim().Length >= minLen
            && text[aEnd..].Trim().Length >= minLen;

        var modified = ReceiptTextLine.FromLine(line).CopyWith(
            text: isTrailing || isBetween ? text : leadingPart);
        TryParseUnknown(modified, parsed, centerBound);

        return true;
    }

    // ─── Unknown parsing ──────────────────────────────────────────

    private static bool TryParseUnknown(
        ReceiptTextLine line, List<IRecognizedEntity> parsed, float centerBound)
    {
        if (CxL(line) > centerBound) return false;
        var unknown = line.Text;
        var numeric = ConvertToDouble(unknown)?.ToString() ?? "";
        if ((double)numeric.Length / unknown.Length < 0.5)
        {
            parsed.Add(new RecognizedUnknown(unknown, line));
            return true;
        }
        return false;
    }

    // ─── Bounds ───────────────────────────────────────────────────

    private static bool ApplyBounds(List<ReceiptTextLine> lines, List<IRecognizedEntity> parsed)
    {
        var line = new ReceiptTextLine(
            boundingBox: ExtractRectFromLines(lines),
            angle: 0.0);
        parsed.Add(new RecognizedBounds(line.BoundingBox, line));
        return true;
    }

    // ─── Numeric conversion ───────────────────────────────────────

    private static int? ConvertToInteger(string input)
    {
        var m = IntegerExtractPattern.Match(input);
        return m.Success ? int.TryParse(m.Value, out var v) ? v : null : null;
    }

    private static double? ConvertToDouble(string input)
    {
        var normalized = ReceiptNormalizer.NormalizeFullWidth(input)
            .Replace("\u2212", "-").Replace("\u2013", "-").Replace("\u2014", "-").Replace("-", "-")
            .Replace("\u00a5", "").Replace("\uffe5", "")
            .Replace("\u5186", ""); // 円

        var commaMatch = Regex.Match(normalized, @",(\d+)");
        var decimalResolved = commaMatch.Success && commaMatch.Groups[1].Value.Length == 3
            ? normalized.Replace(",", "")
            : Regex.Replace(normalized, @"[.,\u201a\u060c\u066b\u00b7]", ".");

        var cleaned = Regex.Replace(decimalResolved, @"[^-0-9.]", "");
        return double.TryParse(cleaned.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    // ─── ReplaceWhere ─────────────────────────────────────────────

    private static int ReplaceWhere<T>(
        List<T> list, Func<T, bool> test, Func<T, T> replace)
    {
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (test(list[i]))
            {
                list[i] = replace(list[i]);
                count++;
            }
        }
        return count;
    }

    // ─── Bounding rect ───────────────────────────────────────────

    private static BoundingBox ExtractRectFromLines(List<ReceiptTextLine> lines)
    {
        if (lines.Count == 0) return default;
        float minX = LeftL(lines[0]);
        float maxX = RightL(lines[0]);
        float minY = TopL(lines[0]);
        float maxY = BottomL(lines[0]);
        for (int i = 1; i < lines.Count; i++)
        {
            var l = lines[i];
            var lft = LeftL(l); var rgt = RightL(l);
            var tp = TopL(l); var bt = BottomL(l);
            if (lft < minX) minX = lft;
            if (rgt > maxX) maxX = rgt;
            if (tp < minY) minY = tp;
            if (bt > maxY) maxY = bt;
        }
        return BoundingBox.FromLTRB(minX, minY, maxX, maxY);
    }

    // ─── Date extraction ──────────────────────────────────────────

    private static bool ApplyPurchaseDate(List<ReceiptTextLine> lines, List<IRecognizedEntity> parsed)
    {
        var purchaseDate = ExtractDateFromLines(lines);
        if (purchaseDate == null) return false;
        parsed.Add(purchaseDate);
        return true;
    }

    private static RecognizedPurchaseDate? ExtractDateFromLines(List<ReceiptTextLine> lines)
    {
        Regex[] patterns =
        [
            DateJapaneseEra, DateKanji, DateIsoYMD,
            DateYearMonthDayNumeric, DateDayMonthYearNumeric,
            DateMonthDayYearEn, DateDayMonthYearEn, DateDayMonthYearDe,
        ];

        foreach (var line in lines)
        {
            var t = ReceiptNormalizer.NormalizeFullWidth(line.Text);
            foreach (var p in patterns)
            {
                foreach (Match m in p.Matches(t))
                {
                    if (m.Groups.Count < 2) continue;
                    var s = m.Groups[1].Value;
                    DateTime? dt;
                    if (ReferenceEquals(p, DateJapaneseEra))
                        dt = ReceiptFormatter.ParseJapaneseEraDate(s);
                    else if (ReferenceEquals(p, DateKanji))
                        dt = ReceiptFormatter.ParseKanjiDate(s);
                    else if (ReferenceEquals(p, DateIsoYMD) || ReferenceEquals(p, DateYearMonthDayNumeric))
                        dt = ReceiptFormatter.ParseNumericYMD(s);
                    else if (ReferenceEquals(p, DateDayMonthYearNumeric))
                        dt = ReceiptFormatter.ParseNumericDMY(s);
                    else if (ReferenceEquals(p, DateMonthDayYearEn))
                        dt = ReceiptFormatter.ParseNameMDY(s);
                    else
                        dt = ReceiptFormatter.ParseNameDMY(s);

                    if (dt != null)
                        return new RecognizedPurchaseDate(dt.Value, line);
                }
            }
        }
        return null;
    }

    // ─── Entity finders ───────────────────────────────────────────

    private static RecognizedStore? FindStore(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedStore>().FirstOrDefault();

    private static RecognizedPurchaseDate? FindPurchaseDate(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedPurchaseDate>().FirstOrDefault();

    private static RecognizedBounds? FindBounds(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedBounds>().FirstOrDefault();

    private static RecognizedTotalLabel? FindTotalLabel(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedTotalLabel>().LastOrDefault();

    private static RecognizedTotal? FindTotal(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedTotal>().LastOrDefault();

    private static RecognizedAmount? FindAmount(List<IRecognizedEntity> entities) =>
        entities.OfType<RecognizedAmount>().LastOrDefault();

    // ─── Receipt field processors ─────────────────────────────────

    private static void ProcessTotalLabel(RecognizedTotalLabel? totalLabel, RecognizedReceipt receipt) =>
        receipt.TotalLabel = totalLabel;

    private static void ProcessTotal(RecognizedTotal? total, RecognizedReceipt receipt) =>
        receipt.Total = total;

    private static void ProcessPurchaseDate(RecognizedPurchaseDate? purchaseDate, RecognizedReceipt receipt) =>
        receipt.PurchaseDate = purchaseDate;

    private static void ProcessBounds(RecognizedBounds? bounds, RecognizedReceipt receipt) =>
        receipt.Bounds = bounds;

    private static void ProcessStore(RecognizedStore? store, RecognizedReceipt receipt) =>
        receipt.Store = store;

    // ─── Position / Amount processing ─────────────────────────────

    private static void ProcessAmounts(
        List<IRecognizedEntity> entities,
        List<RecognizedUnknown> yUnknowns,
        List<RecognizedUnitPrice> yUnitPrices,
        List<RecognizedUnitQuantity> yUnitQuantities,
        RecognizedReceipt receipt)
    {
        var timestamp = receipt.Timestamp;
        var amounts = entities.OfType<RecognizedAmount>().ToList();
        var positions = new List<RecognizedPosition?>();
        foreach (var amount in amounts)
            positions.Add(CreatePositionForAmount(amount, yUnknowns, timestamp));

        int idx = 0;
        foreach (var amount in amounts)
        {
            if (positions[idx] == null)
            {
                positions[idx] = CreatePositionForAmount(
                    amount, yUnknowns, timestamp, strict: false);
            }
            idx++;
        }

        positions.RemoveAll(p => IsTotalLabelLike(p?.Product.Line.Text ?? ""));
        receipt.Positions.AddRange(positions.Where(p => p != null).Cast<RecognizedPosition>());
        AssignUnitToPositions(yUnitPrices, yUnitQuantities, receipt);
    }

    // ─── Unit processing ──────────────────────────────────────────

    private static RecognizedUnit? ProcessUnit(
        RecognizedProduct product,
        List<RecognizedProduct> products,
        List<RecognizedUnitPrice> yUnitPrices,
        List<RecognizedUnitQuantity> yUnitQuantities,
        bool lineAbove = false,
        bool lineBelow = true)
    {
        if (product.Position == null) return null;

        var yUnitPrice = FindClosestEntity(
            product, yUnitPrices.Cast<IRecognizedEntity>().ToList(),
            lineAbove: lineAbove, lineBelow: lineBelow,
            crossCheckEntities: products.Cast<IRecognizedEntity>().ToList()) as RecognizedUnitPrice;

        var yUnitQuantity = FindClosestEntity(
            product, yUnitQuantities.Cast<IRecognizedEntity>().ToList(),
            lineAbove: lineAbove, lineBelow: lineBelow,
            crossCheckEntities: products.Cast<IRecognizedEntity>().ToList()) as RecognizedUnitQuantity;

        var tolerance = Options.Tuning.OptimizerTotalTolerance;
        var defaultLine = product.Line;
        var defaultPrice = product.Position!.Price.Value;
        const int defaultQuantity = 1;

        var sign = (yUnitPrice?.Value ?? 0) > 0 && defaultPrice < 0 ? -1.0 : 1.0;
        var unitPrice = sign * (yUnitPrice?.Value ?? defaultPrice);
        var unitQuantity = yUnitQuantity?.Value ?? defaultQuantity;

        if (yUnitPrice != null) yUnitPrices.Remove(yUnitPrice);
        if (yUnitQuantity != null) yUnitQuantities.Remove(yUnitQuantity);

        if (unitPrice != defaultPrice && unitQuantity != defaultQuantity)
        {
            if (IsClose(unitPrice * unitQuantity, defaultPrice, tolerance))
                return RecognizedUnit.FromNumbers(unitQuantity, unitPrice, defaultLine);
        }

        if (unitPrice != defaultPrice)
        {
            var qty = (int)Math.Round(defaultPrice / unitPrice);
            if (qty > 0 && IsClose(qty * unitPrice, defaultPrice, tolerance))
                return RecognizedUnit.FromNumbers(qty, unitPrice, defaultLine);
        }

        if (unitQuantity != defaultQuantity)
        {
            var price = Math.Round(defaultPrice / unitQuantity * 100) / 100;
            if (price != 0 && IsClose(unitQuantity * price, defaultPrice, tolerance))
            {
                var unitPriceFromTotal = defaultPrice / unitQuantity;
                return RecognizedUnit.FromNumbers(unitQuantity, unitPriceFromTotal, defaultLine);
            }
        }

        return RecognizedUnit.FromNumbers(defaultQuantity, defaultPrice, defaultLine);
    }

    private static bool IsClose(double a, double b, double tolerance) =>
        Math.Abs(a - b) < tolerance;

    private static void AssignUnitToPositions(
        List<RecognizedUnitPrice> yUnitPrices,
        List<RecognizedUnitQuantity> yUnitQuantities,
        RecognizedReceipt receipt)
    {
        var positions = receipt.Positions;
        var unitCountAbove = UnitCount(positions, yUnitPrices, yUnitQuantities,
            lineAbove: true, lineBelow: false);
        var unitCountBelow = UnitCount(positions, yUnitPrices, yUnitQuantities,
            lineAbove: false, lineBelow: true);
        var products = positions.Select(p => p.Product).ToList();
        foreach (var position in positions)
        {
            var unit = ProcessUnit(
                position.Product, products, yUnitPrices, yUnitQuantities,
                lineAbove: unitCountAbove > unitCountBelow,
                lineBelow: unitCountAbove <= unitCountBelow);
            if (unit != null) position.Unit = unit;
        }
    }

    private static int UnitCount(
        List<RecognizedPosition> positions,
        List<RecognizedUnitPrice> yUnitPrices,
        List<RecognizedUnitQuantity> yUnitQuantities,
        bool lineAbove = false, bool lineBelow = false)
    {
        var products = positions.Select(p => p.Product).ToList();
        var unitPrices = new List<RecognizedUnitPrice>(yUnitPrices);
        var unitQuantities = new List<RecognizedUnitQuantity>(yUnitQuantities);
        int count = 0;
        foreach (var position in positions)
        {
            var unit = ProcessUnit(
                position.Product, products, unitPrices, unitQuantities,
                lineAbove: lineAbove, lineBelow: lineBelow);
            if (unit != null && unit.Quantity.Value > 1) count++;
        }
        return count;
    }

    // ─── Position creation ────────────────────────────────────────

    private static RecognizedPosition? CreatePositionForAmount(
        RecognizedAmount amount, List<RecognizedUnknown> yUnknowns,
        DateTime timestamp, bool strict = true)
    {
        SortByDistance(amount.Line.BoundingBox, yUnknowns);
        foreach (var yUnknown in yUnknowns.ToList())
        {
            if (IsMatchingUnknown(amount, yUnknown, strict: strict) &&
                ReferenceEquals(
                    FindClosestEntity(amount, yUnknowns.Cast<IRecognizedEntity>().ToList(),
                        lineAbove: !strict),
                    yUnknown))
            {
                var position = CreatePosition(yUnknown, amount, timestamp);
                yUnknowns.RemoveAll(e => ReferenceEquals(e, yUnknown));
                return position;
            }
        }
        return null;
    }

    private static bool IsMatchingUnknown(
        RecognizedAmount amount, RecognizedUnknown unknown, bool strict = true)
    {
        var unknownText = ReceiptFormatter.Trim(unknown.Value);
        if (IsTotalLabelLike(unknownText)) return false;

        var isLeftOfAmount = Right(unknown) <= Left(amount);
        var alignedVertically = Math.Abs(Dy(amount.Line, unknown.Line)) <= HeightL(amount.Line);

        return isLeftOfAmount && (alignedVertically || !strict);
    }

    private static RecognizedPosition CreatePosition(
        RecognizedUnknown unknown, RecognizedAmount amount, DateTime timestamp)
    {
        var product = new RecognizedProduct(unknown.Value, unknown.Line, options: Options);
        var price = new RecognizedPrice(amount.Value, amount.Line);
        var position = new RecognizedPosition(product, price, timestamp, Operation.None);
        product.Position = position;
        price.Position = position;
        return position;
    }

    // ─── Geometric matching ───────────────────────────────────────

    private static IRecognizedEntity? FindClosestEntity(
        IRecognizedEntity entity, List<IRecognizedEntity> entities,
        bool lineAbove = false, bool lineBelow = false,
        List<IRecognizedEntity>? crossCheckEntities = null)
    {
        crossCheckEntities ??= [];
        IRecognizedEntity? best = null;
        double bestScore = double.PositiveInfinity;
        var line = entity.Line;
        foreach (var e in entities)
        {
            var s = DistanceScore(line, e.Line, lineAbove: lineAbove, lineBelow: lineBelow);
            if (s < bestScore)
            {
                bestScore = s;
                best = e;
            }
        }
        if (best == null || double.IsPositiveInfinity(bestScore)) return null;
        if (crossCheckEntities.Count > 0)
        {
            var bothDirection = lineAbove && lineBelow;
            var crossLineAbove = bothDirection || !lineAbove;
            var crossLineBelow = bothDirection || !lineBelow;
            var crossCheckEntity = FindClosestEntity(
                best, crossCheckEntities,
                lineAbove: crossLineAbove, lineBelow: crossLineBelow);
            if (!ReferenceEquals(entity, crossCheckEntity)) return null;
        }
        return best;
    }

    private static RecognizedAmount? FindClosestTotalAmount(
        RecognizedTotalLabel totalLabel, List<RecognizedAmount> amounts)
    {
        var entity = FindClosestEntity(
            totalLabel, amounts.Cast<IRecognizedEntity>().ToList(), lineBelow: true);
        return entity as RecognizedAmount;
    }

    private static double DistanceScore(
        ReceiptTextLine sourceLine, ReceiptTextLine targetLine,
        bool lineAbove = false, bool lineBelow = false)
    {
        if (ReferenceEquals(sourceLine, targetLine)) return double.PositiveInfinity;

        var tol = Math.Max(HeightL(sourceLine), HeightL(targetLine));
        var srcTop = TopL(sourceLine) - (lineAbove ? tol : 0);
        var srcBottom = BottomL(sourceLine) + (lineBelow ? tol : 0);

        var targetCy = CyL(targetLine);
        var isSameLine = targetCy >= srcTop && targetCy <= srcBottom;

        return isSameLine ? Math.Abs(Dy(sourceLine, targetLine)) : double.PositiveInfinity;
    }

    private static void SortByDistance(BoundingBox amountBox, List<RecognizedUnknown> entities)
    {
        var amountYCtr = CyR(amountBox);
        entities.Sort((a, b) =>
        {
            var dyA = Math.Abs(Cy(a) - amountYCtr);
            var dyB = Math.Abs(Cy(b) - amountYCtr);
            var vc = dyA.CompareTo(dyB);
            return vc != 0 ? vc : Cx(a).CompareTo(Cx(b));
        });
    }

    // ─── Entity filtering ─────────────────────────────────────────

    private static List<IRecognizedEntity> FilterEntities(List<IRecognizedEntity> entities)
    {
        RecognizedUnknown? leftUnknown = null;
        float leftmostX = float.MaxValue;
        RecognizedAmount? rightAmount = null;
        float rightmostX = 0;
        RecognizedAmount? firstAmount = null;

        foreach (var e in entities)
        {
            if (e is RecognizedUnknown u)
            {
                var lx = Left(e);
                if (lx < leftmostX)
                {
                    leftmostX = lx;
                    leftUnknown = u;
                }
            }
            else if (e is RecognizedAmount a)
            {
                firstAmount ??= a;
                var rx = Right(e);
                if (rx > rightmostX)
                {
                    rightmostX = rx;
                    rightAmount = a;
                }
            }
        }
        if (leftUnknown == null || rightAmount == null) return entities;

        var totalLabel = FindTotalLabel(entities);
        var total = FindTotal(entities);

        var filtered = new List<IRecognizedEntity>();
        foreach (var entity in entities)
        {
            if (entity is RecognizedStore)
            {
                if (firstAmount != null && Cy(entity) < Top(firstAmount))
                    filtered.Add(entity);
                continue;
            }
            else if (entity is RecognizedBounds or RecognizedPurchaseDate
                     or RecognizedUnitPrice or RecognizedUnitQuantity)
            {
                filtered.Add(entity);
                continue;
            }
            else if (entity is RecognizedTotalLabel tl)
            {
                if (ReferenceEquals(entity, totalLabel))
                    filtered.Add(entity);
                else
                    filtered.Add(new RecognizedUnknown(tl.Value, tl.Line));
                continue;
            }
            else if (entity is RecognizedTotal tot)
            {
                if (ReferenceEquals(entity, total))
                    filtered.Add(entity);
                else
                    filtered.Add(new RecognizedAmount(tot.Value, tot.Line));
                continue;
            }

            var horizontallyBetween =
                Left(entity) > Right(leftUnknown) &&
                Right(entity) < Left(rightAmount);

            var dyU = Math.Abs(Cy(entity) - Cy(leftUnknown));
            var dyA = Math.Abs(Cy(entity) - Cy(rightAmount));
            var verticallyAligned =
                dyU < HeightL(leftUnknown.Line) || dyA < HeightL(rightAmount.Line);

            var betweenUnknownAndAmount = horizontallyBetween && verticallyAligned;

            var belowTotalLabelAndTotal =
                totalLabel != null && total != null &&
                Cy(entity) > Bottom(totalLabel) &&
                Cy(entity) > Bottom(total);

            if (!betweenUnknownAndAmount && !belowTotalLabelAndTotal)
                filtered.Add(entity);
        }
        return filtered;
    }

    // ─── Receipt building ─────────────────────────────────────────

    private static RecognizedReceipt BuildReceipt(List<IRecognizedEntity> entities)
    {
        var yUnknowns = entities.OfType<RecognizedUnknown>().ToList();
        var yUnitPrices = entities.OfType<RecognizedUnitPrice>().ToList();
        var yUnitQuantities = entities.OfType<RecognizedUnitQuantity>().ToList();
        var receipt = RecognizedReceipt.Empty();
        var store = FindStore(entities);
        var totalLabel = FindTotalLabel(entities);
        var total = FindTotal(entities);
        var purchaseDate = FindPurchaseDate(entities);
        var bounds = FindBounds(entities);

        ProcessStore(store, receipt);
        ProcessTotalLabel(totalLabel, receipt);
        ProcessTotal(total, receipt);
        ProcessPurchaseDate(purchaseDate, receipt);
        ProcessBounds(bounds, receipt);
        ProcessAmounts(entities, yUnknowns, yUnitPrices, yUnitQuantities, receipt);

        return receipt.CopyWith(entities: entities);
    }
}
