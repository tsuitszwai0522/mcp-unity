---
name: unity-figma-sync
description: ALL Figma-related UI operations use this Skill. Includes extracting Design Spec from Figma URL (Spec mode), building/updating Unity UI from Figma (Pull mode), pushing Unity UI to Figma (Push mode), and bidirectional iteration loops. TRIGGER THIS SKILL whenever user provides a Figma URL or mentions Figma for UI work.
---

# Unity-Figma 設計整合 (Design Integration) for Claude Code

> **核心定位**：凡是涉及 Figma 的操作，都由此 Skill 處理。
> 包含：Design Spec 提取、Figma→Unity 建構/更新、Unity→Figma 推送、雙向迭代循環。
> 當使用者**提供 Figma URL**、**提到 Figma**、或**要求從設計稿建構 UI** 時，此 Skill 為首選。

> UI 建構規則（Layout Group、ScrollRect 等）參考 `unity-ui-builder`。通用 MCP 工具注意事項參考 `unity-mcp-workflow`。
> 首次從 Figma 建構 UI 時：先用此 Skill 提取 Design Spec，再交由 `unity-ui-builder` 執行建構。

## 前置條件

| MCP Server | 必要性 | 設定 |
|------------|--------|------|
| MCP Unity | 必備 | 已隨專案配置 |
| Figma MCP (Official) | 主要 | `claude mcp add --transport http figma https://mcp.figma.com/mcp` |
| Framelink MCP (Non-official) | 備用 | 已在 `.mcp.json`，需將 `YOUR_TOKEN_HERE` 替換為 Figma Personal Access Token |

> 官方 Remote MCP 統一處理讀取與推送，取代舊版 Dev Mode MCP 與 Remote MCP，不需要 Figma Desktop App。
> Framelink 作為備用，當官方 MCP 達到呼叫次數上限時自動切換。

## 操作模式

| 模式 | 方向 | 觸發 |
|------|------|------|
| **Spec** | Figma → JSON | 使用者貼 Figma URL、「看一下這個 Figma」、「提取設計規格」 |
| **Pull** | Figma → Unity | 「從 Figma 建 UI」、「Figma to Unity」、「更新 Unity」、「Figma 有改」 |
| **Push** | Unity → Figma | 「推到 Figma」、「同步到 Figma」、「讓設計師看」 |
| **Loop** | 雙向循環 | 「設計迭代」、「UI 迭代循環」 |

> **模式選擇**：使用者只貼 Figma URL 未說要建 UI → **Spec**；要建構/更新 Unity UI → **Pull**（Spec 為前置步驟）；推送至 Figma → **Push**；迭代 → **Loop**。

## Figma MCP Fallback 策略

1. **預設使用官方 MCP**（`mcp__figma__*` 工具）
2. **當官方 MCP 回傳 rate limit / 額度錯誤時**，自動切換到 Framelink（`mcp__framelink-figma__*` 工具）
3. 切換後在該 session 內持續使用 Framelink，不再嘗試官方 MCP

### 工具對照表

| 用途 | 官方工具 | Framelink 工具 |
|------|---------|---------------|
| 取得設計資料 | `get_design_context` + `get_metadata` + `get_variable_defs` | `get_figma_data` |
| 取得截圖/圖片 | `get_screenshot` | `download_figma_images` |
| 推送到 Figma | `generate_figma_design` | **不支援**（僅官方可用） |

> Framelink `get_figma_data` 回傳精簡 90% 的佈局/樣式資料（JSON/YAML），已包含 hierarchy、style、layout，一次呼叫即可取得，不需要分多次呼叫。

## Mode A：Unity → Figma Push

> ⚠️ Push 模式需要官方 Figma MCP（`generate_figma_design`），Framelink 不支援此功能。

