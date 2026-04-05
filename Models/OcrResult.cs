namespace SevenSegmentOcr.Models;

public class OcrResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public double? Value { get; init; }
    public string RawText { get; init; } = string.Empty;
    public double Confidence { get; init; }
    public DisplayValueType ValueType { get; init; }

    public static OcrResult Failure(string message) =>
        new() { Success = false, Message = message };

    public static OcrResult From(double value, DisplayValueType type, string raw, double conf)
    {
        string label = type == DisplayValueType.Temperature
            ? $"溫度: {value}°C"
            : $"濕度: {value}%";

        return new OcrResult
        {
            Success = true,
            Message = label,
            Value = value,
            RawText = raw,
            Confidence = Math.Round(conf, 4),
            ValueType = type
        };
    }
}

public enum DisplayValueType { Temperature, Humidity }
