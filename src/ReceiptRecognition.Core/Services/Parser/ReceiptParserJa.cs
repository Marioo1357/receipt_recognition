using System.Text.RegularExpressions;
using ReceiptRecognition.Core.Models;
using ReceiptRecognition.Core.Services.Ocr;
using ReceiptRecognition.Core.Utils.Configuration;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Services.Parser;

/// <summary>
/// Japanese receipt parser using a row-grouping strategy.
///
/// Groups <see cref="ReceiptTextLine"/>s into rows by Y-coordinate proximity, then extracts
/// prices and product names from each row. This handles both separate and
/// combined product-name/price lines that are common in Japanese receipts.
///
/// Falls back to the same <see cref="RecognizedReceipt"/> output format as
/// <see cref="ReceiptParser"/> so the optimizer and validation pipeline work unchanged.
/// </summary>
public static class ReceiptParserJa
{
    // ─── Japanese price patterns ──────────────────────────────────

    /// <summary>Matches Japanese yen amounts: ¥198, ￥1,280, ¥702※, etc.</summary>
    private static readonly Regex YenPrefix = new(
        @"[\u00a5\uffe5]\s*([\d,\s]+)",
        RegexOptions.Compiled);

    /// <summary>Matches amounts with trailing 円: 198円, 1,280円.</summary>
    private static readonly Regex YenSuffix = new(
        @"(\d[\d,]*)\s*\u5186",
        RegexOptions.Compiled);