1. **讀取 Unity UI**：`ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 取得場景結構 → `batch_execute` + `get_ui_element_info` 取得所有元素屬性。
2. **建立結構清單**：整理每個元素的 elementType、位置、尺寸、色彩、文字、Layout Group 設定。
3. **生成 HTML/CSS**：根據對應表生成等價 preview，存至 `Temp/FigmaSync/preview.html`。
4. **啟動本地伺服器**：`python3 -m http.server 8080`（或 `npx serve`），使用者瀏覽器開啟確認。
5. **推送**：`generate_figma_design` 將 localhost preview 推至 Figma。
6. **清理**：停止伺服器，保留 HTML 供後續比對。

### Unity → HTML/CSS 對應表

| Unity | CSS |
|-------|-----|
| Canvas 1920×1080 | `width:1920px; height:1080px; position:relative; overflow:hidden` |
| anchoredPosition (x, -y) | `position:absolute; left:{x}px; top:{abs(y)}px` |
| anchorPreset: stretch | `position:absolute; inset:0` |
| anchorPreset: topStretch | `position:absolute; top:{y}px; left:0; right:0; height:{h}px` |
| anchorPreset: topLeft + pivot(0,1) | `position:absolute; top:{abs(y)}px; left:{x}px` |
| anchorPreset: topRight + pivot(1,1) | `position:absolute; top:{abs(y)}px; right:{abs(x)}px` |
| sizeDelta (w, h) | `width:{w}px; height:{h}px` |
| Image color (r,g,b,a) 0-1 | `background:rgba({r*255},{g*255},{b*255},{a})` |
| Image sprite | `<img src="...">` 或 `background-image:url(...)` |
| TextMeshPro | `font-size:{fs}px; color:rgba({r*255},{g*255},{b*255},{a})` |
| TMP alignment MiddleLeft | `display:flex; align-items:center` |
| TMP alignment MiddleCenter | `display:flex; align-items:center; justify-content:center` |
| Button | `<button style="background:rgba(...)">` |
| HorizontalLayoutGroup | `display:flex; flex-direction:row; gap:{spacing}px` |
| VerticalLayoutGroup | `display:flex; flex-direction:column; gap:{spacing}px` |
| LayoutGroup padding (L,R,T,B) | `padding:{T}px {R}px {B}px {L}px` |
| ScrollRect vertical | `overflow-y:auto` |
| CanvasGroup alpha | `opacity:{alpha}` |
| RectMask2D | `overflow:hidden` |

**轉換注意**：Y 軸取絕對值（Unity `-56` → CSS `top:56px`）、色彩 0-1 → 0-255（alpha 保持 0-1）。

## Mode B：Figma → Unity Pull

1. **提取 Design Spec**：使用者提供 Figma URL → 提取 `fileKey` + `nodeId` → 依照「Design Spec 提取」雙軌流程整理成 JSON spec（主路線用官方 MCP，備用路線用 Framelink）。同時取得截圖作視覺參考（官方：`get_screenshot`；Framelink：`download_figma_images`）。
2. **讀取 Unity**：`batch_execute` + `get_ui_element_info` 取得對應元素當前狀態。
3. **差異分析**：建立對照表。

   | 元素 | 屬性 | Figma 值 | Unity 值 | 差異 |
   |------|------|---------|---------|------|
   | Header | bg | #0066FF | rgba(1,0,0,1) | 色彩 |
   | Item1 | height | 140px | sizeDelta.y=120 | 尺寸 |

4. **差異報告**（**強制門檻**）：呈現變更摘要 + 影響範圍 + 不支援的變更，**等待使用者確認**。
5. **增量更新**：`batch_execute` 套用變更。
6. **驗證**：`get_ui_element_info` 確認正確。
7. **儲存**：`save_scene`。

### 差異類型與對應操作

| 差異類型 | Unity 操作 |
|----------|-----------|
| 色彩 | `update_component` → Image/TMP `color` |
| 尺寸 | `set_rect_transform` → `sizeDelta` |
| 位置 | `set_rect_transform` → `anchoredPosition` |
| 文字 | `update_component` → TMP `text`/`fontSize`/`alignment` |
| 間距 | `update_component` → LayoutGroup `spacing`/`padding` |
| 新增元素 | `create_ui_element`（需額外確認） |
| 移除元素 | `delete_gameobject`（需額外確認） |

### Figma → Unity 值轉換

| Figma / CSS | Unity | 公式 |
|-------------|-------|------|
| `#RRGGBB` | Color {r,g,b,a} | 每通道 `/255` |
| `top:56px; left:24px` | anchoredPosition {x:24, y:-56} | Y 取負 |
| `gap:12px` | spacing: 12 | 直接對應 |
| `padding: T R B L` | padding {top,right,bottom,left} | 直接對應 |
| `opacity:0.5` | CanvasGroup alpha: 0.5 | 直接對應 |

