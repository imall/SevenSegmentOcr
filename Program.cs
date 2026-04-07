using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tesseract;

const string outputDir = @"D:\projects\ocr";
const string tessData  = "./Tessdata";
const bool   runOcr    = true;

Directory.CreateDirectory(outputDir);

var imageConfigs = new[]
{
    new ImageConfig(ImagePath: "./images/2.png", ConfigPath: "./configs/2.json"),
};

var roiLoader  = new RoiLoader(expandPixels: 0);
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented       = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

};

TesseractEngine? tessEngine = runOcr
    ? new TesseractEngine(tessData, "lets", EngineMode.Default)
    : null;
tessEngine?.SetVariable("tessedit_char_whitelist", "0123456789.Cc");

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
                error     = "無法載入圖片",
                results   = Array.Empty<object>(),
            }, jsonOptions));
            continue;
        }

        RoiDefinition[] configs = GetOrSelectRois(fullImage, imageConfig);

        var roiResults = new List<object>();

        foreach (var cfg in configs)
        {
            using var roi = roiLoader.Crop(fullImage, cfg);

            // ROI 超出範圍
            if (roi.Empty())
            {
                roiResults.Add(new
                {
                    id          = cfg.Id,
                    deviceType  = cfg.DeviceType.ToString(),
                    rawOcr      = (string?)null,
                    value       = (string?)null,
                    success     = false,
                    errorReason = "ROI 超出圖片範圍",
                });
                continue;
            }

            using var preprocessor = new ImagePreprocessor(cfg.Options);
            using var processed    = preprocessor.Process(roi);

            // 儲存前處理圖
            var procPath = Path.Combine(outputDir,
                Path.GetFileNameWithoutExtension(imageConfig.ImagePath),
                $"{cfg.Id}_proc.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(procPath)!);
            Cv2.ImWrite(procPath, processed);

            if (!runOcr || tessEngine is null)
            {
                roiResults.Add(new
                {
                    id          = cfg.Id,
                    deviceType  = cfg.DeviceType.ToString(),
                    rawOcr      = (string?)null,
                    value       = (string?)null,
                    success     = false,
                    errorReason = "OCR 未啟用",
                });
                continue;
            }

            // 執行 OCR
            var (rawOcr, ocrError) = RunTesseract(tessEngine, processed);

            if (ocrError is not null)
            {
                roiResults.Add(new
                {
                    id          = cfg.Id,
                    deviceType  = cfg.DeviceType.ToString(),
                    rawOcr      = (string?)null,
                    value       = (string?)null,
                    success     = false,
                    errorReason = ocrError,
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawOcr))
            {
                roiResults.Add(new
                {
                    id          = cfg.Id,
                    deviceType  = cfg.DeviceType.ToString(),
                    rawOcr      = rawOcr,
                    value       = (string?)null,
                    success     = false,
                    errorReason = "OCR 回傳空白",
                });
                continue;
            }

            // 成功
            roiResults.Add(new
            {
                id          = cfg.Id,
                deviceType  = cfg.DeviceType.ToString(),
                rawOcr      = rawOcr,
                value       = rawOcr,   // 目前先直接用，之後可加解析邏輯
                success     = true,
                errorReason = (string?)null,
            });
        }

        // 輸出整張圖的 JSON 結果
        var output = new
        {
            imagePath = imageConfig.ImagePath,
            results   = roiResults,
        };
        Console.WriteLine(JsonSerializer.Serialize(output, jsonOptions));
    }
}
finally
{
    tessEngine?.Dispose();
}

// ════════════════════════════════════════════════════════════════
// Tesseract 辨識，回傳 (rawText, errorMessage)
// ════════════════════════════════════════════════════════════════
static (string? raw, string? error) RunTesseract(TesseractEngine engine, Mat processedMat)
{
    try
    {
        // ★ 先把圖片縮放到固定高度，讓 DPI 計算穩定
        int targetHeight = 100;
        double scale = (double)targetHeight / processedMat.Rows;
        using var resized = new Mat();
        Cv2.Resize(processedMat, resized, new Size(0, 0), scale, scale, InterpolationFlags.Cubic);

        Cv2.ImEncode(".png", resized, out byte[] buf);
        using var pix  = Pix.LoadFromMemory(buf);
        using var page = engine.Process(pix, PageSegMode.SingleLine);
        return (page.GetText().Trim(), null);
    }
    catch (Exception ex)
    {
        return (null, $"Tesseract 例外：{ex.Message}");
    }
}

// ════════════════════════════════════════════════════════════════
// 以下不動
// ════════════════════════════════════════════════════════════════
static RoiDefinition[] GetOrSelectRois(Mat fullImage, ImageConfig imageConfig)
{
    if (imageConfig.ConfigPath is not null)
    {
        var loaded = RoiConfigStore.TryLoad(imageConfig.ConfigPath);
        if (loaded is { Length: > 0 })
            return loaded;
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
        var input      = Console.ReadLine()?.Trim().ToLower();
        var deviceType = input == "r" ? DeviceType.Rectangular : DeviceType.Circular;
        result.Add(roi with { DeviceType = deviceType });
    }
    return result.ToArray();
}

record ImageConfig(string ImagePath, string? ConfigPath = null);