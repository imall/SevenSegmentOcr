using System.Text.Json.Serialization;

namespace SevenSegmentOcr.Models;

/// <summary>
/// 裝置外型，決定後續辨識策略
/// </summary>
public enum DeviceType
{
    /// <summary>圓形 ø76mm：同一螢幕輪流顯示溫度或濕度，類型由 OCR 結果判斷</summary>
    Circular,

    /// <summary>方形 83×57×18mm：永遠只顯示溫度</summary>
    Rectangular
}



/// <summary>
/// 感興趣區域定義：包含裁切座標、裝置外型與顯示類型
/// </summary>
public record RoiDefinition(
    [property: JsonPropertyName("Id")]     int Id,
    [property: JsonPropertyName("X")]      int X,
    [property: JsonPropertyName("Y")]      int Y,
    [property: JsonPropertyName("Width")]  int Width,
    [property: JsonPropertyName("Height")] int Height,
    [property: JsonPropertyName("DeviceType")] DeviceType DeviceType
);