## Design Spec 提取（Figma → JSON Spec）

> 獨立流程，可被 Mode B 和 `unity-ui-builder` 引用。依據 Fallback 策略選擇主路線或備用路線。

1. 從 Figma URL 提取 `fileKey` + `nodeId`

### 主路線（官方 MCP）

2. 平行呼叫：`get_design_context(nodeId, fileKey)` + `get_variable_defs(nodeId, fileKey)` + `get_metadata(nodeId, fileKey)`
3. 整理成 JSON spec，包含：
   - `hierarchy`：巢狀 UI 結構樹（name、type、layout、children）← `get_metadata`
   - `tokens.colors`：Design Token 色彩變數 ← `get_variable_defs`
   - `tokens.typography`：字型樣式清單（fontSize、fontWeight、color）← 參考代碼
   - `tokens.spacing`：間距變數（gap、padding）← `get_variable_defs` + 參考代碼
   - `elements`：扁平化元素清單（size、position、layout、style、text）← 參考代碼
   - `assets`：圖片下載 URL ← `get_design_context` 回傳

### 備用路線（Framelink MCP）

2. 呼叫 `get_figma_data(fileKey, nodeId)` — 一次取得所有佈局/樣式資料
3. 如需圖片素材，呼叫 `download_figma_images(fileKey, nodes, localPath)`
4. 將回傳的精簡資料整理成相同格式的 JSON spec（hierarchy、tokens、elements、assets）

## Mode C：Loop（迭代循環）

1. 初次建構 → `unity-ui-builder`
2. Push → Mode A
3. 設計師修改 Figma
4. Pull → Mode B
5. 重複 2-4 直到雙方滿意

**注意**：每次 Pull 後再 Push，應重新生成 HTML preview。結構性變更累積後建議重整 Prefab。

## 截圖視覺參考（強制規則）

> **在調整 UI 位置、尺寸、佈局時，必須以 Figma 截圖作為視覺參考，禁止僅憑座標數據盲算。**

### 何時取得截圖

| 時機 | 動作 |
|------|------|
| **提取 Design Spec 時** | `get_design_context` 已含截圖；若需更大範圍，額外呼叫 `get_screenshot` 取得父節點或全 UI 一覽 |
| **調整 UI 位置/尺寸前** | 必須先取得 Figma 截圖，確認元素間的相對位置與層級關係 |
| **套用變更後** | 用 `screenshot_game_view` 或 `screenshot_scene_view` 截取 Unity 畫面，與 Figma 截圖視覺比對 |

### 截圖使用原則

1. **先看圖再算數**：從截圖觀察元素的相對佈局（上下左右關係），再從座標數據計算精確位置
2. **取全覽截圖**：單一組件截圖不足以判斷整體佈局，應同時取得包含周圍元素的上層節點截圖
3. **迭代比對**：每次修改後重新截取 Unity 畫面，與 Figma 截圖並排比對，直到視覺一致

## 禁止事項

1. ❌ 未經使用者確認差異報告就執行更新
2. ❌ 整體重建 UI 而非增量更新
3. ❌ Unity → CSS 轉換忽略 Y 軸翻轉或色彩範圍（0-1 → 0-255）
4. ❌ HTML preview 使用不對應的 CSS 佈局
5. ❌ 修改 Prefab 實例結構而非 Prefab 資產
6. ❌ 跳過驗證步驟
7. ❌ 未停止本地伺服器就結束
8. ❌ 複用上一輪 HTML preview 進行新一輪 Push
9. ❌ 忽略不支援的變更而不告知使用者
10. ❌ **僅憑座標數據調整 UI 位置，未參考 Figma 截圖做視覺比對**
