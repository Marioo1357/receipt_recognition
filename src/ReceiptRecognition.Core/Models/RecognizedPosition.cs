using ReceiptRecognition.Core.Utils.Configuration;
using ReceiptRecognition.Core.Utils.Normalize;

namespace ReceiptRecognition.Core.Models;

/// <summary>
/// A single line-item position on a receipt, pairing a product with its price.
/// </summary>
public sealed class RecognizedPosition
{
    public RecognizedProduct Product { get; }
    public RecognizedPrice Price { get; }
    public DateTime Timestamp { get; set; }
    public Operation Operation { get; set; }
    public RecognizedUnit? Unit { get; set; }
    public RecognizedGroup? Group { get; set; }

    public RecognizedPosition(
        RecognizedProduct product, RecognizedPrice price,
        DateTime timestamp, Operation operation,
        RecognizedUnit? unit = null, RecognizedGroup? group = null)
    {
        Product = product;
        Price = price;
        Timestamp = timestamp;
        Operation = operation;
        Unit = unit;
        Group = group;
    }

    public static RecognizedPosition FromJson(Dictionary<string, object?> json)
    {
        var productRaw = json.GetValueOrDefault("product") as Dictionary<string, object?>
                         ?? new Dictionary<string, object?>();
        var priceRaw = json.GetValueOrDefault("price") as Dictionary<string, object?>
                       ?? new Dictionary<string, object?>();

        return new RecognizedPosition(
            RecognizedProduct.FromJson(productRaw),
            RecognizedPrice.FromJson(priceRaw),
            DateTime.TryParse(json.GetValueOrDefault("timestamp")?.ToString() ?? "", out var ts)
                ? ts
                : DateTime.Now,
            Operation.None);
    }

    public static RecognizedPosition Empty()
    {
        return new RecognizedPosition(
            new RecognizedProduct("", new ReceiptTextLine()),
            new RecognizedPrice(0.0, new ReceiptTextLine()),
            DateTime.Now, Operation.None);
    }

    public static RecognizedPosition Pseudo(RecognizedReceipt receipt, string pseudoName)
    {
        if (receipt.Total == null || receipt.Positions.Count == 0)
            return Empty();

        var totalCents = (int)Math.Round(receipt.Total.Value * 100);
        var calculatedTotalCents = (int)Math.Round(receipt.CalculatedTotal.Value * 100);
        var lastPos = receipt.Positions[^1];
        var aRect = lastPos.Product.Line.BoundingBox;
        var bRect = lastPos.Price.Line.BoundingBox;

        var product = new RecognizedProduct(
            pseudoName,
            new ReceiptTextLine(boundingBox: BoundingBox.FromLTRB(
                aRect.Left, aRect.Top + aRect.Height, aRect.Right, aRect.Bottom)));

        var price = new RecognizedPrice(
            (totalCents - calculatedTotalCents) / 100.0,
            new ReceiptTextLine(boundingBox: BoundingBox.FromLTRB(
                bRect.Left, bRect.Top + bRect.Height, bRect.Right, bRect.Bottom)));

        return new RecognizedPosition(
            product, price,
            receipt.Timestamp, Operation.None);
    }

    public RecognizedPosition CopyWith(
        RecognizedProduct? product = null, RecognizedPrice? price = null,
        DateTime? timestamp = null, Operation? operation = null,
        RecognizedUnit? unit = null, RecognizedGroup? group = null)
    {
        return new RecognizedPosition(
            product ?? Product, price ?? Price,
            timestamp ?? Timestamp, operation ?? Operation,
            unit: unit ?? Unit, group: group ?? Group);
    }

    public BoundingBox BoundingBox => BoundingBox.FromLTRB(
        Product.Line.BoundingBox.Left,
        Math.Min(Product.Line.BoundingBox.Top, Price.Line.BoundingBox.Top),
        Price.Line.BoundingBox.Right,
        Math.Max(Product.Line.BoundingBox.Bottom, Price.Line.BoundingBox.Bottom));

