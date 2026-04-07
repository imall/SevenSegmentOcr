using OpenCvSharp;
using SevenSegmentOcr.Models;

namespace SevenSegmentOcr.Imaging;

/// <summary>
/// 根據人工標記的固定 ROI 座標，從原始圖片裁切出感興趣區域
/// </summary>
public class RoiLoader(int expandPixels = 10) : IDisposable
{
    /// <summary>
    /// 從磁碟載入圖片並裁切 ROI
    /// </summary>
    public Mat LoadAndCrop(string imagePath, RoiDefinition roi)
    {
        var img = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (img.Empty())
            throw new FileNotFoundException($"無法載入圖片：{imagePath}");
        return Crop(img, roi);
    }

    /// <summary>
    /// 從已載入的 Mat 裁切 ROI
    /// </summary>
    public Mat Crop(Mat image, RoiDefinition roi)
        => CropInternal(image, roi.X, roi.Y, roi.Width, roi.Height);

    private Mat CropInternal(Mat image, int x, int y, int w, int h)
    {
        var left   = Math.Max(0, x - expandPixels);
        var top    = Math.Max(0, y - expandPixels);
        var right  = Math.Min(image.Cols, x + w + expandPixels);
        var bottom = Math.Min(image.Rows, y + h + expandPixels);

        if (right <= left || bottom <= top) return new Mat();
        return new Mat(image, new Rect(left, top, right - left, bottom - top));
    }

    public void Dispose() { }
}
