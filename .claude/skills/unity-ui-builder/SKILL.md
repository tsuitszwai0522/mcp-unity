---
name: unity-ui-builder
description: Build Unity UGUI using MCP Unity tools. Use when user wants to build UI in Unity from any source: Figma designs, text descriptions, wireframes, screenshots, or specifications.
---

# Unity UI Builder for Claude Code

此規則為 Claude Code 透過 MCP Unity 工具建構 Unity UGUI 的行為規範。支援多種設計輸入：Figma 設計稿、文字描述、wireframe、截圖、規格表。

> 通用 MCP 工具注意事項（Prefab 工作流、Asset Reference、Scene 物件引用、Material 等）請參考 `unity-mcp-workflow`。

## 觸發條件 (When to Activate)

- 使用者提供 Figma 連結要求建構 UI（→ Figma 模式）
- 使用者口頭描述想要的 UI（→ 描述模式）
- 使用者提供 wireframe / 截圖（→ 視覺參考模式）
- 使用者提供 UI 規格表（→ 規格模式）
- 使用者說「建 UI」、「做一個介面」、「建構 UI」、「Figma 轉 Unity」

## 核心規則 (Core Rules)

1. **四步分離建構**：UI 建構分為四個明確步驟，每步之間有驗證門檻。禁止在同一步驟中同時處理定位、Layout Group、和自適應。
2. **Step 1 統一 topLeft**：首次放置所有元素時，**一律使用 `topLeft` + pivot(0,1) + 絕對值 width/height**，不做任何 anchor 判斷。
3. **計劃先行**：必須先完成建構計劃（階層樹 + 屬性表格），**呈現給使用者確認後**才能呼叫任何 MCP 建構工具。
4. **批次優先**：使用 `batch_execute` 批次建立相關元素，單次上限 100 個操作。
5. **每步驗證**：每個步驟完成後用截圖比對確認，再進入下一步。

## UGUI 建構規則

### Canvas 標準設定

`create_canvas`: ScreenSpaceOverlay, ScaleWithScreenSize, referenceResolution **1920×1080**, screenMatchMode **Expand**。標準階層：`TestCanvas → View(stretch-fill) → Container`。

### 色彩轉換

Hex → Unity RGB (0-1)：每通道除以 255。`#426B1F` → `(0.259, 0.420, 0.122)`。

### Anchor Preset 參考表（Step 4 自適應時使用）

> **Step 1 統一使用 `topLeft`，此表僅在 Step 4 參考。**

| 使用情境 | anchorPreset | pivot | sizeDelta 語義 |
|----------|-------------|-------|---------------|
| 絕對定位 | `topLeft` | (0, 1) | (width, height) 絕對值 |
| 水平填滿 | `topStretch` | (0.5, 1) | (leftOffset+rightOffset, height) |
| 填滿父層 | `stretch` | (0.5, 0.5) | (0, 0) 四邊 offset 為 0 |
| 置中 | `middleCenter` | (0.5, 0.5) | (width, height) 絕對值 |
| 右對齊 | `topRight` | (1, 1) | (width, height) 絕對值 |

**重要**：改變 anchor 後，`sizeDelta` 語義會改變。`stretch`/`topStretch` 時 sizeDelta 不再是寬高，而是 offset。改 anchor 時必須同步重算 sizeDelta。

## MCP 工具注意事項 — UI 專屬

| 陷阱 | 說明 |
|------|------|
| CanvasScaler | referenceResolution 固定 1920×1080 + Expand，不可用設計畫面尺寸 |
| Button 文字 | 子物件名 `Text`，元件 `UnityEngine.UI.Text`，非 TMP |
| Button 背景 | `elementData.color` 是 Image 背景色，非文字色 |
| TMP 元件名 | componentName 為 `TMPro.TextMeshProUGUI` |
| TMP alpha | 建立 TMP 時 `color` 未指定 `a` 預設為 1（不透明），需半透明時才需明確帶 `a` |
| Viewport alpha | ScrollRect Viewport Image alpha 必須為 1，Mask stencil 才能正常運作。`showMaskGraphic: false` 隱藏 Image |
| localScale | 所有 UI 元素 localScale 保持 (1,1,1) |
| UI 物件用 create_ui_element | Canvas 下用 `update_gameobject` 建立的 GO 不含 CanvasRenderer 等 UI 元件，應用 `create_ui_element` |

