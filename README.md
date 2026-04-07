# SevenSegmentOcr

七段顯示器溫濕度 OCR 辨識專案（.NET 8 + OpenCvSharp4 + Tesseract）

自動識別圓形/長形計量裝置上的七段顯示數值，支援溫度、濕度自動判別。

## 專案結構

```
SevenSegmentOcr/
├── images/                      ← 📁 放置待處理的原始圖片（PNG/JPG）
├── configs/                     ← 📁 自動生成的 ROI 配置文件（JSON）
├── Imaging/
│   ├── ImagePreprocessor.cs     ← OpenCV 前處理主邏輯
│   ├── ImageProcessor.cs        ← 處理流程協調器
│   ├── RoiSelector.cs           ← 互動式 ROI 圈選工具
│   ├── RoiLoader.cs             ← ROI 座標讀取
│   └── RoiConfigStore.cs        ← ROI 配置持久化
├── Models/
│   ├── OcrResult.cs
│   ├── PreprocessorOptions.cs
│   └── RoiDefinition.cs         ← ROI 資料模型（圓形/長形）
├── Parsing/
│   └── ValueParser.cs           ← 值解析（暫未使用）
├── Recognition/
│   └── OcrRunner.cs             ← Tesseract OCR 執行器
└── Program.cs                   ← 主程式入口
```

## 快速開始

### 前置要求
- **.NET 8 SDK** 已安裝
- **Tessdata 語言包** 已在 `./Tessdata/` 資料夾中（預設包含 `letsgodigital.traineddata`）

### 步驟 1：準備圖片
1. 將待掃描的圖片放入 **`images/`** 資料夾
   - 支援格式：PNG, JPG
   - 例如：`images/1.png`, `images/2.png` 等

### 步驟 2：編輯 Program.cs 設置

在 `Program.cs` 中修改以下配置：

```csharp
// 第 1-3 行：自動計算（無需改）
var projectRoot = GetProjectRoot();
var configsDir = Path.Combine(projectRoot, "configs");
var imagesDir = Path.Combine(projectRoot, "images");

// ⚙️ 修改輸出路徑（第 4 行）
const string outputDir = @"D:\projects\ocr";  // ← 改成你的輸出資料夾

// ⚙️ 啟用/禁用 OCR（第 5 行）
const bool runOcr = true;  // true = 執行 OCR; false = 僅前處理

// ⚙️ 選擇要處理的圖片（第 9-37 行）
var imageConfigs = new[]
{
    new ImageConfig(Path.Combine(imagesDir, "1.png"), Path.Combine(configsDir, "1.json")),
    new ImageConfig(Path.Combine(imagesDir, "2.png"), Path.Combine(configsDir, "2.json")),
    // ... 取消註釋需要的圖片，或註釋不需要的圖片
};
```

### 步驟 3：運行程式

```bash
dotnet run
```

### 步驟 4：首次設置 ROI（互動式）

首次掃描新圖片時，程式會進入 **互動圈選模式**：

1. **圈選感應器區域**
   - 滑鼠拖拽框選七段顯示區域
   - 按 **SPACE** 或 **ENTER** 確認
   - 按 **ESC** 完成所有 ROI 圈選

2. **選擇感應器類型**
   ```
   ROI id=1 [c=圓形 / r=長形，預設=圓形]：r
   ROI id=2 [c=圓形 / r=長形，預設=圓形]：c
   ```
   - 輸入 `c` 或留白 = 圓形（溫濕度自動判別）
   - 輸入 `r` = 長形（直接識別為溫度）

3. **配置自動保存**
   - ROI 座標會自動保存至 `configs/{imageName}.json`
   - 再次運行時自動載入，無需重新圈選

### 步驟 5：查看結果

程式輸出 JSON 格式的結果：

```json
{
  "imagePath": "D:\\GitHub\\SevenSegmentOcr\\images\\14.png",
  "results": [
    {
      "id": 1,
      "deviceType": "Circular",
      "rawOcr": "25.36",
      "value": "25.3",
      "unit": "°C",
      "success": true,
      "errorReason": null
    },
    {
      "id": 2,
      "deviceType": "Circular",
      "rawOcr": "65.2",
      "value": "65.2",
      "unit": "%",
      "success": true,
      "errorReason": null
    }
  ]
}
```

## 輸出路徑配置

### 圖片前處理結果
- **位置**：`{outputDir}/processed_{imageName}_{roiId}.png`
- **說明**：經過 OCR 前處理後的七段數位影像

### 完整結果
- **位置**：控制台標準輸出（STDOUT）
- **格式**：JSON 格式
- **建議**：將輸出重定向到文件
  ```bash
  dotnet run > results.json 2>&1
  ```

## 識別邏輯

### 圓形裝置（Circular）
依據小數點位數自動判別：
- **小數點後 ≥ 2 位**（如 25.36）→ 溫度 (°C)
- **小數點後 1 位或無**（如 65.2）→ 濕度 (%)
- **或依數值範圍**：0-100 → 濕度；負數或 > 100 → 溫度

### 長形裝置（Rectangular）
- 固定識別為溫度 (°C)
- 小數點後保留最多 1 位

### 後處理流程
1. 移除所有空白字元
2. 保留只數字和小數點
3. 若多個小數點，只保留第一個
4. 轉換為數值並格式化

## 前處理流程（圖片優化）

```
原始 ROI 圖
   │
   ▼ 轉灰階
   ▼ 移除邊界雜訊（連通區域分析）
   ▼ 放大 4 倍（高質量縮放）
   ▼ 高斯模糊（消除鋸齒）
   ▼ 大津法二值化（自適應閾值）
   ▼ 形態學操作 Closing（補焊斷開筆劃）
   ▼ 加白邊框（OCR 友好格式）
   ▼ 確保黑字白底
```

## NuGet 依賴

- `OpenCvSharp4@4.9.0` — OpenCV .NET 綁定
- `OpenCvSharp4.runtime.win@4.9.0` — Windows 運行時
- `Tesseract@5.2.0` — OCR 引擎

> 💡 如需在 Linux/Mac 上運行，請修改 `.csproj` 中的 runtime 參考

## 常見問題

### Q: 圈選後配置在哪裡？
**A**: 自動保存在 `configs/{imageName}.json`，格式如下：
```json
[
  {
    "id": 1,
    "x": 100,
    "y": 200,
    "width": 80,
    "height": 80,
    "deviceType": "Circular"
  }
]
```

### Q: 如何跳過 OCR 只看前處理結果？
**A**: 在 Program.cs 改為 `const bool runOcr = false;`

### Q: 輸出路徑怎麼改？
**A**: 修改 Program.cs 第 4 行 `const string outputDir = @"...";`

### Q: 如何重新圈選已有的配置？
**A**: 刪除 `configs/{imageName}.json` 後重新運行

### Q: OCR 結果不準怎麼辦？
**A**: 檢查圖片品質、圈選範圍是否準確；或嘗試調整前處理參數（但原始碼需編譯）
