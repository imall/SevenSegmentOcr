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
    /// 完整前處理流程：去噪 → 放大 → 二值化 → 形態學處理 → 補邊距
    /// </summary>
    public Mat Process(Mat input)
    {
        using var gray     = ToGrayscale(input);
        using var cropped  = CropEdgeBands(gray); 
        using var resized  = Upscale(cropped);
        using var blurred  = SmoothStrokes(resized);
        using var equalized = EqualizeLocal(blurred); 
        using var binary   = Binarize(equalized); 
        using var morphed  = ApplyMorphology(binary);
        var padded         = AddPadding(morphed);
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

    // ── Step 4：平滑筆劃（消除 OCR 困惑的鋸齒邊緣）──────────────
    private static Mat SmoothStrokes(Mat src)
    {
        var blurred = new Mat();
        Cv2.GaussianBlur(src, blurred, new Size(5, 5), 0);
        return blurred;
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
    // ── Step 6：形態學處理（補焊斷開的七段筆劃）─────────────────
    private Mat ApplyMorphology(Mat binary)
    {
        var result = new Mat();
        // using var kernel = Cv2.GetStructuringElement(
        //     MorphShapes.Rect,
        //     new Size(_options.MorphKernelSize, _options.MorphKernelSize));
        int dynamicKernel = _options.MorphKernelSize > 0
            ? _options.MorphKernelSize
            : Math.Max(1, binary.Cols / 80);  // ← 這行是關鍵
        
        
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(dynamicKernel, dynamicKernel));
        
        // Closing = 先膨脹後侵蝕，填補數字中間的縫隙，讓筆劃連續
        Cv2.MorphologyEx(binary, result, MorphTypes.Close, kernel);
        return result;
    }

    // ── Step 7：加白色邊距（Tesseract/OCR 不喜歡文字緊貼邊緣）──
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
