using OpenCvSharp;
using Tesseract;

namespace SevenSegmentOcr.Recognition;

public class OcrRunner : IDisposable
{
    private readonly TesseractEngine _engine;

    public OcrRunner(string tessDataPath, string language = "lets")
    {
        _engine = new TesseractEngine(tessDataPath, language, EngineMode.Default);
        _engine.SetVariable("tessedit_char_whitelist", "0123456789.Cc");
    }

    /// <summary>
    /// 對前處理圖執行 OCR，回傳 (rawText, errorMessage)
    /// </summary>
    public (string? Raw, string? Error) Recognize(Mat processedMat)
    {
        try
        {
            int targetHeight = 100;
            double scale = (double)targetHeight / processedMat.Rows;
            using var resized = new Mat();
            Cv2.Resize(processedMat, resized, new Size(0, 0), scale, scale, InterpolationFlags.Cubic);

            Cv2.ImEncode(".png", resized, out byte[] buf);
            using var pix  = Pix.LoadFromMemory(buf);
            using var page = _engine.Process(pix, PageSegMode.SingleLine);
            return (page.GetText().Trim(), null);
        }
        catch (Exception ex)
        {
            return (null, $"Tesseract 例外：{ex.Message}");
        }
    }

    public void Dispose() => _engine.Dispose();
}