## 執行流程 (Workflow)

> **核心原則**：四步分離建構。
> ```
> 前置：Design Input Analysis → Step 0: Asset Preparation
>   → Build Planning（使用者確認）
>   → Step 1: Faithful Layout（1:1 定位）  ← 驗證門檻
>   → Step 2: Prefab Analysis（元件化）    ← 驗證門檻
>   → Step 3: Layout Group Injection（佈局注入）← 驗證門檻
>   → Step 4: Adaptive/Responsive（自適應）  ← 驗證門檻
>   → Save
> ```

### 前置階段：設計輸入分析

根據輸入源分支。**此階段只提取資料，不做 Layout Group 分析。**

**A. Figma 模式**（使用者提供 Figma 連結）：
1. `get_figma_data` 取得節點佈局。資料過大時改用 `get_metadata`（sparse XML）取得概覽。
1b.（Dev Mode MCP）`get_design_context` 取得語義化佈局資訊。
1c.（Dev Mode MCP）`get_variable_defs` 取得 Design Token → 色彩表/字型表的**權威來源**。
1d.（Dev Mode MCP）`get_screenshot` 截取 frame，作為 layout fidelity 基準。
2. 分析結構：建立色彩表/字型表/階層樹。**記錄每個元素的 Figma 座標與尺寸**。

**B. 描述模式**（使用者口頭描述）：
1. 從描述中提取 UI 結構需求。
2. 確認不明確的部分（元素數量、排列方式、色彩偏好、尺寸需求、是否滾動）。
3. 建立初步階層結構。

**C. 視覺參考模式**（截圖 / wireframe）：
1. 分析圖片中的 UI 元素與佈局。
2. 推斷色彩、尺寸、間距。
3. 建立階層結構。

**D. 規格模式**（結構化規格表）：
1. 直接解析規格中的元素定義。
2. 轉換為階層結構。

### Step 0：Asset Preparation（僅 Figma 模式，強制）

1. 列出所有需要下載的圖片 node。
2. **建議下載路徑並向使用者確認**：
   - 已知 Feature → `Assets/ProjectT/AddressablesAssets/UI/{Feature}/Sprites/{SubFolder}/`
   - 探索/測試 → `Assets/ProjectT/Placeholder/figma_{design_name}/`
   - 向使用者確認：「圖片將下載至 `{path}`，OK？」
3. 使用者確認後，用 `download_figma_images` 下載所有圖片。
4. 用 `batch_execute` + `import_texture_as_sprite` 匯入為 Sprite（預設 `spriteMode: "Single"`, `meshType: "FullRect"`, `compression: "None"`）。
5. 建立 Sprite 對照表（Figma node name → Unity sprite path）。

### Build Planning（強制門檻）

1. **Hierarchy Plan**：**所有元素統一 `topLeft` + pivot(0,1)**，標註 Figma 座標與尺寸。
2. **屬性表格**：每個元素的 position (x, -y)、size (w×h)、備註。
3. **標記 Prefab 候選**（重複元件）和 **Layout Group 候選**（但不在 Step 1 套用）。
4. **等待使用者確認**：獲得批准後才進入 Step 1。

### Step 1：Faithful Layout（1:1 定位）

> **所有元素統一 `topLeft` + pivot(0,1) + 絕對值。禁止使用其他 anchor。**

1. 檢查 / 建立 TestCanvas（1920×1080, Expand）。
2. 建立 View（stretch-fill）。
3. 用 `batch_execute` 一次建立所有 UI 元素：
   - `anchorPreset: "topLeft"`, `pivot: {x:0, y:1}`
   - `anchoredPosition: {x: figma_x, y: -figma_y}`
   - `sizeDelta: {x: figma_width, y: figma_height}`