    /// <summary>Matches standalone discount lines: -100, −200, –300.</summary>
    private static readonly Regex Discount = new(
        @"^[-\u2212\u2013\u2014]\s*([\d,]+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Fallback: standalone price-like numbers without ¥/円.
    /// Handles OCR artifacts: leading *, +; trailing >, %, ), X, *.
    /// Requires 2+ digits to avoid matching quantity "1".
    /// </summary>
    private static readonly Regex StandalonePrice = new(
        @"^[*+]?(\d[\d,]{1,5})[>%\)X\u203b\uff1e\*]*$",
        RegexOptions.Compiled);

    /// <summary>
    /// Garbled ¥ prefix: OCR commonly reads ¥ as digit 4.
    /// "4702%" → ¥702※ (price = 702).
    /// </summary>
    private static readonly Regex YenGarbled = new(
        @"^4(\d[\d,]{0,5})[%\)>\u203b\uff1e\*X]+$",
        RegexOptions.Compiled);

    /// <summary>
    /// Matches tax rate indicator lines: "(8% 軽)", "(10% 標)", etc.
    /// </summary>
    private static readonly Regex TaxRate = new(
        @"^\(?\d{1,2}%",
        RegexOptions.Compiled);

    /// <summary>Active options from <see cref="ReceiptRuntime"/>.</summary>
    private static ReceiptOptions Options => ReceiptRuntime.Options;

    // ─── Entry point ──────────────────────────────────────────────

    /// <summary>Parses <paramref name="text"/> using the row-grouping strategy and returns a receipt.</summary>
    public static RecognizedReceipt ProcessText(OcrResult text, ReceiptOptions options)
    {
        return ReceiptRuntime.RunWithOptions(options, () =>
        {
            if (text.Blocks.Count == 0) return RecognizedReceipt.Empty();

            var allLines = new List<ReceiptTextLine>();
            foreach (var block in text.Blocks)
                allLines.AddRange(block.Lines);

            if (allLines.Count == 0) return RecognizedReceipt.Empty();

            var rotated = IsRotated(allLines);

            allLines.Sort((a, b) =>
            {
                var ab = a.BoundingBox;
                var bb = b.BoundingBox;
                float p1, s1, p2, s2;
                if (rotated)
                { p1 = ab.Left; s1 = ab.Top; p2 = bb.Left; s2 = bb.Top; }
                else
                { p1 = ab.Top; s1 = ab.Left; p2 = bb.Top; s2 = bb.Left; }
                var c = p1.CompareTo(p2);
                return c != 0 ? c : s1.CompareTo(s2);
            });

            var entities = new List<IRecognizedEntity>();

            var purchaseDate = ExtractDate(allLines);
            if (purchaseDate != null) entities.Add(purchaseDate);

            var bounds = ExtractBounds(allLines);
            entities.Add(bounds);

            var rows = GroupIntoRows(allLines, rotated: rotated);

            var store = ExtractStore(rows);
            if (store != null) entities.Add(store);

            var timestamp = DateTime.Now;
            var positions = new List<RecognizedPosition>();
            RecognizedTotal? total = null;
            RecognizedTotalLabel? totalLabel = null;
            var foundAnyTotal = false;

            foreach (var row in rows)
            {
                var rowText = string.Join(" ", row.Select(l => l.Text));
                var normalizedRowText = ReceiptNormalizer.NormalizeFullWidth(rowText);

                var cleanedRowText = normalizedRowText
                    .Replace("\u203b", "")
                    .Replace("  ", " ").Replace("  ", " ").Trim();
                cleanedRowText = Regex.Replace(cleanedRowText, @"\s+", " ").Trim();

                if (Options.StopKeywords.HasMatch(cleanedRowText)) break;

                if (!string.IsNullOrEmpty(cleanedRowText) &&
                    Options.IgnoreKeywords.HasMatch(cleanedRowText))
                    continue;

                if (purchaseDate != null &&
                    row.Any(l => ReferenceEquals(l, purchaseDate.Line)))
                    continue;

                if (TaxRate.IsMatch(cleanedRowText)) continue;

                if (Options.TotalLabels.HasMatch(cleanedRowText))
                {
                    var label = Options.TotalLabels.Detect(cleanedRowText);
                    if (label != null)
                    {
                        var labelLine = row[0];
                        totalLabel = new RecognizedTotalLabel(label, labelLine);
                        entities.Add(totalLabel);

                        var price = ExtractPrice(row);
                        if (price != null)
                        {
                            total = new RecognizedTotal(price.Value, PriceLine(row));
                            entities.Add(total);
                        }
                        foundAnyTotal = true;
                        continue;
                    }
                }

                if (foundAnyTotal) continue;

                if (cleanedRowText.StartsWith('(')) continue;

                var discountVal = ExtractDiscount(row);
                if (discountVal != null)
                {
                    var name = ExtractProductName(row);
                    var pos = CreatePosition(
                        name: name, row: row,
                        price: -discountVal.Value, timestamp: timestamp);
                    positions.Add(pos);
                    entities.Add(new RecognizedAmount(-discountVal.Value, pos.Price.Line));
                    entities.Add(new RecognizedUnknown(name, pos.Product.Line));
                    continue;
                }

                var itemPrice = ExtractPrice(row);
                if (itemPrice == null) continue;

                var productName = ExtractProductName(row);
                if (string.IsNullOrEmpty(productName) || !IsMeaningfulName(productName))
                    continue;

                var position = CreatePosition(
                    name: productName, row: row,
                    price: itemPrice.Value, timestamp: timestamp);
                positions.Add(position);
                entities.Add(new RecognizedAmount(itemPrice.Value, position.Price.Line));
                entities.Add(new RecognizedUnknown(productName, position.Product.Line));
            }

            if (total == null && positions.Count > 0)
            {
                total = EstimateTotal(positions, allLines);
                if (total != null) entities.Add(total);
            }

            var receipt = RecognizedReceipt.Empty();
            receipt.Store = store;
            receipt.TotalLabel = totalLabel;
            receipt.Total = total;
            receipt.PurchaseDate = purchaseDate;
            receipt.Bounds = bounds;
            receipt.Positions.AddRange(positions);

            return receipt.CopyWith(entities: entities);
        });
    }

    // ─── Rotation Detection ──────────────────────────────────────

    private static bool IsRotated(List<ReceiptTextLine> lines)
    {
        if (lines.Count < 3) return false;
        var sampleSize = Math.Clamp(lines.Count, 0, 10);
        var verticalCount = 0;
        for (var i = 0; i < sampleSize; i++)
        {
            var bb = lines[i].BoundingBox;
            if (bb.Height > bb.Width * 1.5f) verticalCount++;
        }
        return verticalCount > sampleSize / 2;
    }

    // ─── Row Grouping ─────────────────────────────────────────────

    private static List<List<ReceiptTextLine>> GroupIntoRows(
        List<ReceiptTextLine> lines, bool rotated = false)
    {
        if (lines.Count == 0) return [];

        var rows = new List<List<ReceiptTextLine>>();
        var currentRow = new List<ReceiptTextLine> { lines[0] };
        var currentPos = rotated ? lines[0].BoundingBox.Left : lines[0].BoundingBox.Top;

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var pos = rotated ? line.BoundingBox.Left : line.BoundingBox.Top;

            var rowBb = currentRow[0].BoundingBox;
            var lineBb = line.BoundingBox;
            var rowSize = rotated ? rowBb.Width : rowBb.Height;
            var lineSize = rotated ? lineBb.Width : lineBb.Height;
            var maxSize = rowSize > lineSize ? rowSize : lineSize;
            var tolerance = maxSize * 0.6f;

            if (Math.Abs(pos - currentPos) <= tolerance)
            {
                currentRow.Add(line);
            }
            else
            {
                rows.Add(currentRow);
                currentRow = [line];
                currentPos = pos;
            }
        }
        rows.Add(currentRow);

        foreach (var row in rows)
        {
            row.Sort((a, b) => rotated
                ? a.BoundingBox.Top.CompareTo(b.BoundingBox.Top)
                : a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return rows;
    }

    // ─── Price Extraction ─────────────────────────────────────────

    private static double? ExtractPrice(List<ReceiptTextLine> row)
    {
        // First pass: explicit ¥/円 patterns.
        foreach (var line in row)
        {
            var normalized = ReceiptNormalizer.NormalizeFullWidth(line.Text)
                .Replace("\u203b", "")
                .Replace("\uff1e", "").Replace(">", "").Replace("X", "")
                .Trim();

            var prefixMatch = YenPrefix.Match(normalized);
            if (prefixMatch.Success)
            {
                var priceStr = Regex.Replace(prefixMatch.Groups[1].Value, @"[\s,]", "");
                if (double.TryParse(priceStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var price) && price > 0)
                    return price;
            }

            var suffixMatch = YenSuffix.Match(normalized);
            if (suffixMatch.Success)
            {
                var priceStr = suffixMatch.Groups[1].Value.Replace(",", "");
                if (double.TryParse(priceStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var price) && price > 0)
                    return price;
            }
        }

        // Second pass: garbled ¥ prefix (OCR reads ¥ as 4).
        foreach (var line in row)
        {
            var normalized = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            var garbMatch = YenGarbled.Match(normalized);
            if (garbMatch.Success)
            {
                var priceStr = garbMatch.Groups[1].Value.Replace(",", "");
                if (double.TryParse(priceStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var price) && price > 0)
                    return price;
            }
        }

        // Third pass: standalone number patterns (last line = rightmost = price column).
        if (row.Count > 0)
        {
            var lastLine = row[^1];
            var normalized = ReceiptNormalizer.NormalizeFullWidth(lastLine.Text).Trim();
            var m = StandalonePrice.Match(normalized);
            if (m.Success)
            {
                var priceStr = m.Groups[1].Value.Replace(",", "");
                if (double.TryParse(priceStr, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var price) && price >= 10)
                    return price;
            }
        }
        return null;
    }

    private static double? ExtractDiscount(List<ReceiptTextLine> row)
    {
        foreach (var line in row)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            var m = Discount.Match(text);
            if (m.Success)
            {
                if (double.TryParse(m.Groups[1].Value.Replace(",", ""),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var val))
                    return val;
            }
        }
        return null;
    }

    // ─── Product Name Extraction ──────────────────────────────────

    private static string ExtractProductName(List<ReceiptTextLine> row)
    {
        var names = new List<string>();
        foreach (var line in row)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            if (IsPriceLine(text)) continue;
            if (Discount.IsMatch(text)) continue;
            var cleaned = RemovePriceFromText(text);
            if (!string.IsNullOrEmpty(cleaned)) names.Add(cleaned);
        }
        return string.Join(" ", names).Trim();
    }

