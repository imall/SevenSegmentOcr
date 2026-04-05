using System.Text.RegularExpressions;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Parsing;

/// <summary>
/// 將 OCR 吐出的原始文字清理並轉換成有意義的數值
/// </summary>
public static class ValueParser
{
    private static readonly Regex DigitsOnly = new(@"[^0-9.]", RegexOptions.Compiled);

    public static OcrResult Parse(string rawText, DisplayValueType valueType)
    {
        // 1. 只保留數字和小數點
        string clean = DigitsOnly.Replace(rawText, "");

        if (string.IsNullOrEmpty(clean))
            return OcrResult.Failure($"無法從 '{rawText}' 提取數字");

        // 2. 修正常見問題：沒有小數點時嘗試自動補位
        //    例如：253 → 25.3（溫度通常是 XX.X 格式）
        clean = FixMissingDecimalPoint(clean, valueType);

        // 3. 嘗試解析
        if (!double.TryParse(clean, out double val))
            return OcrResult.Failure($"無法解析數值：'{clean}'");

        // 4. 修正常見辨識錯誤：前導數字遺失
        //    例如：4.9 → 24.9（在室溫場景下）
        val = FixMissingLeadingDigit(val, valueType);

        // 5. 範圍驗證
        return valueType == DisplayValueType.Temperature
            ? ValidateTemperature(val, clean)
            : ValidateHumidity(val, clean);
    }

    private static string FixMissingDecimalPoint(string text, DisplayValueType type)
    {
        // 只對溫度/濕度的典型長度處理（3位數字沒有小數點）
        if (text.Contains('.') || text.Length < 3) return text;

        // 溫度：253 → 25.3
        // 濕度：524 → 52.4
        return text[..^1] + "." + text[^1..];
    }

    private static double FixMissingLeadingDigit(double val, DisplayValueType type)
    {
        if (type == DisplayValueType.Temperature)
        {
            // 4.x ~ 5.x 大概率是 24.x ~ 25.x（根據你的場景調整這個範圍）
            if (val is >= 4.0 and <= 5.9)
                return val + 20.0;
        }
        return val;
    }

    private static OcrResult ValidateTemperature(double val, string raw) =>
        val is >= -40.0 and <= 200.0
            ? OcrResult.From(val, DisplayValueType.Temperature, raw, 1.0)
            : OcrResult.Failure($"溫度超出合理範圍：{val}°C");

    private static OcrResult ValidateHumidity(double val, string raw) =>
        val is >= 0.0 and <= 100.0
            ? OcrResult.From(val, DisplayValueType.Humidity, raw, 1.0)
            : OcrResult.Failure($"濕度超出合理範圍：{val}%");
}
