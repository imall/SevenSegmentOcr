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
        var dotBox = FindDecimalDot(processedImage, boxes);

        for (int i = 0; i < boxes.Count; i++)
        {
            Rect box = boxes[i];

            if (dotBox.HasValue && result.Count > 0
                && ShouldInsertDotBefore(dotBox.Value, box, result[^1].Item1))
            {
                result.Add((dotBox.Value, '.'));
                dotBox = null;
            }

            using var charMat = new Mat(processedImage, box);
        
            // ★ 最後一個框且是圓形裝置，優先嘗試辨識 C
            bool isLastBox = (i == boxes.Count - 1);
            char ch = (isLastBox && deviceType == DeviceType.Circular)
                ? RecognizeLastCharCircular(charMat)
                : RecognizeChar(charMat);
            
            result.Add((box, ch));
        }

        result = deviceType == DeviceType.Rectangular
            ? PostProcessRectangular(result)
            : PostProcessCircular(result);

        return result;
    }
    
    /// <summary>
    /// 圓形裝置最後一個框的辨識：C 或 % 優先判斷。
    /// 最後一個框若不像數字，直接判斷是 C 還是 %。
    /// </summary>
    private char RecognizeLastCharCircular(Mat charMat)
    {
        int w = charMat.Cols;
        int h = charMat.Rows;

        bool a = SampleSegment(charMat, SegRect(0.20, 0.02, 0.60, 0.12, w, h));
        bool b = SampleSegment(charMat, SegRect(0.68, 0.08, 0.22, 0.33, w, h));
        bool c = SampleSegment(charMat, SegRect(0.68, 0.55, 0.22, 0.33, w, h));
        bool d = SampleSegment(charMat, SegRect(0.20, 0.86, 0.60, 0.12, w, h));
        bool e = SampleSegment(charMat, SegRect(0.08, 0.55, 0.22, 0.33, w, h));
        bool f = SampleSegment(charMat, SegRect(0.08, 0.08, 0.22, 0.33, w, h));
        bool g = SampleSegment(charMat, SegRect(0.20, 0.44, 0.60, 0.12, w, h));
        Console.WriteLine($"  w={w} h={h} ar={(double)w/h:F2} | a={a} b={b} c={c} d={d} e={e} f={f} g={g}");

        double ar = (double)w / h;

        // C 的唯一可靠特徵：無右上豎(b)，且有左側筆劃(f 或 e)
        // ° 的污染會讓 c/g 誤判，但不會影響 b（° 在右上角但面積小）
        if (!b && (f || e))
            return 'C';

        // % 的特徵：有中橫(g)，無右上豎(b)，無右下豎(c)
        if (!b && !c && g)
            return '%';

        // 回退到一般辨識
        return RecognizeChar(charMat);
    }

    // ── 後處理 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 長型裝置：永遠只顯示溫度，只保留數字和小數點。
    /// 用中位數寬高過濾異常框（°C 合併框、° 殘留等）。
    /// </summary>
    private static List<(Rect, char)> PostProcessRectangular(List<(Rect Box, char Character)> chars)
    {
        if (chars.Count == 0) return chars;

        var sortedWidths  = chars.Select(c => c.Box.Width).OrderBy(w => w).ToArray();
        var sortedHeights = chars.Select(c => c.Box.Height).OrderBy(h => h).ToArray();
        double medianW = sortedWidths[sortedWidths.Length / 2];
        double medianH = sortedHeights[sortedHeights.Length / 2];

        return chars
            .Where(c => c.Box.Width  <= medianW * 1.6
                     && c.Box.Height >= medianH * 0.5
                     && (char.IsDigit(c.Character) || c.Character == '.'))
            .ToList();
    }

    /// <summary>
    /// 圓形裝置：需要保留末尾的 C 或 % 來判斷溫度/濕度類型。
    /// 用中位數寬高過濾異常框，但保留合法的 C / % / 數字 / 小數點。
    /// </summary>
    private static List<(Rect, char)> PostProcessCircular(List<(Rect Box, char Character)> chars)
    {
        if (chars.Count == 0) return chars;

        var sortedWidths  = chars.Select(c => c.Box.Width).OrderBy(w => w).ToArray();
        var sortedHeights = chars.Select(c => c.Box.Height).OrderBy(h => h).ToArray();
        double medianW = sortedWidths[sortedWidths.Length / 2];
        double medianH = sortedHeights[sortedHeights.Length / 2];

        return chars
            .Where(c => c.Box.Width  <= medianW * 1.6
                     && c.Box.Height >= medianH * 0.5
                     && (char.IsDigit(c.Character) || c.Character is '.' or 'C' or '%' or '-'))
            .ToList();
    }

    // ── 段位辨識核心 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 對一個字符圖塊取樣 7 個段位，查表回傳字符。
    /// </summary>
    private char RecognizeChar(Mat charMat)
    {
        int w = charMat.Cols;
        int h = charMat.Rows;

        bool a = SampleSegment(charMat, SegRect(0.20, 0.02, 0.60, 0.12, w, h)); // 頂橫
        bool b = SampleSegment(charMat, SegRect(0.68, 0.08, 0.22, 0.33, w, h)); // 右上豎
        bool c = SampleSegment(charMat, SegRect(0.68, 0.55, 0.22, 0.33, w, h)); // 右下豎
        bool d = SampleSegment(charMat, SegRect(0.20, 0.86, 0.60, 0.12, w, h)); // 底橫
        bool e = SampleSegment(charMat, SegRect(0.08, 0.55, 0.22, 0.33, w, h)); // 左下豎
        bool f = SampleSegment(charMat, SegRect(0.08, 0.08, 0.22, 0.33, w, h)); // 左上豎
        bool g = SampleSegment(charMat, SegRect(0.20, 0.44, 0.60, 0.12, w, h)); // 中橫
        Console.WriteLine($"  w={w} h={h} ar={(double)w/h:F2} | a={a} b={b} c={c} d={d} e={e} f={f} g={g}");

        char result = LookupPattern(a, b, c, d, e, f, g);

        double ar = (double)w / h;

        if (!c && !g && d && e && ar < 1.0)
            return 'C';

        // 4：有右上、右下、左上、中橫，無底橫、無左下
        if (result == '?' && b && c && f && g && !d && !e)
            return '4';

        // 7：有頂橫、右上、右下，無左側段、無中橫，寬高比 < 0.6
        if (result == '?' && a && b && !e && !f && !g && ar < 0.6)
            return '7';

        return result;
    }

    /// <summary>
    /// 對指定矩形區域計算暗像素比例，超過閾值回傳 true（段位亮起）。
    /// </summary>
    private bool SampleSegment(Mat image, Rect zone)
    {
        int x = Math.Max(0, zone.X);
        int y = Math.Max(0, zone.Y);
        int w = Math.Min(zone.Width,  image.Cols - x);
        int h = Math.Min(zone.Height, image.Rows - y);
        if (w <= 0 || h <= 0) return false;

        using var region = new Mat(image, new Rect(x, y, w, h));
        using var inv    = new Mat();
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
        int pat = (a ? 64 : 0) | (b ? 32 : 0) | (c ? 16 : 0)
                | (d ?  8 : 0) | (e ?  4 : 0) | (f ?  2 : 0) | (g ? 1 : 0);

        return pat switch
        {
            0b1111110 => '0',
            0b0110000 => '1',
            0b1101101 => '2',
            0b1100101 => '2',   // 實測：c=F d=F，右下豎沒取到
            0b1111001 => '3',
            0b0110011 => '4',
            0b0110111 => '4',   // 實測：d=T，底橫誤判
            0b1011011 => '5',
            0b1011111 => '6',
            0b1010111 => '6',   // 實測：d=F，底橫沒取到
            0b1110000 => '7',
            0b1110010 => '7',
            0b1111111 => '8',
            0b1111011 => '9',
            0b1001110 => 'C',
            0b1101110 => 'C',   // 實測：b=T，右上豎誤判
            0b0000001 => '-',
            0b0000000 => ' ',
            _ => '?'
        };
    }

    // ── 小數點處理 ───────────────────────────────────────────────────────────

    private static Rect? FindDecimalDot(Mat image, List<Rect> charBoxes)
    {
        if (charBoxes.Count == 0) return null;

        int totalArea  = image.Rows * image.Cols;
        int maxDotArea = (int)(totalArea * 0.003);

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

            if (box.X < charMinX || box.Right > charMaxX) continue;

            double ratio = (double)box.Width / box.Height;
            if (ratio < 0.4 || ratio > 2.5) continue;

            if (box.Y < charMinY + charAvgH * 0.5) continue;

            return box;
        }
        return null;
    }

    private static bool ShouldInsertDotBefore(Rect dot, Rect currentBox, Rect prevBox)
    {
        int dotCenterX = dot.X + dot.Width / 2;
        return dotCenterX > prevBox.Right && dotCenterX < currentBox.X + currentBox.Width / 2;
    }
}