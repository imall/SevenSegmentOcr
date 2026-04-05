using OpenCvSharp;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Imaging;

/// <summary>
/// 互動式 ROI 框選器：將大圖縮小顯示，讓使用者用滑鼠框選多個 ROI，
/// 再將座標換算回原始解析度並回傳 RoiDefinition 陣列。
/// <para>操作方式：拖曳框選 → 按 SPACE 或 ENTER 確認；按 ESC 完成所有選取。</para>
/// </summary>
public static class RoiSelector
{
    private const int MaxDisplayWidth  = 1920;
    private const int MaxDisplayHeight = 1080;

    /// <summary>
    /// 開啟視窗讓使用者框選 ROI。
    /// </summary>
    /// <param name="image">原始全尺寸圖片（不會被修改）</param>
    /// <param name="existingRois">已有的 ROI（顯示為參考用綠框，可為 null）</param>
    /// <param name="startId">自動分配 ID 的起始值</param>
    /// <returns>使用者框選出的 RoiDefinition 陣列（座標已換算回原始尺寸）</returns>
    public static RoiDefinition[] Select(
        Mat image,
        IEnumerable<RoiDefinition>? existingRois = null,
        int startId = 1)
    {
        double scale = ComputeScale(image.Cols, image.Rows);

        // 建立顯示用縮圖，並疊上既有 ROI 綠框
        using var display = new Mat();
        Cv2.Resize(image, display, new Size(0, 0), scale, scale, InterpolationFlags.Area);

        if (existingRois != null)
            DrawExistingRois(display, existingRois, scale);

        const string winTitle = "框選 ROI（SPACE/ENTER 確認一個，ESC 結束全部）";
        var selected  = Cv2.SelectROIs(winTitle, display);
        Cv2.DestroyAllWindows();

        return selected
            .Where(r => r.Width > 0 && r.Height > 0)
            .Select((r, idx) => new RoiDefinition(
                Id:         startId + idx,
                X:          (int)Math.Round(r.X      / scale),
                Y:          (int)Math.Round(r.Y      / scale),
                Width:      (int)Math.Round(r.Width  / scale),
                Height:     (int)Math.Round(r.Height / scale),
                DeviceType: GuessDeviceType(r)))
            .ToArray();
    }

    // ── 私用輔助 ───────────────────────────────────────────────────────────

    private static double ComputeScale(int w, int h)
    {
        double sx = (double)MaxDisplayWidth  / w;
        double sy = (double)MaxDisplayHeight / h;
        return Math.Min(1.0, Math.Min(sx, sy));
    }

    /// <summary>長寬比接近 1:1 → Circular，否則 → Rectangular</summary>
    private static DeviceType GuessDeviceType(Rect r)
    {
        double ratio = (double)r.Width / r.Height;
        return ratio is >= 0.75 and <= 1.33 ? DeviceType.Circular : DeviceType.Rectangular;
    }

    private static void DrawExistingRois(Mat display, IEnumerable<RoiDefinition> rois, double scale)
    {
        foreach (var roi in rois)
        {
            var rect = new Rect(
                (int)(roi.X      * scale),
                (int)(roi.Y      * scale),
                (int)(roi.Width  * scale),
                (int)(roi.Height * scale));

            Cv2.Rectangle(display, rect, Scalar.LimeGreen, thickness: 2);
            Cv2.PutText(display, roi.Id.ToString(),
                new Point(rect.X + 4, rect.Y + 18),
                HersheyFonts.HersheySimplex, 0.6, Scalar.LimeGreen, thickness: 2);
        }
    }
}

