using OpenCvSharp;
using SevenSegmentOcr.Imaging;

// ════════════════════════════════════════════════════════════════
// 用法：dotnet run -- <圖片路徑>
// 輸出：debug_output/<id>_raw/proc_<x>_<y>.jpg
// ════════════════════════════════════════════════════════════════

var imagePath = args.Length > 0 ? args[0] : "./images/5.png";
var outputDir = "debug_output";
Directory.CreateDirectory(outputDir);

// 對應 Python CONFIG
var configs = new[]
{
    new RoiDefinition(Id:  1, X: 1034, Y:  363,  Width: 238, Height: 122, DeviceType.Circular),
    new RoiDefinition(Id:  2, X: 1822, Y:  423,  Width: 228, Height: 117, DeviceType.Circular),
    new RoiDefinition(Id:  3, X: 2657, Y:  427,  Width: 230, Height: 123, DeviceType.Circular),
    new RoiDefinition(Id:  4, X: 3379, Y:  358,  Width: 254, Height: 144, DeviceType.Rectangular),
    new RoiDefinition(Id:  5, X: 1049, Y: 1570,  Width: 281, Height: 133, DeviceType.Circular),
    new RoiDefinition(Id:  6, X: 1789, Y: 1567,  Width: 267, Height: 112, DeviceType.Circular),
    new RoiDefinition(Id:  7, X: 2537, Y: 1585,  Width: 284, Height: 120, DeviceType.Circular),
    new RoiDefinition(Id:  8, X: 3324, Y: 1550,  Width: 230, Height: 158, DeviceType.Rectangular),
    new RoiDefinition(Id: 11, X: 2536, Y: 2615,  Width: 226, Height: 158, DeviceType.Rectangular),
    new RoiDefinition(Id: 12, X: 3216, Y: 2634,  Width: 250, Height: 159, DeviceType.Circular),
};

// 載入原始圖片
using var fullImage = Cv2.ImRead(imagePath, ImreadModes.Color);
if (fullImage.Empty())
{
    Console.WriteLine($"[錯誤] 無法載入圖片：{imagePath}");
    return;
}
Console.WriteLine($"載入圖片：{imagePath} ({fullImage.Cols}x{fullImage.Rows})\n");

var preprocessor = new ImagePreprocessor();
var roiLoader    = new RoiLoader(expandPixels: 0);

foreach (var cfg in configs)
{
    using var roi = roiLoader.Crop(fullImage, cfg);
    if (roi.Empty()) { Console.WriteLine($"[跳過] id={cfg.Id} ROI 超出圖片範圍"); continue; }

    using var processed = preprocessor.Process(roi);

    // 同時儲存原始裁切和處理後，方便對比
    var rawPath  = Path.Combine(outputDir, $"{cfg.Id}_raw_{cfg.X}_{cfg.Y}.jpg");
    var procPath = Path.Combine(outputDir, $"{cfg.Id}_proc_{cfg.X}_{cfg.Y}.jpg");
    Cv2.ImWrite(rawPath,  roi);
    Cv2.ImWrite(procPath, processed);

    Console.WriteLine($"id={cfg.Id:D2} [{cfg.DeviceType,-11}] → {Path.GetFileName(procPath)}");
}

Console.WriteLine($"\n完成，請檢查：{Path.GetFullPath(outputDir)}");