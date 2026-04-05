using System.Text.Json;
using System.Text.Json.Serialization;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Imaging;

/// <summary>
/// 將 ROI 設定序列化到 JSON 檔案，或從 JSON 檔案反序列化回來。
/// </summary>
public static class RoiConfigStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented      = true,
        Converters         = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>將 ROI 陣列寫入 JSON 檔。</summary>
    public static void Save(IEnumerable<RoiDefinition> rois, string path)
    {
        var json = JsonSerializer.Serialize(rois, Options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 嘗試從 JSON 檔讀取 ROI 陣列。
    /// 檔案不存在或格式錯誤時回傳 null。
    /// </summary>
    public static RoiDefinition[]? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RoiDefinition[]>(json, Options);
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"[警告] 讀取 ROI 設定失敗（{path}）：{ex.Message}");
            return null;
        }
    }
}

