using OpenCvSharp;

namespace SevenSegmentOcr.Recognition;

/// <summary>
/// 從前處理後的二值圖中，找出每個數字字元的 bounding box。
/// 使用連通區域分析，依 X 座標由左到右排序。
/// </summary>
public class DigitSegmenter(SegmenterOptions? options = null)
{
    private readonly SegmenterOptions _options = options ?? new SegmenterOptions();

    /// <summary>
    /// 回傳由左到右的數字字元 bounding box 清單。
    /// </summary>
    public List<Rect> FindDigitBoxes(Mat processedImage)
    {
        // 確保是黑字白底（黑色像素是前景）
        using var binary = EnsureBlackOnWhite(processedImage);

        // ← 加這步：確保是純二值圖再做連通區域分析
        using var thresh = new Mat();
        Cv2.Threshold(binary, thresh, 127, 255, ThresholdTypes.BinaryInv);
        // BinaryInv：黑字（< 127）→ 白色前景（255），白底（> 127）→ 黑色背景（0）
        // ConnectedComponents 以白色（255）為前景
        
        
        // 連通區域分析
        using var labels = new Mat();
        using var stats  = new Mat();
        using var cents  = new Mat();
        var n = Cv2.ConnectedComponentsWithStats(thresh, labels, stats, cents);

        var imgArea = thresh.Rows * thresh.Cols;
        var minArea = (int)(imgArea * _options.MinAreaRatio);
        var maxArea = (int)(imgArea * _options.MaxAreaRatio);
        var minH    = (int)(thresh.Rows * _options.MinHeightRatio);
        
        // ── 暫時加這段 debug ──────────────────────────────────────
        Console.WriteLine($"  圖片尺寸：{thresh.Cols}x{thresh.Rows}，總像素：{imgArea}");
        Console.WriteLine($"  minArea={minArea} maxArea={maxArea} minH={minH}");
        Console.WriteLine($"  找到 {n - 1} 個連通區域（不含背景）");
        for (int i = 1; i < n; i++)
        {
            var x    = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            var y    = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            var w    = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var h    = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);
            double ratio = h > 0 ? (double)w / h : 0;
            Console.WriteLine($"    [{i}] x={x} y={y} w={w} h={h} area={area} ratio={ratio:F2}");
        }
        // ──────────────────────────────────────────────────────────

        var boxes = new List<Rect>();

        for (var i = 1; i < n; i++) // i=0 是背景
        {
            var x  = stats.At<int>(i, (int)ConnectedComponentsTypes.Left);
            var y  = stats.At<int>(i, (int)ConnectedComponentsTypes.Top);
            var w  = stats.At<int>(i, (int)ConnectedComponentsTypes.Width);
            var h  = stats.At<int>(i, (int)ConnectedComponentsTypes.Height);
            var area = stats.At<int>(i, (int)ConnectedComponentsTypes.Area);

            if (area < minArea || area > maxArea) continue; // 過濾噪點和大片噪音
            if (h < minH) continue;                         // 過濾 °、小數點（高度不足）
            if (!IsDigitAspectRatio(w, h)) continue;        // 過濾 °C 的 C（太寬或太窄）

            boxes.Add(new Rect(x, y, w, h));
        }

        // 依 X 座標排序（左 → 右）
        boxes.Sort((a, b) => a.X.CompareTo(b.X));

        // 合併距離太近的 box（避免一個數字被切成兩塊）
        boxes = MergeOverlapping(boxes);
        
        // ← 加在這裡
        if (boxes.Count > 3)
            boxes = boxes.Take(3).ToList();

        return boxes;
    }

    /// <summary>
    /// 裁切單一字元並 resize 成標準尺寸，方便後續分析或訓練。
    /// </summary>
    public Mat CropDigit(Mat processedImage, Rect box, int targetSize = 28)
    {
        // 加一點 padding 避免切到邊
        int pad = 4;
        var padded = new Rect(
            Math.Max(0, box.X - pad),
            Math.Max(0, box.Y - pad),
            Math.Min(processedImage.Cols - box.X + pad, box.Width  + pad * 2),
            Math.Min(processedImage.Rows - box.Y + pad, box.Height + pad * 2));

        using var cropped = new Mat(processedImage, padded);
        var resized = new Mat();
        Cv2.Resize(cropped, resized, new Size(targetSize, targetSize),
            interpolation: InterpolationFlags.Area);
        return resized;
    }

    // ── 私用輔助 ────────────────────────────────────────────────

    // 七段數字的長寬比大約在 0.4 ~ 1.2 之間
    // °C 的 C 太寬（> 1.2），° 太小（高度過濾掉了）
    // 1 很窄（< 0.4），但筆劃加上 padding 後通常 > 0.3，可以放寬
    private bool IsDigitAspectRatio(int w, int h) =>
        h > 0 && (double)w / h is >= 0.25 and <= 1.4;

    private static Mat EnsureBlackOnWhite(Mat src)
    {
        // 平均亮度 > 127 代表已是黑字白底，不需反轉
        if (Cv2.Mean(src).Val0 > 127) return src.Clone();
        var inv = new Mat();
        Cv2.BitwiseNot(src, inv);
        return inv;
    }

    /// <summary>
    /// 合併 X 軸重疊或距離過近的 box（閾值：_options.MergeGapThreshold）
    /// </summary>
    private List<Rect> MergeOverlapping(List<Rect> boxes)
    {
        if (boxes.Count == 0) return boxes;

        var merged = new List<Rect> { boxes[0] };

        for (int i = 1; i < boxes.Count; i++)
        {
            var prev = merged[^1];
            var curr = boxes[i];

            int gap = curr.X - (prev.X + prev.Width);

            if (gap <= _options.MergeGapThreshold)
            {
                // 合併：取聯集
                int x1 = Math.Min(prev.X, curr.X);
                int y1 = Math.Min(prev.Y, curr.Y);
                int x2 = Math.Max(prev.X + prev.Width,  curr.X + curr.Width);
                int y2 = Math.Max(prev.Y + prev.Height, curr.Y + curr.Height);
                merged[^1] = new Rect(x1, y1, x2 - x1, y2 - y1);
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }
}