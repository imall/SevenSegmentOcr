using OpenCvSharp;
using SevenSegmentOcr.Imaging;
using SevenSegmentOcr.Models;
using SevenSegmentOcr.Parsing;
using SevenSegmentOcr.Recognition;

// ════════════════════════════════════════════════════════════════
// 用法：
//   dotnet run -- [圖片路徑] [--select]
//
//   --select  強制重新框選 ROI（忽略現有 roi_config.json）
//
// 流程：
//   1. 載入圖片
//   2. 若無 config 或指定 --select，顯示圖片讓使用者框選 ROI 並寫入 JSON
//   3. 從 JSON 讀取 ROI 設定
//   4. 裁切 + 前處理 + 輸出
// ════════════════════════════════════════════════════════════════

var imagePath  = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "./images/21.png";
var forceSelect = args.Contains("--select");
const string configPath = "roi_config.json";
const string outputDir  = @"D:\projects\ocr";
Directory.CreateDirectory(outputDir);


// ── Step 2：取得 ROI 設定（互動框選 or 讀取 JSON）─────────────
RoiDefinition[] configs;

//  讀取 JSON
// configs = new[]
// {
//     new RoiDefinition(Id:  1, X: 1034, Y:  343,  Width: 238, Height: 122, DeviceType.Circular),
//     new RoiDefinition(Id:  2, X: 1822, Y:  400,  Width: 228, Height: 117, DeviceType.Circular),
//     new RoiDefinition(Id:  3, X: 2657, Y:  420,  Width: 230, Height: 123, DeviceType.Circular),
//     new RoiDefinition(Id:  4, X: 3379, Y:  358,  Width: 280, Height: 144, DeviceType.Rectangular),
//     // new RoiDefinition(Id:  5, X: 1049, Y: 1570,  Width: 281, Height: 133, DeviceType.Circular),
//     // new RoiDefinition(Id:  6, X: 1789, Y: 1567,  Width: 267, Height: 112, DeviceType.Circular),
//     // new RoiDefinition(Id:  7, X: 2537, Y: 1585,  Width: 284, Height: 120, DeviceType.Circular),
//     new RoiDefinition(Id:  8, X: 3324, Y: 1540,  Width: 300, Height: 144, DeviceType.Rectangular),
//     // new RoiDefinition(Id: 11, X: 2536, Y: 2615,  Width: 226, Height: 158, DeviceType.Rectangular),
//     // new RoiDefinition(Id: 12, X: 3216, Y: 2634,  Width: 250, Height: 159, DeviceType.Circular),
// };

// 載入原始圖片
using var fullImage = Cv2.ImRead(imagePath, ImreadModes.Color);
if (fullImage.Empty())
{
    Console.WriteLine($"[錯誤] 無法載入圖片：{imagePath}");
    return;
}
Console.WriteLine($"載入圖片：{imagePath} ({fullImage.Cols}x{fullImage.Rows})");



var loaded = forceSelect ? null : RoiConfigStore.TryLoad(configPath);
if (loaded is { Length: > 0 })
{
    configs = loaded;
    Console.WriteLine($"載入 ROI 設定：{Path.GetFullPath(configPath)} ({configs.Length} 個區域)\n");
}
else
{
    Console.WriteLine(forceSelect
        ? "\n[--select] 強制重新框選 ROI"
        : $"\n找不到 {configPath}，進入互動框選模式");
    Console.WriteLine("操作：拖曳框選 → SPACE/ENTER 確認一個 → ESC 完成所有選取\n");

    configs = RoiSelector.Select(fullImage);

    if (configs.Length == 0)
    {
        Console.WriteLine("[取消] 未框選任何 ROI，結束。");
        return;
    }

    // ── Step 3：寫入 config ────────────────────────────────────
    RoiConfigStore.Save(configs, configPath);
    Console.WriteLine($"已儲存 {configs.Length} 個 ROI → {Path.GetFullPath(configPath)}");
    Console.WriteLine("[提示] 可直接編輯 JSON 中的 DeviceType（Circular / Rectangular）\n");
}

// ── Step 4：裁切 + 前處理 + 辨識 + 儲存 ───────────────────────
var preprocessor = new ImagePreprocessor();
var roiLoader    = new RoiLoader(expandPixels: 0);
var decoder      = new SevenSegmentDecoder();

foreach (var cfg in configs)
{
    using var roi = roiLoader.Crop(fullImage, cfg);
    if (roi.Empty()) { Console.WriteLine($"[跳過] id={cfg.Id} ROI 超出圖片範圍"); continue; }

    using var processed = preprocessor.Process(roi);

    // ── 辨識 ──────────────────────────────────────────────────
    var charBoxes = decoder.DecodeWithBoxes(processed, cfg.DeviceType);
    string rawText = string.Concat(charBoxes.Select(c => c.Character));
    var ocrResult  = ValueParser.Parse(rawText);

    // ── 除錯視覺化：在前處理圖上畫出邊界框與辨識字符 ────────
    using var debugViz = processed.CvtColor(ColorConversionCodes.GRAY2BGR);
    foreach (var (box, ch) in charBoxes)
    {
        Cv2.Rectangle(debugViz, box, new Scalar(0, 0, 220), 2);   // 紅框
        Cv2.PutText(debugViz, ch.ToString(),
            new Point(box.X, Math.Max(0, box.Y - 4)),
            HersheyFonts.HersheySimplex, 0.8,
            new Scalar(0, 180, 0), 2);                              // 綠字
    }

    // ── 儲存圖片 ──────────────────────────────────────────────
    var rawPath  = Path.Combine(outputDir, $"{cfg.Id}_raw.jpg");
    var procPath = Path.Combine(outputDir, $"{cfg.Id}_proc.jpg");
    var segPath  = Path.Combine(outputDir, $"{cfg.Id}_seg.jpg");
    Cv2.ImWrite(rawPath,  roi);
    Cv2.ImWrite(procPath, processed);
    Cv2.ImWrite(segPath,  debugViz);

    // ── 輸出結果 ──────────────────────────────────────────────
    string statusIcon = ocrResult.Success ? "✓" : "✗";
    Console.WriteLine($"id={cfg.Id:D2} [{cfg.DeviceType,-11}] raw='{rawText}' {statusIcon} {ocrResult.Message}");
}

Console.WriteLine($"\n完成，請檢查：{Path.GetFullPath(outputDir)}");