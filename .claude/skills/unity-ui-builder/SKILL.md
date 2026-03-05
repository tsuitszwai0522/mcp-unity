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

1. **計劃先行**：必須先完成建構計劃（階層樹 + 屬性表格），**呈現給使用者確認後**才能呼叫任何 MCP 建構工具。
2. **Layout Group 強制分析**：對每個擁有 ≥2 個同類子元素的父節點執行判斷。Figma 模式優先看 Auto Layout（`layoutMode`）直接對應；否則用座標規律演算法推斷（x 相同 + w 相同 + gap 相等 → Vertical；y 相同 + gap 相等 → Horizontal；多行多列 → Grid；皆不符 → 絕對定位）。
3. **ScrollRect 判斷**：Layout Group 子元素總尺寸超過容器時，包一層 ScrollRect（結構：`ScrollRect+Image(a=0) → Viewport(RectMask2D, stretch-fill) → Content(LayoutGroup) → Children`）。用 `update_component` 接線 `content`/`viewport`。可選加 Scrollbar。
4. **批次優先**：使用 `batch_execute` 批次建立相關元素，單次上限 100 個操作。
5. **由外而內**：建構順序 Canvas → 容器 → 區塊 → 子元素 → Layout 組件。
6. **每步驗證**：每個區塊完成後用 `get_gameobject` 或 `get_ui_element_info` 確認。

## UGUI 建構規則

### Canvas 標準設定

`create_canvas`: ScreenSpaceOverlay, ScaleWithScreenSize, referenceResolution **1920×1080**, screenMatchMode **Expand**。標準階層：`TestCanvas → View(stretch-fill) → Container`。

### Anchor Preset 選用表

| 使用情境 | anchorPreset | pivot |
|----------|-------------|-------|
| 左上角絕對定位 | `topLeft` | (0, 1) |
| 水平填滿 | `topStretch` | (0.5, 1) |
| 填滿父層 | `stretch` | (0.5, 0.5) |
| 置中 | `middleCenter` | (0.5, 0.5) |
| 右對齊 | `topRight` | (1, 1) |
| 垂直置中靠左 | `middleLeft` | (0, 0.5) |

### 色彩轉換

Hex → Unity RGB (0-1)：每通道除以 255。`#426B1F` → `(0.259, 0.420, 0.122)`。

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

### 第一階段：設計輸入分析

根據輸入源分支：

**A. Figma 模式**（使用者提供 Figma 連結）：
1. `get_figma_data` 取得節點佈局。資料過大時改用 `get_metadata`（sparse XML）取得概覽。
1b.（Dev Mode MCP）`get_design_context` 取得語義化佈局資訊。
1c.（Dev Mode MCP）`get_variable_defs` 取得 Design Token → 色彩表/字型表的**權威來源**。
1d.（Dev Mode MCP）`get_screenshot` 截取 frame，作為 layout fidelity 基準。
2. `download_figma_images` 下載圖片至 `Assets/Sprites/{DesignName}/`。
3. 分析結構：識別可複用元件、建立色彩表/字型表/階層樹。
4. Layout Group 分析（強制）：優先用 Figma Auto Layout；無則計算子元素 gap 推斷。結果標注在階層樹中。若子元素超出容器 → 標記 ScrollRect。

**B. 描述模式**（使用者口頭描述）：
1. 從描述中提取 UI 結構需求。
2. 確認不明確的部分（元素數量、排列方式、色彩偏好、尺寸需求、是否滾動）。
3. 建立初步階層結構。
4. Layout Group 分析（強制）。

**C. 視覺參考模式**（截圖 / wireframe）：
1. 分析圖片中的 UI 元素與佈局。
2. 推斷色彩、尺寸、間距。
3. 建立階層結構。
4. Layout Group 分析（強制）。

**D. 規格模式**（結構化規格表）：
1. 直接解析規格中的元素定義。
2. 轉換為階層結構。
3. Layout Group 分析（強制）。

### 第 1.5 階段：Sprite 匯入（僅 Figma 模式）

1. 用 `batch_execute` + `import_texture_as_sprite` 將所有下載圖片設為 Sprite 類型（預設 `spriteMode: "Single"`, `meshType: "FullRect"`, `compression: "None"`）。
2. 透過 `unity://packages` 確認 `com.unity.2d.sprite` 已安裝後，用 `create_sprite_atlas` 建立 SpriteAtlas（可選）。

### 第二階段：建構規劃（強制門檻）

1. **撰寫 Hierarchy Plan（階層樹）**：標註 elementType、anchorPreset、Layout Group、ScrollRect。
2. **輸出屬性表格**：每個元素的 Layout/ScrollRect/備註。
3. **確認 Prefab 策略**：標記重複元件，規劃 duplicate + update 流程。
4. **等待使用者確認**：獲得批准後才進入建構階段。

### 第三階段：Canvas 建構

