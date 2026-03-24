namespace ReceiptRecognition.Core.Utils.Configuration;

/// <summary>
/// Built-in default options as typed dictionaries (can be persisted/overridden).
/// Canonicals map to themselves by default. Adjust values if you want alias→canonical.
/// </summary>
public static class ReceiptDefaults
{
    public static readonly Dictionary<string, object> KReceiptDefaultOptions = new()
    {
        ["storeNames"] = new Dictionary<string, string>
        {
            ["Aldi"] = "Aldi",
            ["Edeka"] = "Edeka",
            ["Kaufland"] = "Kaufland",
            ["Lidl"] = "Lidl",
            ["Netto"] = "Netto",
            ["Penny"] = "Penny",
            ["Rewe"] = "Rewe",
            ["Spar"] = "Spar",
            ["DM"] = "DM",
        },
        ["totalLabels"] = new Dictionary<string, string>
        {
            ["Zu zahlen"] = "Zu zahlen",
            ["Gesamt"] = "Gesamt",
            ["Summe"] = "Summe",
            ["Subtotal"] = "Subtotal",
            ["Total"] = "Total",
            ["Sum"] = "Sum",
        },
        ["ignoreKeywords"] = new List<string> { "E-Bon", "Coupon", "Eingabe", "Posten" },
        ["stopKeywords"] = new List<string> { "Geg.", "Rückgeld", "Bar", "Change" },
        ["allowedProductGroups"] = new List<string> { "A", "B", "AW", "BW", "1", "2" },
        ["tuning"] = new Dictionary<string, object>
        {
            ["optimizerTotalTolerance"] = 0.009,
            ["optimizerEwmaAlpha"] = 0.5,
            ["optimizerVerticalTolerance"] = 50,
            ["optimizerLoopThreshold"] = 10,
            ["optimizerMaxCacheSize"] = 15,
            ["optimizerConfidenceThreshold"] = 90,
            ["optimizerStabilityThreshold"] = 50,
            ["optimizerAboveCountDecayThreshold"] = 50,
            ["optimizerProductWeight"] = 1,
            ["optimizerPriceWeight"] = 1,
            ["optimizerUnrecognizedProductName"] = "Unrecognized items",
        },
    };

    public static readonly Dictionary<string, object> KReceiptDefaultOptionsJa = new()
    {
        ["storeNames"] = new Dictionary<string, string>
        {
            ["イオン"] = "イオン",
            ["AEON"] = "イオン",
            ["セブンイレブン"] = "セブンイレブン",
            ["セブン-イレブン"] = "セブンイレブン",
            ["7-ELEVEN"] = "セブンイレブン",
            ["ローソン"] = "ローソン",
            ["LAWSON"] = "ローソン",
            ["ファミリーマート"] = "ファミリーマート",
            ["FamilyMart"] = "ファミリーマート",
            ["ライフ"] = "ライフ",
            ["LIFE"] = "ライフ",
            ["マツモトキヨシ"] = "マツモトキヨシ",
            ["マツキヨ"] = "マツモトキヨシ",
            ["ウエルシア"] = "ウエルシア",
            ["ダイエー"] = "ダイエー",
            ["イトーヨーカドー"] = "イトーヨーカドー",
            ["まいばすけっと"] = "まいばすけっと",
            ["ミニストップ"] = "ミニストップ",
            ["MINISTOP"] = "ミニストップ",
            ["デイリーヤマザキ"] = "デイリーヤマザキ",
            ["サミット"] = "サミット",
            ["オーケー"] = "オーケー",
            ["OK"] = "オーケー",
            ["ドン・キホーテ"] = "ドン・キホーテ",
            ["ドンキ"] = "ドン・キホーテ",
            ["ツルハドラッグ"] = "ツルハドラッグ",
            ["スギ薬局"] = "スギ薬局",
            ["コスモス"] = "コスモス",
            ["西友"] = "西友",
            ["SEIYU"] = "西友",
            ["マックスバリュ"] = "マックスバリュ",
            ["カスミ"] = "カスミ",
            ["ヤオコー"] = "ヤオコー",
            ["ベルク"] = "ベルク",
            ["しまむら"] = "しまむら",
            ["ユニクロ"] = "ユニクロ",
            ["UNIQLO"] = "ユニクロ",
            ["ダイソー"] = "ダイソー",
            ["DAISO"] = "ダイソー",
            ["セリア"] = "セリア",
            ["Seria"] = "セリア",
            ["キャンドゥ"] = "キャンドゥ",
            ["Can★Do"] = "キャンドゥ",
            ["ニトリ"] = "ニトリ",
            ["NITORI"] = "ニトリ",
            ["コメリ"] = "コメリ",
            ["カインズ"] = "カインズ",
            ["CAINZ"] = "カインズ",
            ["ビックカメラ"] = "ビックカメラ",
            ["ヨドバシカメラ"] = "ヨドバシカメラ",
            ["ヤマダ電機"] = "ヤマダ電機",
        },
        ["totalLabels"] = new Dictionary<string, string>
        {
            ["合計"] = "合計",
            ["税込合計"] = "税込合計",
            ["小計"] = "小計",
            ["お買上合計"] = "お買上合計",
            ["お買い上げ合計"] = "お買い上げ合計",
            ["税込計"] = "税込計",
            ["お支払い合計"] = "お支払い合計",
            ["請求額"] = "請求額",
            ["Total"] = "Total",
            ["売上合計"] = "売上合計",
        },
        ["ignoreKeywords"] = new List<string>
        {
            "クーポン", "ポイント", "ありがとうございます", "またのご来店",
            "お待ちしております", "会員番号", "レジ担当", "電話番号",
            "TEL", "No.", "領収書", "店舗コード", "内税", "外税",
            "消費税", "非課税", "軽減税率", "※", "対象",
        },
        ["stopKeywords"] = new List<string>
        {
            "お預かり", "お釣り", "お釣", "現金", "クレジット",
            "PayPay", "nanaco", "WAON", "Suica", "PASMO",
            "iD", "QUICPay", "楽天ペイ", "d払い", "au PAY",
            "LINE Pay", "メルペイ", "電子マネー", "カード", "つり銭",
            "お預り", "釣銭",
        },
        ["allowedProductGroups"] = new List<string>(),
        ["tuning"] = new Dictionary<string, object>
        {
            ["optimizerTotalTolerance"] = 1.0,
            ["optimizerEwmaAlpha"] = 0.5,
            ["optimizerVerticalTolerance"] = 50,
            ["optimizerLoopThreshold"] = 10,
            ["optimizerMaxCacheSize"] = 15,
            ["optimizerConfidenceThreshold"] = 90,
            ["optimizerStabilityThreshold"] = 50,
            ["optimizerAboveCountDecayThreshold"] = 50,
            ["optimizerProductWeight"] = 1,
            ["optimizerPriceWeight"] = 1,
            ["optimizerUnrecognizedProductName"] = "未認識商品",
        },
    };
}
