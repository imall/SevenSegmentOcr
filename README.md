# SevenSegmentOcr

七段顯示器 OCR 辨識專案（.NET 8 + OpenCvSharp4）

## 專案結構

```
SevenSegmentOcr/
├── Imaging/
│   ├── ImagePreprocessor.cs     ← OpenCV 前處理主邏輯（調參在這裡）
│   ├── PreprocessorOptions.cs   ← 所有可調參數集中管理
│   ├── RoiLoader.cs             ← 根據固定座標裁切 ROI
│   ├── RoiDefinition.cs         ← ROI 資料模型（DeviceType / DisplayType）
│   └── DebugImageWriter.cs      ← 儲存各階段中間圖（調教用）
├── Models/
│   └── OcrResult.cs             ← 辨識結果資料模型
├── Parsing/
│   └── ValueParser.cs           ← 將 OCR 原始文字解析成數值
├── Recognition/
│   └── SevenSegmentAnalyzer.cs  ← 主協調器（串連所有流程）
└── Program.cs                   ← Console 測試入口
```

## 前處理流程（ImagePreprocessor）

```
原始 ROI 圖
   │
   ▼ ToGrayscale       → 轉灰階
   ▼ RemoveEdgeNoise   → 用連通區域分析去除四周噪音帶
   ▼ Upscale           → 放大 4x（INTER_CUBIC）
   ▼ SmoothStrokes     → Gaussian Blur 消除鋸齒
   ▼ Binarize          → OTSU 自適應二值化
   ▼ ApplyMorphology   → Closing 補焊斷開的七段筆劃
   ▼ AddPadding        → 四周加白邊（OCR 友好）
   ▼ EnsureBlackOnWhite→ 確保黑字白底
```

## 快速開始

```bash
# 建立測試圖片資料夾
mkdir test_images
# 把你的 final_*.jpg 放進去

# 執行前處理測試
dotnet run -- test_images

# 結果在 debug_output/ 資料夾
```

## 主要參數調教指引

| 參數 | 預設值 | 調整建議 |
|------|--------|----------|
| UpscaleFactor | 4.0 | 圖片本來就大的話改 2.0 |
| MorphKernelSize | 5 | 筆劃斷裂嚴重 → 調大；數字黏連 → 調小 |
| MinComponentAreaRatio | 0.001 | 有很多小噪點 → 調大 |
| MaxComponentAreaRatio | 0.35 | 大片背景噪音 → 調小 |
| Padding | 20 | 通常不需要動 |

## NuGet 套件

- `OpenCvSharp4` — OpenCV .NET 綁定
- `OpenCvSharp4.runtime.win` — Windows 原生 library（IIS 環境）

## 下一步

Stage C 的七段像素分析邏輯將實作在：
`Recognition/SevenSegmentDecoder.cs`（待建立）

並在 `SevenSegmentAnalyzer.RunOcr()` 中呼叫。
