namespace SevenSegmentOcr.Models;

/// <summary>
/// ImagePreprocessor 的可調參數，集中管理方便調教
/// </summary>
public class PreprocessorOptions
{
    /// <summary>
    /// 放大倍率。建議 3~4，太大會讓形態學處理過度。
    /// </summary>
    public double UpscaleFactor { get; set; } = 2.0;

    /// <summary>
    /// 形態學 Closing 的 kernel 大小（像素，放大後的圖）。
    /// 調大→更能補斷裂筆劃，但可能讓數字黏在一起。
    /// </summary>
    public int MorphKernelSize { get; set; } = 3;

    /// <summary>
    /// OCR 前在圖片四周加的白色邊距（像素，放大後的圖）。
    /// </summary>
    public int Padding { get; set; } = 10;

    /// <summary>
    /// 去邊緣噪音時，連通區域「最小」佔整圖面積的比例。
    /// 低於此值視為噪點被捨棄。
    /// </summary>
    public double MinComponentAreaRatio { get; set; } = 0.001; // 0.1%

    /// <summary>
    /// 去邊緣噪音時，連通區域「最大」佔整圖面積的比例。
    /// 高於此值視為大片背景噪音被捨棄。
    /// </summary>
    public double MaxComponentAreaRatio { get; set; } = 0.35; // 35%
}
