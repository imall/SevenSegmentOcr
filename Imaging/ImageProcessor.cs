using OpenCvSharp;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Imaging;

public class ImageProcessor(RoiLoader roiLoader)
{
    /// <summary>
    /// 處理單一圖片的所有 ROI，回傳前處理結果清單
    /// </summary>
    public List<ProcessedRoi> Process(Mat fullImage, RoiDefinition[] configs)
    {
        var results = new List<ProcessedRoi>();

        foreach (var cfg in configs)
        {
            using var roi = roiLoader.Crop(fullImage, cfg);

            if (roi.Empty())
            {
                results.Add(new ProcessedRoi(cfg, Processed: null, Error: "ROI 超出圖片範圍"));
                continue;
            }

            using var preprocessor = new ImagePreprocessor(cfg.Options);
            var processed = preprocessor.Process(roi); // 呼叫端負責 Dispose
            results.Add(new ProcessedRoi(cfg, processed, Error: null));
        }

        return results;
    }

    /// <summary>
    /// 儲存前處理圖到指定資料夾
    /// </summary>
    public static void SaveProcessed(Mat processed, string outputDir, string imageName, int roiId)
    {
        var path = Path.Combine(outputDir, imageName, $"{roiId}_proc.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Cv2.ImWrite(path, processed);
    }
}

/// <summary>單一 ROI 的前處理結果</summary>
public record ProcessedRoi(RoiDefinition Config, Mat? Processed, string? Error);