1. **檢查 TestCanvas**：用 `ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 確認是否存在。
2. **建立 TestCanvas**（僅在不存在時）：`create_canvas(objectPath: "TestCanvas")`，ScreenSpaceOverlay，ScaleWithScreenSize，referenceResolution **1920×1080**，screenMatchMode **Expand**。
3. **View**：`TestCanvas/View`，stretch-fill 容器。
4. **設計框架**：middleCenter，尺寸對應設計畫面。
5. **Container**：stretch-fill，CanvasGroup，背景色。

> 所有 UI 元素均建立在 `TestCanvas/View/` 之下。

### 第四階段：區塊建構

逐區塊建構，每個區塊使用 `batch_execute`：
- 全寬區塊：`topStretch` + 高度
- 絕對定位：`topLeft` + pivot (0,1) + 設計座標（Figma 模式 Y 取負）
- 右對齊：`topRight` + pivot (1,1) + 負 X offset
- **Sprite 指定**（有圖片時）：用 `update_component` 將 Sprite 指定給 Image。

### 第五階段：可複用元件

Prefab 完整操作流程詳見 `unity-mcp-workflow`「Prefab 操作」。Prefab 存放路徑：`Assets/Prefabs/{DesignName}/`。

### 第六階段：儲存

使用 `save_scene` 儲存場景。

## Figma 專屬參考

### Figma MCP 資料來源策略

| MCP 來源 | 工具 | 用途 |
|----------|------|------|
| **Plugin MCP**（必備） | `get_figma_data`, `download_figma_images` | 原始結構與圖片 |
| **Dev Mode MCP**（推薦） | `get_design_context`, `get_variable_defs`, `get_metadata`, `get_screenshot` | 語義上下文、Design Token、截圖 |

**Dev Mode MCP 設定**：Figma Desktop → Preferences → 啟用 "Dev Mode MCP Server" → `claude mcp add --transport sse figma-dev-mode-mcp-server http://127.0.0.1:3845/sse`。若未設定，跳過相關步驟即可。

**優先順序**（當 Dev Mode MCP 可用時）：
- 色彩/字型/間距 → `get_variable_defs`（Design Token，最權威）
- 佈局結構 → `get_figma_data` + `get_design_context`（語義補充）
- 大型設計 → `get_metadata`（sparse XML）→ 再用 `get_design_context` 深入特定區塊
- 視覺對照 → `get_screenshot` 作為 layout fidelity 基準

### Figma → Unity 座標對應

| Figma 屬性 | Unity 屬性 | 說明 |
|------------|-----------|------|
| X, Y (父層左上角) | anchoredPosition (x, -y) | Y 軸翻轉 |
| Width, Height | sizeDelta (w, h) | 直接對應 |
| 填滿父層 | `stretch`, sizeDelta (0, 0) | 四邊 offset 為 0 |
| 水平填滿 | `topStretch`, sizeDelta.y = h | NavBar/標題列 |

### Figma Layout Group 補充

Auto Layout 直接對應：`HORIZONTAL` → HorizontalLayoutGroup，`VERTICAL` → VerticalLayoutGroup。提取 `itemSpacing` → `spacing`、`padding`。無 Auto Layout 時 fallback 到座標規律演算法。

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認計劃就開始建構
2. ❌ 未分析設計輸入就開始建構
3. ❌ 不使用 `batch_execute` 逐個建立元素
4. ❌ Figma 模式忘記 Y 軸翻轉
5. ❌ 假設 Button 文字為 TMP
6. ❌ 規律排列子元素不使用 Layout Group
7. ❌ 跳過 Layout Group 分析，僅憑「感覺」判斷
8. ❌ ScrollRect 結構不按規範
9. ❌ localScale 不為 (1,1,1) 而未修正
10. ❌ 可複用元件只用 duplicate 而不建立 Prefab
11. ❌ 跳過場景儲存
12. ❌ 直接修改場景中 Prefab 實例結構（應用 `open_prefab_contents`）
13. ❌ Prefab Edit Mode 中忘記 `save_prefab_contents`
14. ❌ 在 Canvas 下用 `update_gameobject` 建立 UI 物件（應用 `create_ui_element`；工具會回傳警告）
15. ❌ 組件加錯後 `delete_gameobject` 重建整個 GO（應改用 `remove_component`）
16. ❌ 在沒有 Canvas 的情況下建立 UI 元素
17. ❌ 手動設定 localScale 為非 (1,1,1) 的值
18. ❌ 忽略 Canvas/RectTransform 警告訊息

## 主動學習 (Active Learning)

- **操作前**：讀取 `doc/lessons/unity-mcp-lessons.md`，避免重蹈已知問題。
- **操作後**：判斷本次操作是否產生新經驗（踩坑、發現隱藏行為、確認可行做法、找到更好方法），若「是」→ 依 `unity-mcp-learning` 協議追加記錄；若「否」→ 不做任何事。
