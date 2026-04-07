using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using Tesseract;

const string outputDir  = @"D:\projects\ocr";
const string tessData   = "./Tessdata"; 

// ★ 控制旗標：true = 跑辨識，false = 只切圖
const bool runOcr = false;

Directory.CreateDirectory(outputDir);

var imageConfigs = new[]
{
    new ImageConfig(ImagePath: "./images/1.png",  ConfigPath: "./configs/1.json"),
    new ImageConfig(ImagePath: "./images/2.png",  ConfigPath: "./configs/2.json"),
    new ImageConfig(ImagePath: "./images/3.png",  ConfigPath: "./configs/3.json"),
    new ImageConfig(ImagePath: "./images/16.png", ConfigPath: "./configs/16.json"),
};

var roiLoader = new RoiLoader(expandPixels: 0);

// ★ 只有 runOcr = true 才建立 Tesseract engine
TesseractEngine? tessEngine = runOcr
    ? new TesseractEngine(tessData, "lets", EngineMode.Default)
    : null;

tessEngine?.SetVariable("tessedit_char_whitelist", "-0123456789.C%");

try
{
    foreach (var imageConfig in imageConfigs)
    {
        Console.WriteLine($"\n══ 處理圖片：{imageConfig.ImagePath}");

        using var fullImage = Cv2.ImRead(imageConfig.ImagePath, ImreadModes.Color);
        if (fullImage.Empty())
        {
            Console.WriteLine($"  [錯誤] 無法載入：{imageConfig.ImagePath}");
            continue;
        }
        Console.WriteLine($"  尺寸：{fullImage.Cols}x{fullImage.Rows}");

        RoiDefinition[] configs = GetOrSelectRois(fullImage, imageConfig);
        if (configs.Length == 0)
        {
            Console.WriteLine("  [跳過] 未框選任何 ROI");
            continue;
        }

        var imageName   = Path.GetFileNameWithoutExtension(imageConfig.ImagePath);
        var imageOutDir = Path.Combine(outputDir, imageName);
        Directory.CreateDirectory(imageOutDir);

        foreach (var cfg in configs)
        {
            using var roi = roiLoader.Crop(fullImage, cfg);
            if (roi.Empty())
            {
                Console.WriteLine($"  [跳過] id={cfg.Id} ROI 超出圖片範圍");
                continue;
            }

            using var preprocessor = new ImagePreprocessor(cfg.Options);
            using var processed    = preprocessor.Process(roi);

            // ── 切圖輸出（永遠執行）──────────────────────────────
            var procPath = Path.Combine(imageOutDir, $"{cfg.Id}_proc.jpg");
            Cv2.ImWrite(procPath, processed);
            Console.Write($"  id={cfg.Id:D2} [{cfg.DeviceType,-11}] → {procPath}");

            // ★ 辨識（只有 runOcr = true 才執行）────────────────
            if (runOcr && tessEngine is not null)
            {
                string rawText = RunTesseract(tessEngine, processed);
                Console.Write($"  OCR='{rawText}'");
            }

            Console.WriteLine();
        }
    }
}
finally
{
    tessEngine?.Dispose();
}

Console.WriteLine($"\n完成，請檢查：{Path.GetFullPath(outputDir)}");

// ════════════════════════════════════════════════════════════════
// Tesseract 辨識
// ════════════════════════════════════════════════════════════════
static string RunTesseract(TesseractEngine engine, Mat processedMat)
{
    try
    {
        // OpenCvSharp Mat → byte[] → Tesseract Pix
        Cv2.ImEncode(".png", processedMat, out byte[] buf);
        using var pix  = Pix.LoadFromMemory(buf);
        using var page = engine.Process(pix);
        return page.GetText().Trim();
    }
    catch (Exception ex)
    {
        return $"[錯誤:{ex.Message}]";
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
        {
            Console.WriteLine($"  載入 ROI 設定：{imageConfig.ConfigPath} ({loaded.Length} 個)");
            return loaded;
        }
    }

    Console.WriteLine("  進入互動圈選模式");
    Console.WriteLine("  操作：拖曳框選 → SPACE/ENTER 確認 → ESC 完成所有選取");
    var selected = RoiSelector.Select(fullImage);
    if (selected.Length == 0) return selected;

    selected = PromptDeviceTypes(selected);

    if (imageConfig.ConfigPath is not null)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(imageConfig.ConfigPath) ?? ".");
        RoiConfigStore.Save(selected, imageConfig.ConfigPath);
        Console.WriteLine($"  已儲存 ROI → {imageConfig.ConfigPath}");
        Console.WriteLine($"  [提示] 可直接編輯 JSON 調整 DeviceType 或 MorphKernelSize");
    }

    return selected;
}

static RoiDefinition[] PromptDeviceTypes(RoiDefinition[] rois)
{
    Console.WriteLine("\n  請為每個 ROI 設定裝置類型：");
    var result = new List<RoiDefinition>();

    foreach (var roi in rois)
    {
        Console.Write($"    ROI id={roi.Id} → 輸入裝置類型 [c=圓形 / r=長形，預設=圓形]：");
        var input = Console.ReadLine()?.Trim().ToLower();
        var deviceType = input == "r" ? DeviceType.Rectangular : DeviceType.Circular;
        result.Add(roi with { DeviceType = deviceType });
    }

    return result.ToArray();
}

record ImageConfig(string ImagePath, string? ConfigPath = null);