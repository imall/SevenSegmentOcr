using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using SevenSegmentOcr.Recognition;

var projectRoot = GetProjectRoot();
var configsDir = Path.Combine(projectRoot, "configs");
var imagesDir = Path.Combine(projectRoot, "images");
const string outputDir = @"D:\projects\ocr";
const string tessData = "./Tessdata";
const bool runOcr = true;

Directory.CreateDirectory(outputDir);

var imageConfigs = new[]
{
    new ImageConfig(Path.Combine(imagesDir, "1.png"), Path.Combine(configsDir, "1.json")),
    new ImageConfig(Path.Combine(imagesDir, "2.png"), Path.Combine(configsDir, "2.json")),
    new ImageConfig(Path.Combine(imagesDir, "3.png"), Path.Combine(configsDir, "3.json")),
    new ImageConfig(Path.Combine(imagesDir, "5.png"), Path.Combine(configsDir, "5.json")),
    new ImageConfig(Path.Combine(imagesDir, "6.png"), Path.Combine(configsDir, "6.json")),
    new ImageConfig(Path.Combine(imagesDir, "7.png"), Path.Combine(configsDir, "7.json")),
    new ImageConfig(Path.Combine(imagesDir, "8.png"), Path.Combine(configsDir, "8.json")),
    new ImageConfig(Path.Combine(imagesDir, "9.png"), Path.Combine(configsDir, "9.json")),
    new ImageConfig(Path.Combine(imagesDir, "10.png"), Path.Combine(configsDir, "10.json")),
    new ImageConfig(Path.Combine(imagesDir, "11.png"), Path.Combine(configsDir, "11.json")),
    new ImageConfig(Path.Combine(imagesDir, "12.png"), Path.Combine(configsDir, "12.json")),
    new ImageConfig(Path.Combine(imagesDir, "13.png"), Path.Combine(configsDir, "13.json")),
    new ImageConfig(Path.Combine(imagesDir, "14.png"), Path.Combine(configsDir, "14.json")),
    new ImageConfig(Path.Combine(imagesDir, "15.png"), Path.Combine(configsDir, "15.json")),
    new ImageConfig(Path.Combine(imagesDir, "16.png"), Path.Combine(configsDir, "16.json")),
    // new ImageConfig(Path.Combine(imagesDir, "17.png"), Path.Combine(configsDir, "17.json")),
    // new ImageConfig(Path.Combine(imagesDir, "18.png"), Path.Combine(configsDir, "18.json")),
    // new ImageConfig(Path.Combine(imagesDir, "19.png"), Path.Combine(configsDir, "19.json")),
    // new ImageConfig(Path.Combine(imagesDir, "20.png"), Path.Combine(configsDir, "20.json")),
    new ImageConfig(Path.Combine(imagesDir, "21.png"), Path.Combine(configsDir, "21.json")),
    // new ImageConfig(Path.Combine(imagesDir, "22.png"), Path.Combine(configsDir, "22.json")),
    // new ImageConfig(Path.Combine(imagesDir, "23.png"), Path.Combine(configsDir, "23.json")),
    // new ImageConfig(Path.Combine(imagesDir, "24.png"), Path.Combine(configsDir, "24.json")),
    // new ImageConfig(Path.Combine(imagesDir, "25.png"), Path.Combine(configsDir, "25.json")),
    // new ImageConfig(Path.Combine(imagesDir, "26.png"), Path.Combine(configsDir, "26.json")),
    // new ImageConfig(Path.Combine(imagesDir, "27.png"), Path.Combine(configsDir, "27.json")),
    // new ImageConfig(Path.Combine(imagesDir, "28.png"), Path.Combine(configsDir, "28.json")),
    // new ImageConfig(Path.Combine(imagesDir, "29.png"), Path.Combine(configsDir, "29.json")),
    new ImageConfig(Path.Combine(imagesDir, "30.png"), Path.Combine(configsDir, "30.json")),
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var roiLoader = new RoiLoader(expandPixels: 0);
var imageProcessor = new ImageProcessor(roiLoader);

OcrRunner? ocrRunner = runOcr ? new OcrRunner(tessData) : null;

try
{
    foreach (var imageConfig in imageConfigs)
    {
        using var fullImage = Cv2.ImRead(imageConfig.ImagePath, ImreadModes.Color);
        if (fullImage.Empty())
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                imagePath = imageConfig.ImagePath,
                error = "無法載入圖片",
                results = Array.Empty<object>(),
            }, jsonOptions));
            continue;
        }

        var roiConfigs = GetOrSelectRois(fullImage, imageConfig);
        var processedList = imageProcessor.Process(fullImage, roiConfigs);
        var imageName = Path.GetFileNameWithoutExtension(imageConfig.ImagePath);
        var roiResults = new List<object>();

        foreach (var item in processedList)
        {
            // 前處理失敗
            if (item.Processed is null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, value: null, unit: null, success: false, item.Error));
                continue;
            }

            // 儲存前處理圖
            ImageProcessor.SaveProcessed(item.Processed, outputDir, imageName, item.Config.Id);

            // OCR 未啟用
            if (ocrRunner is null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, value: null, unit: null, success: false, "OCR 未啟用"));
                item.Processed.Dispose();
                continue;
            }

            // 執行 OCR
            var (raw, error) = ocrRunner.Recognize(item.Processed);
            item.Processed.Dispose();

            if (error is not null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, value: null, unit: null, success: false, error));
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: raw, value: null, unit: null, success: false, "OCR 回傳空白"));
                continue;
            }

            // ★ 後處理
            var (value, unit, postError) = PostProcess(raw, item.Config.DeviceType);

            if (postError is not null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: raw, value: null, unit: null, success: false, postError));
                continue;
            }

            roiResults.Add(MakeResult(item.Config, rawOcr: raw, value: value, unit: unit, success: true, errorReason: null));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            imagePath = imageConfig.ImagePath,
            results = roiResults,
        }, jsonOptions));
    }
}
finally
{
    ocrRunner?.Dispose();
}


