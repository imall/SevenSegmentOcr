using OpenCvSharp;

namespace SevenSegmentOcr.Recognition;

/// <summary>
/// 從前處理後的二值圖中，偵測每個字符的邊界框。
/// 處理重點：過濾度符號「°」、小數點，以及分割「數字+C」合併的輪廓。
/// </summary>
public class DigitSegmenter
{
    /// <summary>
    /// 輪廓面積下限（佔整圖面積的比例）。低於此值視為噪點。
    /// </summary>
    public double MinAreaRatio { get; set; } = 0.005;

    /// <summary>
    /// 輪廓面積上限（佔整圖面積的比例）。高於此值視為背景噪音。
    /// </summary>
    public double MaxAreaRatio { get; set; } = 0.40;

    /// <summary>
    /// 字符高度下限（佔圖片高度的比例）。低於此值為小數點或度符號，直接捨棄。
    /// </summary>
    public double MinHeightRatio { get; set; } = 0.30;

    /// <summary>
    /// 若最後一個邊界框的寬度超過中位數寬度的此倍數，則嘗試切割。
    /// </summary>
    public double MergeSplitThreshold { get; set; } = 1.5;

    /// <summary>
    /// 垂直投影分析時，谷底值需低於峰值的此比例才認定為有效分割點。
    /// </summary>
    public double ValleyMaxRatio { get; set; } = 0.30;

    /// <summary>
    /// 找出所有字符的邊界框（由左至右排序），並嘗試分割合併的末尾輪廓。
    /// </summary>
    /// <param name="processedImage">前處理後的黑字白底二值圖</param>
    /// <returns>字符邊界框清單（由左至右）</returns>
    public List<Rect> FindCharBoxes(Mat processedImage)
    {
        int totalArea = processedImage.Rows * processedImage.Cols;
        int minArea   = (int)(totalArea * MinAreaRatio);
        int maxArea   = (int)(totalArea * MaxAreaRatio);
        int minHeight = (int)(processedImage.Rows * MinHeightRatio);

        // FindContours 需要黑底白字（物件為白色）；輸入是黑字白底，需先反轉
        using var inverted = new Mat();
        Cv2.BitwiseNot(processedImage, inverted);

        Cv2.FindContours(inverted, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        var boxes = new List<Rect>();
        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < minArea || area > maxArea) continue;

            Rect box = Cv2.BoundingRect(contour);
            if (box.Height < minHeight) continue;   // 過濾度符號「°」與小數點

            boxes.Add(box);
        }

        // 由左至右排序
        boxes.Sort((a, b) => a.X.CompareTo(b.X));

        // 嘗試分割最後一個可能合併的邊界框（數字 + C 或 % 被合在一起）
        boxes = TrySplitLastBox(processedImage, boxes);

        return boxes;
    }

    // ── 合併框分割 ─────────────────────────────────────────────────────────

    private List<Rect> TrySplitLastBox(Mat image, List<Rect> boxes)
    {
        if (boxes.Count < 2) return boxes;

        int medianWidth = GetMedianWidth(boxes);
        Rect last = boxes[^1];

        if (last.Width <= medianWidth * MergeSplitThreshold)
            return boxes;   // 寬度正常，不需切割

        var split = FindVerticalSplitPoint(image, last, medianWidth);
        if (split is null) return boxes;

        int splitX = split.Value;
        var leftBox  = new Rect(last.X,          last.Y, splitX,              last.Height);
        var rightBox = new Rect(last.X + splitX,  last.Y, last.Width - splitX, last.Height);

        boxes.RemoveAt(boxes.Count - 1);
        boxes.Add(leftBox);
        boxes.Add(rightBox);
        return boxes;
    }

    /// <summary>
    /// 對合併區域做垂直投影，找出右半部的最低谷（字符間隙）。
    /// </summary>
    private int? FindVerticalSplitPoint(Mat image, Rect merged, int medianWidth)
    {
        // 裁出合併區域（黑字白底），轉成「暗像素計數」的投影
        using var region = new Mat(image, merged);
        using var invRegion = new Mat();
        Cv2.BitwiseNot(region, invRegion);  // 轉白字黑底方便計數

        int w = region.Cols;
        int h = region.Rows;

        // 每列暗像素數量（= 反轉後的非零像素）
        var projection = new int[w];
        for (int x = 0; x < w; x++)
        {
            using var col = invRegion.Col(x);
            projection[x] = Cv2.CountNonZero(col);
        }

        // 平滑（3 列滑動平均）
        var smoothed = SmoothProjection(projection);

        // 在右側 40% 的範圍內搜尋谷底
        int searchStart = (int)(w * 0.60);
        int maxVal = smoothed.Max();
        int threshold = (int)(maxVal * ValleyMaxRatio);

        int valleyCol = -1;
        int valleyVal = int.MaxValue;
        for (int x = searchStart; x < w - 1; x++)
        {
            if (smoothed[x] < valleyVal)
            {
                valleyVal = smoothed[x];
                valleyCol = x;
            }
        }

        if (valleyCol < 0 || valleyVal > threshold)
            return null;    // 沒有明顯的谷底，不切割

        return valleyCol;
    }

    private static int[] SmoothProjection(int[] proj)
    {
        int n = proj.Length;
        var result = new int[n];
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - 1);
            int hi = Math.Min(n - 1, i + 1);
            int sum = 0;
            for (int j = lo; j <= hi; j++) sum += proj[j];
            result[i] = sum / (hi - lo + 1);
        }
        return result;
    }

    private static int GetMedianWidth(List<Rect> boxes)
    {
        var widths = boxes.Select(b => b.Width).OrderBy(w => w).ToArray();
        return widths[widths.Length / 2];
    }
}
