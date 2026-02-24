---
name: unity-ui-builder
description: Build Unity UGUI from Figma designs using MCP Unity tools. Use when user provides a Figma design and wants to recreate it in Unity, or asks to build UI with MCP tools.
---

# Unity UI Builder for Claude Code

此規則為 Claude Code 透過 MCP Unity 工具將 Figma 設計稿建構為 Unity UGUI 的行為規範。

> 通用 UGUI 規則、Layout Group 演算法、ScrollRect 結構、Prefab 操作、完整 MCP 注意事項請參考 `unity-mcp-workflow`。

## Figma MCP 資料來源策略

支援兩組 Figma MCP 工具，可獨立或搭配使用：

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

## 核心規則 (Core Rules)

1. **座標 1:1 映射**：Figma 像素座標直接對應 Unity anchoredPosition（Y 軸取負）。
2. **計劃先行**：必須先完成建構計劃（階層樹 + 屬性表格），**呈現給使用者確認後**才能呼叫任何 MCP 建構工具。
3. **Layout Group 強制分析**：對每個擁有 ≥2 個同類子元素的父節點，優先看 Figma Auto Layout（`layoutMode`）直接對應；若無，用座標規律演算法推斷（詳見 `unity-mcp-workflow`）。
4. **ScrollRect 判斷**：Layout Group 子元素總尺寸超過容器時，包一層 ScrollRect（結構詳見 `unity-mcp-workflow`）。若 Figma 有 Scrollbar UI，在 ScrollRect 下（與 Viewport 同層）加入 `Scrollbar`，並將 ScrollRect 的 `verticalScrollbar`/`horizontalScrollbar` 指向它。
5. **批次優先**：使用 `batch_execute` 批次建立相關元素，單次上限 100 個操作。
6. **由外而內**：建構順序 Canvas → 容器 → 區塊 → 子元素。

## 觸發條件 (When to Activate)

- 使用者提供 Figma 連結或設計稿，要求建構 Unity UI
- 使用者說「Figma 轉 Unity」、「用 MCP 建 UI」
- 使用者說「在 Unity 裡重現這個設計」

## 執行流程 (Workflow)

### 第一階段：Figma 分析

1. **取得設計資料**：用 `get_figma_data` 取得節點佈局。資料過大時改用 `get_metadata`（sparse XML）取得概覽。
1b. **（Dev Mode MCP）語義化上下文**：使用者選取目標 frame → 用 `get_design_context` 取得樣式化佈局資訊，作為原始數據的語義補充。
1c. **（Dev Mode MCP）Design Token**：用 `get_variable_defs` 取得設計系統 Variables/Styles → 直接作為色彩表/字型表的**權威來源**。若有 Figma Variables（如 `primary/500`），記錄 Token 名稱與實際值的對應。
1d. **（Dev Mode MCP）截圖參考**：用 `get_screenshot` 截取 frame，作為 layout fidelity 對照基準。
2. **下載圖片**：用 `download_figma_images` 下載所有圖片資源至 `Assets/Sprites/{DesignName}/`。
3. **分析結構**：識別可複用元件、建立色彩表（有 Design Token 時以 Token 為準）、字型表、階層樹。有 `get_design_context` 時用於交叉驗證結構判斷。
4. **Layout Group 分析（強制）**：優先用 Figma Auto Layout；無則計算子元素 gap 推斷。列出計算過程，結果標注在階層樹中。若子元素超出容器 → 標記 ScrollRect。

### 第 1.5 階段：Sprite 匯入

1. **批量設定 Sprite**：用 `batch_execute` + `import_texture_as_sprite` 將所有下載圖片設為 Sprite 類型（預設 `spriteMode: "Single"`, `meshType: "FullRect"`, `compression: "None"`）。
2. **建立 SpriteAtlas（可選）**：透過 `unity://packages` 確認 `com.unity.2d.sprite` 已安裝後，用 `create_sprite_atlas` 建立 SpriteAtlas。

### 第二階段：建構規劃（強制門檻）

1. **撰寫 Hierarchy Plan（階層樹）**：標註 elementType、anchorPreset、Layout Group、ScrollRect。
2. **輸出屬性表格**：每個元素的 Layout/ScrollRect/備註。
3. **確認 Prefab 策略**：標記重複元件，規劃 duplicate + update 流程。
4. **等待使用者確認**：獲得批准後才進入建構階段。

### 第三階段：Canvas 建構

1. **檢查 TestCanvas**：用 `ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 確認是否存在。
2. **建立 TestCanvas**（僅在不存在時）：`create_canvas(objectPath: "TestCanvas")`，ScreenSpaceOverlay，ScaleWithScreenSize，referenceResolution **1920×1080**，screenMatchMode **Expand**。注意：referenceResolution 固定 1920×1080，不可使用 Figma 畫面尺寸。
3. **View**：`TestCanvas/View`，stretch-fill 容器。
4. **設計框架**：middleCenter，尺寸對應 Figma 畫面。
5. **Container**：stretch-fill，CanvasGroup，背景色。

> 所有 UI 元素均建立在 `TestCanvas/View/` 之下。

### 第四階段：區塊建構

逐區塊建構，每個區塊使用 `batch_execute`：
- 全寬區塊：`topStretch` + 高度
- 絕對定位：`topLeft` + pivot (0,1) + Figma 座標（Y 取負）
- 右對齊：`topRight` + pivot (1,1) + 負 X offset
- **Sprite 指定**：用 `update_component` 將 Sprite 指定給 Image（`{"sprite": "Assets/Sprites/{DesignName}/image.png"}`）。

### 第五階段：可複用元件

Prefab 完整操作流程詳見 `unity-mcp-workflow`「Prefab 操作」。Figma 專案 Prefab 存放路徑：`Assets/Prefabs/{DesignName}/`。

### 第六階段：儲存

使用 `save_scene` 儲存場景。

## 快速參考（關鍵注意事項）

| 項目 | 規則 |
|------|------|
| Y 軸 | Figma Y 正值 → Unity anchoredPosition Y 負值 |
| Anchor 定位 | `topLeft` + pivot (0,1) 最常用，直接映射 Figma 座標 |
| Hex 轉 RGB | 每通道除以 255（如 #42 = 0x42/255 = 0.259） |
| TMP alpha | 建立 TMP 時 `color` 未指定 `a` 預設為 1（不透明），需半透明時才需明確帶 `a` |
| Button 文字 | 子物件名 `Text`，元件 `UnityEngine.UI.Text`，非 TMP |
| CanvasScaler | referenceResolution 固定 1920×1080 + Expand，不可用 Figma 畫面尺寸 |
| Viewport alpha | ScrollRect Viewport Image alpha 必須為 1 |
| localScale | 所有 UI 元素 localScale 保持 (1,1,1) |

> 完整注意事項（11 項）請參考 `unity-mcp-workflow`「MCP 工具注意事項」。

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認計劃就開始建構
2. ❌ 未分析 Figma 結構就開始建構
3. ❌ 不使用 `batch_execute` 逐個建立元素
4. ❌ 忘記 Y 軸翻轉
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