4. 處理 Button 文字（`update_component` 設定 `Text` 子物件顏色）。
5. 指定 Sprite（`update_component` → Image `sprite`）。
6. **驗證門檻**：`screenshot_game_view` vs Figma 截圖，確認位置正確、無遺漏。

### Step 2：Prefab Analysis（元件化）

1. 確認 Prefab 候選（出現 ≥2 次的相同結構）。
2. 第一個實例 `save_as_prefab`（路徑 `Assets/Prefabs/{DesignName}/`）。
3. 刪除其餘重複實例，用 `add_asset_to_scene` 放置 Prefab 實例 + `update_component` 更新差異。
4. **驗證門檻**：所有實例 localScale (1,1,1)，視覺效果一致。

### Step 3：Layout Group Injection（佈局注入）

> 加入 Layout Group 後，子元素位置由 Layout 接管。

1. **執行 Layout Group 分析**：Figma Auto Layout 優先；無則用座標規律演算法（x/y/w/h 計算 gap）。向使用者呈現分析結果。
2. **套用 Layout Group**：`update_component` 加入 HLG/VLG/Grid，設定 spacing、padding。
3. **ScrollRect**（需要時）：按規範重組結構（ScrollRect → Viewport+RectMask2D → Content+LayoutGroup），用 `reparent_gameobject` 移動子元素。
4. **驗證門檻**：`screenshot_game_view` 確認 Layout Group 沒有破壞佈局。

### Step 4：Adaptive/Responsive（自適應，可選）

> 若使用者不需要自適應，可跳過直接儲存。

1. 分析每個元素的自適應需求（水平填滿 → topStretch、填滿父層 → stretch、置中 → middleCenter、固定 → 保持 topLeft）。
2. 用 `set_rect_transform` 修改 anchor + 重算 sizeDelta。
3. **驗證門檻**：改變 Game View 解析度測試自適應效果。

### Final：儲存

使用 `save_scene` 儲存場景。

## Figma 專屬參考

### Figma → Unity 座標對應（Step 1 使用）

| Figma 屬性 | Unity (topLeft, pivot 0,1) | 說明 |
|------------|---------------------------|------|
| X | anchoredPosition.x = X | 直接對應 |
| Y | anchoredPosition.y = -Y | Y 取負 |
| Width | sizeDelta.x = Width | 直接對應 |
| Height | sizeDelta.y = Height | 直接對應 |

### Figma Layout Group 補充（Step 3 使用）

Auto Layout 直接對應：`HORIZONTAL` → HorizontalLayoutGroup，`VERTICAL` → VerticalLayoutGroup。提取 `itemSpacing` → `spacing`、`padding`。無 Auto Layout 時 fallback 到座標規律演算法。

## 禁止事項 (Don'ts)

1. ❌ **Step 1 中使用 `topLeft` 以外的 anchor**
2. ❌ **Step 1 中加入 Layout Group 或 ScrollRect**
3. ❌ **Step 1 中做自適應 anchor 設定**
4. ❌ 跳過步驟間的驗證門檻直接進入下一步
5. ❌ 未經使用者確認計劃就開始建構
6. ❌ 未分析設計輸入就開始建構
7. ❌ 不使用 `batch_execute` 逐個建立元素
8. ❌ Figma 模式忘記 Y 軸翻轉
9. ❌ 假設 Button 文字為 TMP
10. ❌ localScale 不為 (1,1,1) 而未修正
11. ❌ 跳過場景儲存
12. ❌ 直接修改場景中 Prefab 實例結構（應用 `open_prefab_contents`）
13. ❌ Prefab Edit Mode 中忘記 `save_prefab_contents`
14. ❌ 在 Canvas 下用 `update_gameobject` 建立 UI 物件（應用 `create_ui_element`）
15. ❌ **Figma 模式下跳過圖片下載步驟（Step 0）**

## 主動學習 (Active Learning)

- **操作前**：讀取 `doc/lessons/unity-mcp-lessons.md`，避免重蹈已知問題。
- **操作後**：判斷本次操作是否產生新經驗（踩坑、發現隱藏行為、確認可行做法、找到更好方法），若「是」→ 依 `unity-mcp-learning` 協議追加記錄；若「否」→ 不做任何事。
