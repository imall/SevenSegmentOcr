using OpenCvSharp;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Imaging;

/// <summary>
/// 負責將原始 ROI 圖片處理成適合 OCR 的乾淨黑字白底二值圖
/// </summary>
public class ImagePreprocessor(PreprocessorOptions? options = null) : IDisposable
{
    private readonly PreprocessorOptions _options = options ?? new PreprocessorOptions();

    /// <summary>
    /// 完整前處理流程：灰階 → 裁邊 → CLAHE → Bilateral去噪 → 放大 → 二值化 → Opening去噪點 → Closing補筆劃 → 連通區域篩選 → 補邊距 → 確保黑字白底
    /// </summary>
    public Mat Process(Mat input)
    {
        using var gray      = ToGrayscale(input);
        using var cropped   = CropEdgeBands(gray);
        using var equalized = EqualizeLocal(cropped);    // 先增強局部對比
        using var smoothed  = SmoothEdges(equalized);   // 保邊去噪（取代 Gaussian）
        using var resized   = Upscale(smoothed);         // 放大乾淨影像
        using var binary    = Binarize(resized);
        using var opened    = ApplyOpening(binary);      // 移除散點噪聲
        using var morphed   = ApplyMorphology(opened);   // Closing 補筆劃
        using var filtered  = FilterConnectedComponents(morphed); // 面積篩選
        var padded          = AddPadding(filtered);
        return EnsureBlackOnWhite(padded);
    }

    // ── Step 1：轉灰階 ─────────────────────────────────────────────
    private static Mat ToGrayscale(Mat src)
    {
        if (src.Channels() == 1) return src.Clone();
        var gray = new Mat();
        Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        return gray;
    }

    // ── Step 2：處理上方噪點 (裝置上方黑色部分) ─────────────────────────
    private static Mat CropEdgeBands(Mat src, double trimY = 0.08, double trimX = 0.03)
    {
        int ty = (int)(src.Rows * trimY);
        int tx = (int)(src.Cols * trimX);
        var roi = new Rect(tx, ty, src.Cols - tx * 2, src.Rows - ty * 2);
        return new Mat(src, roi);
    }

    // ── Step 3：超解析度放大 ───────────────────────────────────────
    private Mat Upscale(Mat src)
    {
        var enlarged = new Mat();
        Cv2.Resize(src, enlarged, new Size(0, 0),
            _options.UpscaleFactor, _options.UpscaleFactor,
            InterpolationFlags.Cubic);
        return enlarged;
    }

    // ── Step 3：Bilateral Filter 保邊去噪（取代 Gaussian）────────────
    // 七段顯示器為硬邊筆劃，Bilateral 可同時去除感測器噪聲並保留邊緣
    private Mat SmoothEdges(Mat src)
    {
        var result = new Mat();
        Cv2.BilateralFilter(src, result, _options.BilateralD,
            _options.BilateralSigma, _options.BilateralSigma);
        return result;
    }

    private static Mat EqualizeLocal(Mat src)
    {
        var result = new Mat();
        using var clahe = Cv2.CreateCLAHE(clipLimit: 2.0, tileGridSize: new Size(8, 8));
        clahe.Apply(src, result);
        return result;
    }
    
    // ── Step 5：OTSU 自適應二值化 ─────────────────────────────────
    private static Mat Binarize(Mat src)
    {
        var binary = new Mat();

        // 先試 Otsu，檢查閾值是否合理
        var otsuTest = new Mat();
        double otsuThresh = Cv2.Threshold(src, otsuTest, 0, 255,
            ThresholdTypes.Otsu | ThresholdTypes.Binary);

        if (otsuThresh >= 30 && otsuThresh <= 220)
        {
            // Otsu 結果合理，直接用
            return otsuTest;
        }
        otsuTest.Dispose();

        // ✅ Otsu 失效時，改用自適應閾值（對每個局部區域獨立計算）
        Cv2.AdaptiveThreshold(
            src, binary,
            maxValue: 255,
            adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
            thresholdType: ThresholdTypes.Binary,
            blockSize: 31,   // 視字體大小調整，通常是字高的 1~2 倍（奇數）
            c: 10            // 從均值再往下扣的常數，數字越大越激進
        );
        return binary;
    }
    // ── Step 7：Opening（移除二值化後的孤立噪點）────────────────
    private Mat ApplyOpening(Mat binary)
    {
        if (_options.OpeningKernelSize <= 0) return binary.Clone();
        var result = new Mat();
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(_options.OpeningKernelSize, _options.OpeningKernelSize));
        Cv2.MorphologyEx(binary, result, MorphTypes.Open, kernel);
        return result;
    }

    // ── Step 8：Closing（補焊斷開的七段筆劃）─────────────────────
    private Mat ApplyMorphology(Mat binary)
    {
        var result = new Mat();

        var dynamicKernel = _options.MorphKernelSize > 0
            ? _options.MorphKernelSize
            : Math.Max(1, binary.Cols / 80);  // ← 這行是關鍵
        
        
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(dynamicKernel, dynamicKernel));
        
        // Closing = 先膨脹後侵蝕，填補數字中間的縫隙，讓筆劃連續
        Cv2.MorphologyEx(binary, result, MorphTypes.Close, kernel);
        return result;
    }

    // ── Step 9：連通區域面積篩選（去除殘餘噪點與背景污染）────────
    private Mat FilterConnectedComponents(Mat binary)
    {
        // 確保是黑字白底後再做連通分析（黑=0 為前景才能找到字）
        // ConnectedComponents 找白色連通塊，先暫時反轉
        using var inverted = new Mat();
        Cv2.BitwiseNot(binary, inverted);

        using var labels  = new Mat();
        using var stats   = new Mat();
        using var centroids = new Mat();
        int count = Cv2.ConnectedComponentsWithStats(
            inverted, labels, stats, centroids,
            PixelConnectivity.Connectivity8);

        int totalArea = binary.Rows * binary.Cols;
        int minArea   = (int)(totalArea * _options.MinComponentAreaRatio);
        int maxArea   = (int)(totalArea * _options.MaxComponentAreaRatio);

        var result = new Mat(binary.Size(), MatType.CV_8UC1, new Scalar(255));

        for (int i = 1; i < count; i++) // 0 是背景，跳過
        {
            int area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            if (area < minArea || area > maxArea) continue;

            // 將符合條件的連通區域畫回（黑色）
            result.SetTo(new Scalar(0), labels.InRange(new Scalar(i), new Scalar(i)));
        }

        return result;
    }

    // ── Step 10：加白色邊距（Tesseract/OCR 不喜歡文字緊貼邊緣）──
    private Mat AddPadding(Mat src)
    {
        var padded = new Mat();
        Cv2.CopyMakeBorder(src, padded,
            _options.Padding, _options.Padding,
            _options.Padding, _options.Padding,
            BorderTypes.Constant, new Scalar(255));
        return padded;
    }

    // ── Step 8：確保黑字白底 ──────────────────────────────────────
    private static Mat EnsureBlackOnWhite(Mat src)
    {
        // 如果平均亮度 < 127，代表黑底白字，需要反轉
        Scalar mean = Cv2.Mean(src);
        if (mean.Val0 < 127)
        {
            var inverted = new Mat();
            Cv2.BitwiseNot(src, inverted);
            src.Dispose();
            return inverted;
        }
        return src;
    }

    public void Dispose() { }
}