// ════════════════════════════════════════════════════════════════
// 輔助方法
// ════════════════════════════════════════════════════════════════
static object MakeResult(RoiDefinition cfg, string? rawOcr, string? value, string? unit, bool success, string? errorReason) => new
{
    id = cfg.Id,
    deviceType = cfg.DeviceType.ToString(),
    rawOcr,
    value,
    unit,
    success,
    errorReason,
};

static RoiDefinition[] GetOrSelectRois(Mat fullImage, ImageConfig imageConfig)
{
    if (imageConfig.ConfigPath is not null)
    {
        var loaded = RoiConfigStore.TryLoad(imageConfig.ConfigPath);
        if (loaded is { Length: > 0 }) return loaded;
    }

    Console.Error.WriteLine("  進入互動圈選模式（SPACE/ENTER 確認，ESC 完成）");
    var selected = RoiSelector.Select(fullImage);
    if (selected.Length == 0) return selected;

    selected = PromptDeviceTypes(selected);

    if (imageConfig.ConfigPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(imageConfig.ConfigPath) ?? ".");
        RoiConfigStore.Save(selected, imageConfig.ConfigPath);
    }

    return selected;
}

static RoiDefinition[] PromptDeviceTypes(RoiDefinition[] rois)
{
    var result = new List<RoiDefinition>();
    foreach (var roi in rois)
    {
        Console.Error.Write($"  ROI id={roi.Id} [c=圓形 / r=長形，預設=圓形]：");
        var input = Console.ReadLine()?.Trim().ToLower();
        var deviceType = input == "r" ? DeviceType.Rectangular : DeviceType.Circular;
        result.Add(roi with { DeviceType = deviceType });
    }

    return result.ToArray();
}

static string GetProjectRoot()
{
    var baseDir = AppContext.BaseDirectory;
    return Path.GetFullPath(Path.Combine(baseDir, "..", "..", ".."));
}

// ════════════════════════════════════════════════════════════════
// 後處理：清理 OCR 結果，判斷溫度或濕度
// ════════════════════════════════════════════════════════════════
static (string? Value, string? Unit, string? Error) PostProcess(string raw, DeviceType deviceType)
{
    if (string.IsNullOrWhiteSpace(raw))
        return (null, null, "OCR 回傳空白");

    // Step 1：移除所有空白
    var cleaned = raw.Replace(" ", "");

    // Step 2：只保留數字和小數點
    cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"[^0-9.]", "");

    if (string.IsNullOrWhiteSpace(cleaned))
        return (null, null, "後處理後無有效數值");

    // Step 3：若有多個小數點，以第一個為主，移除其餘
    int firstDot = cleaned.IndexOf('.');
    if (firstDot >= 0)
    {
        var beforeDot = cleaned[..(firstDot + 1)]; // 含第一個小數點
        var afterDot = cleaned[(firstDot + 1)..].Replace(".", ""); // 小數點後移除多餘的點
        cleaned = beforeDot + afterDot;
    }

    if (!decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
        return (null, null, $"無法解析為數值：{cleaned}");

    // Step 4：長型裝置直接回傳溫度
    if (deviceType == DeviceType.Rectangular)
    {
        // 如果小數點後超過一位，無條件捨去
        if (firstDot < 0) return (cleaned, "°C", null);
        
        var truncated = cleaned[..Math.Min(firstDot + 2, cleaned.Length)];
        return (truncated, "°C", null);
    }

    // Step 5：圓形裝置判斷溫度或濕度
    // 小數點後有第二位數字（例如 25.36 → 小數後兩位）→ 溫度
    // 否則（例如 49.8）→ 濕度
    var isTemperature = false;
    if (firstDot >= 0)
    {
        var afterDot = cleaned[(firstDot + 1)..];
        isTemperature = afterDot.Length >= 2;
    }
    else
    {
        // 沒有小數點，用數值範圍判斷
        // 濕度範圍 0~100，溫度可能是負數或超過 100
        isTemperature = numericValue < 0 || numericValue > 100;
    }

    return isTemperature
        ? (firstDot >= 0
                ? cleaned[..Math.Min(firstDot + 2, cleaned.Length)] // 有小數點：截到小數後一位
                : cleaned, // 無小數點：整串保留
            "°C", null)
        : (cleaned, "%", null);
}

record ImageConfig(string ImagePath, string? ConfigPath = null);
