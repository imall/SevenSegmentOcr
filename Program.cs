using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using SevenSegmentOcr.Parsing;
using SevenSegmentOcr.Recognition;

const string outputDir = @"D:\projects\ocr";
Directory.CreateDirectory(outputDir);

// ════════════════════════════════════════════════════════════════
// 要處理的圖片清單
// 有 configPath 的圖片 → 直接讀 JSON，跳過互動
// 沒有 configPath 的圖片 → 啟動互動圈選，圈完存 JSON
// ════════════════════════════════════════════════════════════════
var imageConfigs = new[]
{
    new ImageConfig(ImagePath: "./images/1.png", ConfigPath: "./configs/1.json"),
    new ImageConfig(ImagePath: "./images/2.png", ConfigPath: "./configs/2.json"),
    new ImageConfig(ImagePath: "./images/3.png", ConfigPath: "./configs/3.json"),
    new ImageConfig(ImagePath: "./images/16.png", ConfigPath: "./configs/16.json"),
    // 繼續加更多圖片...
};

var roiLoader = new RoiLoader(expandPixels: 0);
var decoder   = new SevenSegmentDecoder();

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

    // ── 取得 ROI 設定 ────────────────────────────────────────────
    RoiDefinition[] configs = GetOrSelectRois(fullImage, imageConfig);
    if (configs.Length == 0)
    {
        Console.WriteLine("  [跳過] 未框選任何 ROI");
        continue;
    }

    // ── 建立輸出子資料夾 ─────────────────────────────────────────
    var imageName   = Path.GetFileNameWithoutExtension(imageConfig.ImagePath);
    var imageOutDir = Path.Combine(outputDir, imageName);
    Directory.CreateDirectory(imageOutDir);

    // ── 處理每個 ROI ─────────────────────────────────────────────
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

        var procPath = Path.Combine(imageOutDir, $"{cfg.Id}_proc.jpg");
        Cv2.ImWrite(procPath, processed);

        Console.WriteLine($"  id={cfg.Id:D2} [{cfg.DeviceType,-11}] → {procPath}");
    }
}

Console.WriteLine($"\n完成，請檢查：{Path.GetFullPath(outputDir)}");

// ════════════════════════════════════════════════════════════════
// 取得 ROI：優先讀 JSON，沒有就互動圈選後存 JSON
// ════════════════════════════════════════════════════════════════
static RoiDefinition[] GetOrSelectRois(Mat fullImage, ImageConfig imageConfig)
{
    // 1. 嘗試讀取現有 JSON
    if (imageConfig.ConfigPath is not null)
    {
        var loaded = RoiConfigStore.TryLoad(imageConfig.ConfigPath);
        if (loaded is { Length: > 0 })
        {
            Console.WriteLine($"  載入 ROI 設定：{imageConfig.ConfigPath} ({loaded.Length} 個)");
            return loaded;
        }
    }

    // 2. 沒有 JSON → 互動圈選
    Console.WriteLine("  進入互動圈選模式");
    Console.WriteLine("  操作：拖曳框選 → SPACE/ENTER 確認 → ESC 完成所有選取");
    var selected = RoiSelector.Select(fullImage);
    if (selected.Length == 0) return selected;

    // 3. 圈選完後詢問 DeviceType
    selected = PromptDeviceTypes(selected);

    // 4. 存 JSON 供下次直接讀取
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

// ════════════════════════════════════════════════════════════════
// 圈選完後，逐一詢問每個 ROI 的 DeviceType
// ════════════════════════════════════════════════════════════════
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

// ════════════════════════════════════════════════════════════════
// ImageConfig：一張圖片的設定
// ConfigPath 為 null 時，每次都重新圈選，不存 JSON
// ════════════════════════════════════════════════════════════════
record ImageConfig(string ImagePath, string? ConfigPath = null);