    public int ConfidenceScore
    {
        get
        {
            var pc = Product.Confidence;
            var prc = Price.Confidence;
            if (pc == null || prc == null) return 0;

            var w1 = pc.Weight;
            var w2 = prc.Weight;
            var denom = w1 + w2;
            if (denom == 0) return 0;

            var num = pc.Value * w1 + prc.Value * w2;
            return num / denom;
        }
    }

    public int Stability => Product.TextConsensusRatio;
}

/// <summary>
/// Groups multiple recognized positions (from successive scans) for consensus-based optimization.
/// </summary>
public sealed class RecognizedGroup
{
    private readonly List<RecognizedPosition> _members;
    private readonly int _maxGroupSize;

    public RecognizedGroup(int maxGroupSize = 1)
    {
        _members = [];
        _maxGroupSize = maxGroupSize > 1 ? maxGroupSize : 1;
    }

    public void AddMember(RecognizedPosition position)
    {
        position.Group = this;
        if (_members.Count >= _maxGroupSize) _members.RemoveAt(0);
        _members.Add(position);
        RecalculateAllConfidences();
    }

    // TODO: Wire up ReceiptNormalizer
    public Confidence CalculateProductConfidence(RecognizedProduct product)
    {
        if (_members.Count == 0) return new Confidence(0);

        var scores = _members
            .Select(b => ReceiptNormalizer.Similarity(product.Value, b.Product.Value))
            .ToList();
        if (scores.Count == 0) return new Confidence(0);

        double total = 0.0, totalSq = 0.0;
        foreach (var s in scores)
        {
            total += s;
            totalSq += s * s;
        }

        var n = scores.Count;
        var avg = total / n;
        var variance = totalSq / n - avg * avg;
        var stddev = Math.Sqrt(Math.Max(0.0, variance));
        var weight = stddev < 10 ? 1.0 : (100 - stddev) / 100.0;

        // TODO: Wire up ReceiptRuntime
        return new Confidence(
            (int)Math.Clamp(avg * weight, 0, 100),
            ReceiptRuntime.Tuning.OptimizerProductWeight);
    }

    // TODO: Wire up ReceiptRuntime
    public Confidence CalculatePriceConfidence(RecognizedPrice price)
    {
        if (_members.Count == 0 || price.Value == 0)
            return new Confidence(0);

        var scores = _members.Select(b =>
        {
            var equalPrice = price.FormattedValue == b.Price.FormattedValue;
            return equalPrice ? 100 : 0;
        }).ToList();

        var avg = (double)scores.Sum() / scores.Count;
        return new Confidence(
            (int)avg,
            ReceiptRuntime.Tuning.OptimizerPriceWeight);
    }

    public List<RecognizedPosition> Members => _members;

    public List<string> AlternativeTexts =>
        _members.Select(p => p.Product.Text).ToList();

    // TODO: Wire up ReceiptFormatter
    public List<string> AlternativePostfixTexts =>
        _members.Select(p => ReceiptFormatter.ToPostfixText(p.Price.Line.Text)).ToList();

    public List<RecognizedUnit> AlternativeUnits =>
        _members
            .Where(p => p.Unit != null
                        && p.Unit.Quantity.Value != 1
                        && p.Unit.Price.FormattedValue != p.Price.FormattedValue)
            .Select(p => p.Unit!)
            .ToList();

    public int ConfidenceScore
    {
        get
        {
            if (_members.Count == 0) return 0;
            var total = _members.Sum(b => b.ConfidenceScore);
            return total / _members.Count;
        }
    }

    public int StabilityScore
    {
        get
        {
            if (_members.Count == 0) return 0;
            var total = _members.Sum(b => b.Stability);
            return total / _members.Count;
        }
    }

    public DateTime Timestamp
    {
        get
        {
            if (_members.Count == 0) return DateTime.Now;
            return _members.MaxBy(m => m.Timestamp)!.Timestamp;
        }
    }

    private void RecalculateAllConfidences()
    {
        foreach (var m in _members)
        {
            m.Product.Confidence = CalculateProductConfidence(m.Product);
            m.Price.Confidence = CalculatePriceConfidence(m.Price);
        }
    }
}