    /// <summary>Common OCR artifacts adjacent to prices on Japanese receipts.</summary>
    private static readonly Regex PriceArtifacts = new(
        @"^[\u203b\uff1e>%\)\*X\s]*$",
        RegexOptions.Compiled);

    private static bool IsPriceLine(string text)
    {
        var stripped = text
            .Replace("\u203b", "")
            .Replace("\uff1e", "").Replace(">", "").Replace("X", "")
            .Trim();

        var prefixMatch = YenPrefix.Match(stripped);
        if (prefixMatch.Success)
        {
            var remainder = YenPrefix.Replace(stripped, "", 1).Trim();
            return string.IsNullOrEmpty(remainder) || PriceArtifacts.IsMatch(remainder);
        }

        var suffixMatch = YenSuffix.Match(stripped);
        if (suffixMatch.Success)
        {
            var remainder = YenSuffix.Replace(stripped, "", 1).Trim();
            return string.IsNullOrEmpty(remainder) || PriceArtifacts.IsMatch(remainder);
        }

        if (StandalonePrice.IsMatch(stripped)) return true;

        return false;
    }

    private static string RemovePriceFromText(string text)
    {
        var result = text;
        result = YenPrefix.Replace(result, "");
        result = YenSuffix.Replace(result, "");
        result = StandalonePrice.Replace(result, "");
        result = result
            .Replace("\u203b", "")
            .Replace("\uff1e", "").Replace(">", "")
            .Replace("%", "").Replace(")", "").Replace("*", "").Replace("X", "");
        result = Regex.Replace(result, @"\s+", " ").Trim();
        return result;
    }

