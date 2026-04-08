using System.Globalization;
using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SevenSegmentOcr.Recognition;
using SevenSegmentOcr.Parsing;

var projectRoot = GetProjectRoot();
var configsDir = Path.Combine(projectRoot, "configs");
var imagesDir = Path.Combine(projectRoot, "images");
const string outputDir = @"D:\projects\ocr";
const string tessData = "./Tessdata";
const bool runOcr = true;

Directory.CreateDirectory(outputDir);

var imageConfigs = new[]
{
    // new ImageConfig(Path.Combine(imagesDir, "1.png"), Path.Combine(configsDir, "1.json")),
    // new ImageConfig(Path.Combine(imagesDir, "2.png"), Path.Combine(configsDir, "2.json")),
    // new ImageConfig(Path.Combine(imagesDir, "3.png"), Path.Combine(configsDir, "3.json")),
    // new ImageConfig(Path.Combine(imagesDir, "5.png"), Path.Combine(configsDir, "5.json")),
    // new ImageConfig(Path.Combine(imagesDir, "6.png"), Path.Combine(configsDir, "6.json")),
    // new ImageConfig(Path.Combine(imagesDir, "7.png"), Path.Combine(configsDir, "7.json")),
    // new ImageConfig(Path.Combine(imagesDir, "8.png"), Path.Combine(configsDir, "8.json")),
    // new ImageConfig(Path.Combine(imagesDir, "9.png"), Path.Combine(configsDir, "9.json")),
    // new ImageConfig(Path.Combine(imagesDir, "10.png"), Path.Combine(configsDir, "10.json")),
    // new ImageConfig(Path.Combine(imagesDir, "11.png"), Path.Combine(configsDir, "11.json")),
    // new ImageConfig(Path.Combine(imagesDir, "12.png"), Path.Combine(configsDir, "12.json")),
    // new ImageConfig(Path.Combine(imagesDir, "13.png"), Path.Combine(configsDir, "13.json")),
    // new ImageConfig(Path.Combine(imagesDir, "14.png"), Path.Combine(configsDir, "14.json")),
    // new ImageConfig(Path.Combine(imagesDir, "15.png"), Path.Combine(configsDir, "15.json")),
    // new ImageConfig(Path.Combine(imagesDir, "16.png"), Path.Combine(configsDir, "16.json")),
    // new ImageConfig(Path.Combine(imagesDir, "17.png"), Path.Combine(configsDir, "17.json")),
    // new ImageConfig(Path.Combine(imagesDir, "18.png"), Path.Combine(configsDir, "18.json")),
    // new ImageConfig(Path.Combine(imagesDir, "19.png"), Path.Combine(configsDir, "19.json")),
    // new ImageConfig(Path.Combine(imagesDir, "20.png"), Path.Combine(configsDir, "20.json")),
    // new ImageConfig(Path.Combine(imagesDir, "21.png"), Path.Combine(configsDir, "21.json")),
    // new ImageConfig(Path.Combine(imagesDir, "22.png"), Path.Combine(configsDir, "22.json")),
    // new ImageConfig(Path.Combine(imagesDir, "23.png"), Path.Combine(configsDir, "23.json")),
    new ImageConfig(Path.Combine(imagesDir, "24.png"), Path.Combine(configsDir, "24.json")),
    new ImageConfig(Path.Combine(imagesDir, "25.png"), Path.Combine(configsDir, "25.json")),
    // new ImageConfig(Path.Combine(imagesDir, "26.png"), Path.Combine(configsDir, "26.json")),
    // new ImageConfig(Path.Combine(imagesDir, "27.png"), Path.Combine(configsDir, "27.json")),
    // new ImageConfig(Path.Combine(imagesDir, "28.png"), Path.Combine(configsDir, "28.json")),
    // new ImageConfig(Path.Combine(imagesDir, "29.png"), Path.Combine(configsDir, "29.json")),
    // new ImageConfig(Path.Combine(imagesDir, "30.png"), Path.Combine(configsDir, "30.json")),
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
            var (value, unit, postError) = ValueParser.PostProcess(raw, item.Config.DeviceType);

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

record ImageConfig(string ImagePath, string? ConfigPath = null);