using System.Text.RegularExpressions;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Parsing;

/// <summary>
/// 將 OCR 吐出的原始文字清理並轉換成有意義的數值
/// </summary>
public static class ValueParser
{
    /// <summary>
    /// 清理 OCR 結果，判斷溫度或濕度
    /// </summary>
    public static (string? Value, string? Unit, string? Error) PostProcess(string raw, DeviceType deviceType)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null, "OCR 回傳空白");

        // Step 1：移除所有空白
        var cleaned = raw.Replace(" ", "");

        // Step 2：只保留數字和小數點
        cleaned = Regex.Replace(cleaned, @"[^0-9.]", "");

        if (string.IsNullOrWhiteSpace(cleaned))
            return (null, null, "後處理後無有效數值");

        // Step 3：小數點出現在第一個字元，去除它
        cleaned = cleaned.TrimStart('.');

        if (string.IsNullOrWhiteSpace(cleaned))
            return (null, null, "移除前導小數點後無有效數值");

        // Step 4：處理連續小數點
        cleaned = Regex.Replace(cleaned, @"\.{2,}", ".");

        // Step 5：若有多個分散的小數點，保留第一個，移除其餘
        int firstDot = cleaned.IndexOf('.');
        if (firstDot >= 0)
        {
            var beforeDot = cleaned[..(firstDot + 1)];
            var afterDot  = cleaned[(firstDot + 1)..].Replace(".", "");
            cleaned = beforeDot + afterDot;
        }

        // Step 6：嘗試解析數值
        if (!decimal.TryParse(cleaned,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var numericValue))
            return (null, null, $"無法解析為數值：{cleaned}");

        // Step 7：長型裝置 → 直接回傳溫度
        if (deviceType == DeviceType.Rectangular)
        {
            var truncated = TruncateToOneDecimal(cleaned);
            return (truncated, "°C", null);
        }

        // Step 8：圓形裝置 → 判斷溫度或濕度
        return ResolveCircularDeviceReading(cleaned, numericValue);
    }

    /// <summary>
    /// 圓形裝置讀值判斷邏輯
    /// </summary>
    private static (string? Value, string? Unit, string? Error) ResolveCircularDeviceReading(
        string cleaned, decimal numericValue)
    {
        int firstDot = cleaned.IndexOf('.');

        // 規則 A：無小數點且為四位數 → 補小數點 (2567 → 25.67 → 25.6)
        if (firstDot < 0 && cleaned.Length == 4)
        {
            cleaned  = cleaned[..2] + "." + cleaned[2..];
            cleaned = TruncateToOneDecimal(cleaned);
            return (cleaned, "°C", null);
        }

        // 規則 B：有小數點 → 依小數位數判斷
        if (firstDot >= 0)
        {
            var afterDot = cleaned[(firstDot + 1)..];
            if (afterDot.Length >= 2)
            {
                return (TruncateToOneDecimal(cleaned), "°C", null);
            }
            else
            {
                return (cleaned, "%", null);
            }
        }

        // 規則 C：無小數點且非四位數 → 用數值範圍判斷
        bool isTemperature = numericValue < 0 || numericValue > 100;
        return (cleaned, isTemperature ? "°C" : "%", null);
    }

    /// <summary>
    /// 工具：無條件捨去小數點第二位，保留一位小數
    /// </summary>
    private static string TruncateToOneDecimal(string value)
    {
        int dot = value.IndexOf('.');
        if (dot < 0) return value;
        var truncated = value[..System.Math.Min(dot + 2, value.Length)];
        return truncated;
    }
}