    // ─── Position Factory ─────────────────────────────────────────

    private static RecognizedPosition CreatePosition(
        string name, List<ReceiptTextLine> row,
        double price, DateTime timestamp)
    {
        var product = new RecognizedProduct(name, ProductLine(row), options: Options);
        var recognizedPrice = new RecognizedPrice(price, PriceLine(row));
        var position = new RecognizedPosition(
            product, recognizedPrice, timestamp, Operation.None);
        product.Position = position;
        recognizedPrice.Position = position;
        return position;
    }

    private static bool IsMeaningfulName(string name)
    {
        var meaningful = Regex.Replace(name, @"[\d\s()|.,*`°#@\-'""\\]", "");
        var hasCjk = Regex.IsMatch(meaningful, @"[\u3000-\u9fff\uf900-\ufaff]");
        return hasCjk || meaningful.Length >= 3;
    }

    // ─── Total Estimation ─────────────────────────────────────────

    private static RecognizedTotal? EstimateTotal(
        List<RecognizedPosition> positions, List<ReceiptTextLine> allLines)
    {
        var expected = positions.Aggregate(0.0, (sum, p) => sum + p.Price.Value);
        if (expected <= 0) return null;

        var totalLine = FindLineByAmount(allLines, expected);
        return new RecognizedTotal(expected, totalLine ?? positions[^1].Price.Line);
    }

    private static ReceiptTextLine? FindLineByAmount(List<ReceiptTextLine> lines, double amount)
    {
        foreach (var line in lines)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            var m = YenPrefix.Match(text);
            if (m.Success)
            {
                var str = Regex.Replace(m.Groups[1].Value, @"[\s,]", "");
                if (double.TryParse(str, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v == amount)
                    return line;
            }
        }
        foreach (var line in lines)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            var m = StandalonePrice.Match(text);
            if (m.Success)
            {
                var str = m.Groups[1].Value.Replace(",", "");
                if (double.TryParse(str, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v == amount)
                    return line;
            }
        }
        return null;
    }

    // ─── Line Helpers ─────────────────────────────────────────────

