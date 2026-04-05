namespace SevenSegmentOcr.Recognition;

public class SegmenterOptions
{
    /// <summary>連通區域最小面積佔整圖比例（過濾小噪點）</summary>
    public double MinAreaRatio { get; set; } = 0.005;

    /// <summary>連通區域最大面積佔整圖比例（過濾大片噪音）</summary>
    public double MaxAreaRatio { get; set; } = 0.40;

    /// <summary>字元最小高度佔圖片高度比例（過濾 ° 符號）</summary>
    public double MinHeightRatio { get; set; } = 0.3;

    /// <summary>兩個 box 的 X 軸間距小於此值時合併（像素，放大後的圖）</summary>
    public int MergeGapThreshold { get; set; } = 10;
}
