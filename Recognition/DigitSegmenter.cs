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
    public double ValleyMaxRatio { get; set; } = 0.40;

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
        boxes = MergeVerticallyAdjacentBoxes(boxes, processedImage);
        boxes = TrySplitWideBoxes(processedImage, boxes);

        return boxes;
    }
    
    
    /// <summary>
    /// 合併 X 軸大幅重疊且垂直相鄰的框（同一個數字被斷成兩段）
    /// </summary>
    private static List<Rect> MergeVerticallyAdjacentBoxes(List<Rect> boxes, Mat image)
    {
        if (boxes.Count < 2) return boxes;
    
        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < boxes.Count - 1; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];

                    // X 軸重疊程度（重疊寬度 / 較小框的寬度）
                    int overlapX = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
                    int minW = Math.Min(a.Width, b.Width);
                    if (overlapX < minW * 0.5) continue; // X 軸重疊不足 50%，跳過

                    // 垂直間距（兩框之間的空隙）
                    int gap = Math.Max(a.Y, b.Y) - Math.Min(a.Bottom, b.Bottom);
                    int avgH = (a.Height + b.Height) / 2;
                    if (gap > avgH * 0.3) continue; // 間距太大，不合併

                    // 合併
                    int x = Math.Min(a.X, b.X);
                    int y = Math.Min(a.Y, b.Y);
                    int right = Math.Max(a.Right, b.Right);
                    int bottom = Math.Max(a.Bottom, b.Bottom);
                    boxes[i] = new Rect(x, y, right - x, bottom - y);
                    boxes.RemoveAt(j);
                    merged = true;
                    break;
                }
                if (merged) break;
            }
        }
        return boxes;
    }

    // ── 改成處理所有過寬的框 ──────────────────────────────────────────────
    private List<Rect> TrySplitWideBoxes(Mat image, List<Rect> boxes)
    {
        if (boxes.Count == 0) return boxes;

        int medianWidth = GetMedianWidth(boxes);
        var result = new List<Rect>();

        foreach (var box in boxes)
        {
            if (box.Width > medianWidth * MergeSplitThreshold)
            {
                var parts = SplitBox(image, box, medianWidth);
                result.AddRange(parts);
            }
            else
            {
                result.Add(box);
            }
        }

        result.Sort((a, b) => a.X.CompareTo(b.X));
        return result;
    }
    
    private List<Rect> SplitBox(Mat image, Rect box, int targetWidth)
    {
        // 嘗試在此框內找垂直分割點（可能有多個）
        var splitPoints = FindAllSplitPoints(image, box, targetWidth);

        if (splitPoints.Count == 0)
            return [box];

        // 依分割點切開
        var parts = new List<Rect>();
        int prevX = 0;
        foreach (int sp in splitPoints)
        {
            parts.Add(new Rect(box.X + prevX, box.Y, sp - prevX, box.Height));
            prevX = sp;
        }
        parts.Add(new Rect(box.X + prevX, box.Y, box.Width - prevX, box.Height));

        // ★ 過濾掉切出來太細的碎片（可能是 °C 的 ° 殘留）
        parts = parts.Where(p => p.Width > targetWidth * 0.25).ToList();

        return parts.Count > 0 ? parts : [box];
    }
    
    
    /// <summary>
    /// 對合併框做垂直投影，找出所有谷底（支援多個分割點）
    /// </summary>
    private List<int> FindAllSplitPoints(Mat image, Rect merged, int targetWidth)
    {
        using var region = new Mat(image, merged);
        using var invRegion = new Mat();
        Cv2.BitwiseNot(region, invRegion);

        int w = region.Cols;

        var projection = new int[w];
        for (int x = 0; x < w; x++)
        {
            using var col = invRegion.Col(x);
            projection[x] = Cv2.CountNonZero(col);
        }

        var smoothed = SmoothProjection(projection);
        int maxVal = smoothed.Max();
        int threshold = (int)(maxVal * ValleyMaxRatio);

        // 找所有局部最小值（谷底），且谷底值 < threshold
        var valleys = new List<int>();
        // 最少要過了第一個字符寬度才開始找
        int searchStart = (int)(targetWidth * 0.5);

        for (int x = searchStart + 1; x < w - 1; x++)
        {
            if (smoothed[x] < smoothed[x - 1] && smoothed[x] < smoothed[x + 1]
                && smoothed[x] <= threshold)
            {
                // 避免兩個谷底太近（至少間隔 targetWidth * 0.4）
                if (valleys.Count == 0 || x - valleys[^1] > targetWidth * 0.4)
                    valleys.Add(x);
            }
        }

        return valleys;
    }

    private static int[] SmoothProjection(int[] proj)
    {
        int n = proj.Length;
        var result = new int[n];
        // 用較大的視窗（5點）讓谷底更明顯
        for (int i = 0; i < n; i++)
        {
            int lo = Math.Max(0, i - 2);
            int hi = Math.Min(n - 1, i + 2);
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