    private static ReceiptTextLine ProductLine(List<ReceiptTextLine> row)
    {
        foreach (var line in row)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            if (!IsPriceLine(text) && !Discount.IsMatch(text)) return line;
        }
        return row[0];
    }

    private static ReceiptTextLine PriceLine(List<ReceiptTextLine> row)
    {
        foreach (var line in row)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text).Trim();
            if (IsPriceLine(text)) return line;
        }
        return row[^1];
    }

    // ─── Store Detection ──────────────────────────────────────────

    /// <summary>Matches phone number patterns common in receipt headers.</summary>
    private static readonly Regex PhoneNumber = new(
        @"\d{2,4}-\d{3,4}-\d{3,4}",
        RegexOptions.Compiled);

    private static RecognizedStore? ExtractStore(List<List<ReceiptTextLine>> rows)
    {
        foreach (var row in rows)
        {
            var rowText = string.Join(" ", row.Select(l => l.Text));
            var normalized = ReceiptNormalizer.NormalizeFullWidth(rowText);

            if (ExtractPrice(row) != null) break;

            var storeName = Options.StoreNames.Detect(normalized);
            if (storeName != null)
                return new RecognizedStore(storeName, row[0]);
        }

        // Fallback: first qualifying text row before any price row.
        foreach (var row in rows)
        {
            var rowText = string.Join(" ", row.Select(l => l.Text));
            var normalized = ReceiptNormalizer.NormalizeFullWidth(rowText).Trim();
            if (ExtractPrice(row) != null) break;
            if (normalized.Length < 3) continue;
            if (PhoneNumber.IsMatch(normalized)) continue;
            if (Regex.IsMatch(normalized, @"^\d+$")) continue;
            return new RecognizedStore(normalized, row[0]);
        }
        return null;
    }

    // ─── Date Extraction ──────────────────────────────────────────

    /// <summary>Japanese era date: 令和7年1月15日.</summary>
    private static readonly Regex DateJapaneseEra = new(
        @"((令和|平成|昭和|大正|明治)\s*\d{1,2}\s*年\s*\d{1,2}\s*月\s*\d{1,2}\s*日)",
        RegexOptions.Compiled);

    /// <summary>Kanji date: 2025年1月15日.</summary>
    private static readonly Regex DateKanji = new(
        @"(\d{4}\s*年\s*\d{1,2}\s*月\s*\d{1,2}\s*日)",
        RegexOptions.Compiled);

    /// <summary>Numeric date: 2025/01/15 or 2025-01-15.</summary>
    private static readonly Regex DateNumeric = new(
        @"(\d{4})\s*[/\-]\s*(\d{1,2})\s*[/\-]\s*(\d{1,2})",
        RegexOptions.Compiled);

    /// <summary>
    /// Garbled date: "20264# 2A 8A(A)..." → 2026/2/8.
    /// OCR garbles 年/月/日 kanji into random characters, but digits survive.
    /// </summary>
    private static readonly Regex DateGarbled = new(
        @"(20\d{2})\d?\D{1,3}(\d{1,2})\D{1,3}(\d{1,2})\D",
        RegexOptions.Compiled);

    private static RecognizedPurchaseDate? ExtractDate(List<ReceiptTextLine> lines)
    {
        foreach (var line in lines)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text);

            var eraMatch = DateJapaneseEra.Match(text);
            if (eraMatch.Success)
            {
                var dt = ReceiptFormatter.ParseJapaneseEraDate(eraMatch.Groups[1].Value);
                if (dt != null) return new RecognizedPurchaseDate(dt.Value, line);
            }

            var kanjiMatch = DateKanji.Match(text);
            if (kanjiMatch.Success)
            {
                var dt = ReceiptFormatter.ParseKanjiDate(kanjiMatch.Groups[1].Value);
                if (dt != null) return new RecognizedPurchaseDate(dt.Value, line);
            }

            var numMatch = DateNumeric.Match(text);
            if (numMatch.Success)
            {
                if (int.TryParse(numMatch.Groups[1].Value, out var y) && y > 2000 &&
                    int.TryParse(numMatch.Groups[2].Value, out var m) && m >= 1 && m <= 12 &&
                    int.TryParse(numMatch.Groups[3].Value, out var d) && d >= 1 && d <= 31)
                {
                    return new RecognizedPurchaseDate(
                        new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), line);
                }
            }
        }

        // Fallback: garbled kanji date.
        foreach (var line in lines)
        {
            var text = ReceiptNormalizer.NormalizeFullWidth(line.Text);
            var garbMatch = DateGarbled.Match(text);
            if (garbMatch.Success)
            {
                if (int.TryParse(garbMatch.Groups[1].Value, out var y) && y >= 2020 && y <= 2030 &&
                    int.TryParse(garbMatch.Groups[2].Value, out var m) && m >= 1 && m <= 12 &&
                    int.TryParse(garbMatch.Groups[3].Value, out var d) && d >= 1 && d <= 31)
                {
                    return new RecognizedPurchaseDate(
                        new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), line);
                }
            }
        }
        return null;
    }

    // ─── Bounds Extraction ────────────────────────────────────────

    private static RecognizedBounds ExtractBounds(List<ReceiptTextLine> lines)
    {
        var rect = default(BoundingBox);
        if (lines.Count > 0)
        {
            float minX = lines[0].BoundingBox.Left;
            float maxX = lines[0].BoundingBox.Right;
            float minY = lines[0].BoundingBox.Top;
            float maxY = lines[0].BoundingBox.Bottom;
            for (var i = 1; i < lines.Count; i++)
            {
                var bb = lines[i].BoundingBox;
                if (bb.Left < minX) minX = bb.Left;
                if (bb.Right > maxX) maxX = bb.Right;
                if (bb.Top < minY) minY = bb.Top;
                if (bb.Bottom > maxY) maxY = bb.Bottom;
            }
            rect = BoundingBox.FromLTRB(minX, minY, maxX, maxY);
        }
        var line = new ReceiptTextLine(boundingBox: rect, angle: 0);
        return new RecognizedBounds(rect, line);
    }
}
