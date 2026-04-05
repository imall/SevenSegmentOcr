using OpenCvSharp;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Recognition;

/// <summary>
/// 七段顯示器字符辨識器。
/// 對每個字符邊界框取樣 7 個段位區域，依點亮/熄滅狀態映射到字符。
/// 支援數字 0-9、字母 C（攝氏）、% 號（濕度）、負號。
/// </summary>
public class SevenSegmentDecoder
{
    private readonly DigitSegmenter _segmenter;

    /// <summary>暗像素比例高於此值，判定該段為「亮起」（ON）。</summary>
    public double DarkThreshold { get; set; } = 0.25;

    /// <summary>建構時可傳入自訂的 DigitSegmenter（方便測試或調參）。</summary>
    public SevenSegmentDecoder(DigitSegmenter? segmenter = null)
    {
        _segmenter = segmenter ?? new DigitSegmenter();
    }

    // ── 公開 API ────────────────────────────────────────────────────────────

    /// <summary>
    /// 從前處理後的二值圖中辨識所有字符，回傳原始字串。
    /// 例如：「25.8C」、「56.4」、「65%」
    /// </summary>
    public string Decode(Mat processedImage, DeviceType deviceType)
    {
        var chars = DecodeWithBoxes(processedImage, deviceType);
        return string.Concat(chars.Select(c => c.Character));
    }

    /// <summary>
    /// 辨識所有字符，同時回傳每個字符的邊界框（供除錯視覺化使用）。
    /// </summary>
    public List<(Rect Box, char Character)> DecodeWithBoxes(Mat processedImage, DeviceType deviceType)
    {
        var boxes = _segmenter.FindCharBoxes(processedImage);
        if (boxes.Count == 0) return [];

        var result = new List<(Rect, char)>();

        // 決定小數點：找最小的輪廓，若符合「近似正方形且矮小」則當小數點
        var dotBox = FindDecimalDot(processedImage, boxes);

        // 辨識每個邊界框
        for (int i = 0; i < boxes.Count; i++)
        {
            Rect box = boxes[i];

            // 插入小數點（若此框左側緊接著小數點位置）
            if (dotBox.HasValue && result.Count > 0 && ShouldInsertDotBefore(dotBox.Value, box, result[^1].Item1))
            {
                result.Add((dotBox.Value, '.'));
                dotBox = null;  // 只插入一次
            }

            using var charMat = new Mat(processedImage, box);
            char ch = RecognizeChar(charMat);
            result.Add((box, ch));
        }

        // 若小數點還沒插入（在最後一個字符後面，不太可能，但防呆）
        // 在這裡不插入，因為小數點不會出現在末尾

        return result;
    }

    // ── 段位辨識核心 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 對一個字符圖塊取樣 7 個段位，查表回傳字符。
    /// </summary>
    private char RecognizeChar(Mat charMat)
    {
        int w = charMat.Cols;
        int h = charMat.Rows;

        // 七個段位的取樣矩形（比例定義，配合黑字白底）
        bool a = SampleSegment(charMat, SegRect(0.10, 0.00, 0.80, 0.14, w, h)); // 頂橫
        bool b = SampleSegment(charMat, SegRect(0.65, 0.10, 0.25, 0.35, w, h)); // 右上豎
        bool c = SampleSegment(charMat, SegRect(0.65, 0.55, 0.25, 0.35, w, h)); // 右下豎
        bool d = SampleSegment(charMat, SegRect(0.10, 0.86, 0.80, 0.14, w, h)); // 底橫
        bool e = SampleSegment(charMat, SegRect(0.10, 0.55, 0.25, 0.35, w, h)); // 左下豎
        bool f = SampleSegment(charMat, SegRect(0.10, 0.10, 0.25, 0.35, w, h)); // 左上豎
        bool g = SampleSegment(charMat, SegRect(0.10, 0.43, 0.80, 0.14, w, h)); // 中橫

        return LookupPattern(a, b, c, d, e, f, g);
    }

    /// <summary>
    /// 對指定矩形區域計算暗像素比例，超過閾值回傳 true（段位亮起）。
    /// </summary>
    private bool SampleSegment(Mat image, Rect zone)
    {
        // 防止取樣區域超出圖片邊界
        int x = Math.Max(0, zone.X);
        int y = Math.Max(0, zone.Y);
        int w = Math.Min(zone.Width,  image.Cols - x);
        int h = Math.Min(zone.Height, image.Rows - y);
        if (w <= 0 || h <= 0) return false;

        using var region = new Mat(image, new Rect(x, y, w, h));

        // 黑字白底：暗像素 = 接近 0 的像素
        // 將影像反轉後計算非零像素數即為暗像素數
        using var inv = new Mat();
        Cv2.BitwiseNot(region, inv);
        int darkPixels = Cv2.CountNonZero(inv);
        double ratio = (double)darkPixels / (w * h);
        return ratio > DarkThreshold;
    }

