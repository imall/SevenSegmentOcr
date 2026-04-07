using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using SevenSegmentOcr.Recognition;

var projectRoot = GetProjectRoot();
var configsDir  = Path.Combine(projectRoot, "configs");
var imagesDir   = Path.Combine(projectRoot, "images");
const string outputDir = @"D:\projects\ocr";
const string tessData  = "./Tessdata";
const bool   runOcr    = true;

Directory.CreateDirectory(outputDir);

var imageConfigs = new[]
{
    new ImageConfig(Path.Combine(imagesDir, "1.png"), Path.Combine(configsDir, "1.json")),
    new ImageConfig(Path.Combine(imagesDir, "2.png"), Path.Combine(configsDir, "2.json")),
};

var jsonOptions = new JsonSerializerOptions
{
    WriteIndented          = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
};

var imageProcessor = new ImageProcessor();
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
                error     = "無法載入圖片",
                results   = Array.Empty<object>(),
            }, jsonOptions));
            continue;
        }

        var roiConfigs  = GetOrSelectRois(fullImage, imageConfig);
        var processedList = imageProcessor.Process(fullImage, roiConfigs);
        var imageName   = Path.GetFileNameWithoutExtension(imageConfig.ImagePath);
        var roiResults  = new List<object>();

        foreach (var item in processedList)
        {
            // 前處理失敗
            if (item.Processed is null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, success: false, item.Error));
                continue;
            }

            // 儲存前處理圖
            ImageProcessor.SaveProcessed(item.Processed, outputDir, imageName, item.Config.Id);

            // OCR 未啟用
            if (ocrRunner is null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, success: false, "OCR 未啟用"));
                item.Processed.Dispose();
                continue;
            }

            // 執行 OCR
            var (raw, error) = ocrRunner.Recognize(item.Processed);
            item.Processed.Dispose();

            if (error is not null)
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: null, success: false, error));
                continue;
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                roiResults.Add(MakeResult(item.Config, rawOcr: raw, success: false, "OCR 回傳空白"));
                continue;
            }

            roiResults.Add(MakeResult(item.Config, rawOcr: raw, success: true, errorReason: null));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            imagePath = imageConfig.ImagePath,
            results   = roiResults,
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
static object MakeResult(RoiDefinition cfg, string? rawOcr, bool success, string? errorReason) => new
{
    id          = cfg.Id,
    deviceType  = cfg.DeviceType.ToString(),
    rawOcr,
    value       = success ? rawOcr : null,
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
        var input      = Console.ReadLine()?.Trim().ToLower();
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

record ImageConfig(string ImagePath, string? ConfigPath = null);