    private static Rect SegRect(double xRatio, double yRatio, double wRatio, double hRatio, int imgW, int imgH)
        => new((int)(imgW * xRatio), (int)(imgH * yRatio),
               Math.Max(1, (int)(imgW * wRatio)), Math.Max(1, (int)(imgH * hRatio)));

    // ── 辨識表 ──────────────────────────────────────────────────────────────
    //
    //  段位編號：a=頂橫, b=右上豎, c=右下豎, d=底橫, e=左下豎, f=左上豎, g=中橫
    //
    //    aaa
    //   f   b
    //   f   b
    //    ggg
    //   e   c
    //   e   c
    //    ddd
    //
    private static char LookupPattern(bool a, bool b, bool c, bool d, bool e, bool f, bool g)
    {
        // 將 7 個 bool 打包成 byte 方便比對
        // 位元順序：a b c d e f g（MSB 到 LSB，bit 6 → bit 0）
        int pat = (a ? 64 : 0) | (b ? 32 : 0) | (c ? 16 : 0)
                | (d ?  8 : 0) | (e ?  4 : 0) | (f ?  2 : 0) | (g ? 1 : 0);

        return pat switch
        {
            // abcdefg
            0b1110111 => '0',   // a b c d e f . (g=off)
            0b0010010 => '1',   // . b c . . . .
            0b1101101 => '2',   // a b . d e . g
            0b1111001 => '3',   // a b c d . . g
            0b0011011 => '4',   // . b c . . f g
            0b1011011 => '5',   // a . c d . f g    ← 注意：5 的 e=OFF
            0b1011111 => '6',   // a . c d e f g
            0b1110010 => '7',   // a b c . . . .
            0b1111111 => '8',   // a b c d e f g
            0b1111011 => '9',   // a b c d . f g
            // C：頂橫+左上豎+左下豎+底橫，無右側、無中橫
            0b1001110 => 'C',   // a . . d e f .
            // 負號：只有中橫
            0b0000001 => '-',
            // 全滅可能是空格
            0b0000000 => ' ',
            // 無法匹配
            _ => '?'
        };
    }

    // ── 小數點處理 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 從原始圖片中找小數點的位置。
    /// 小數點特徵：面積很小、接近正方形、位於字符列表之間的 Y 軸下方。
    /// </summary>
    private static Rect? FindDecimalDot(Mat image, List<Rect> charBoxes)
    {
        if (charBoxes.Count == 0) return null;

        int totalArea = image.Rows * image.Cols;
        int maxDotArea = (int)(totalArea * 0.003);  // 小數點面積上限 0.3%

        using var inverted = new Mat();
        Cv2.BitwiseNot(image, inverted);
        Cv2.FindContours(inverted, out Point[][] contours, out _,
            RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        int charMinX = charBoxes.Min(b => b.X);
        int charMaxX = charBoxes.Max(b => b.Right);
        int charAvgH = (int)charBoxes.Average(b => b.Height);
        int charMinY = charBoxes.Min(b => b.Y);

        foreach (var contour in contours)
        {
            double area = Cv2.ContourArea(contour);
            if (area < 10 || area > maxDotArea) continue;

            Rect box = Cv2.BoundingRect(contour);

            // 必須在字符列表的 X 範圍內
            if (box.X < charMinX || box.Right > charMaxX) continue;

            // 近似正方形
            double ratio = (double)box.Width / box.Height;
            if (ratio < 0.4 || ratio > 2.5) continue;

            // 位於字符下半部（y > 字符起點 + 字符高度的 50%）
            if (box.Y < charMinY + charAvgH * 0.5) continue;

            return box;
        }
        return null;
    }

    private static bool ShouldInsertDotBefore(Rect dot, Rect currentBox, Rect prevBox)
    {
        // 小數點的 X 中心在前一個字符和當前字符之間
        int dotCenterX = dot.X + dot.Width / 2;
        return dotCenterX > prevBox.Right && dotCenterX < currentBox.X + currentBox.Width / 2;
    }
}
