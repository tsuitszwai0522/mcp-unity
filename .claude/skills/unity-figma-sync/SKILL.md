---
name: unity-figma-sync
description: Bidirectional sync between Unity UI and Figma. Push Unity UI to Figma, pull Figma changes back to Unity, iterate. Use when user wants to sync UI between Unity and Figma, or run design iteration loops.
---

# Unity-Figma Bidirectional Sync for Claude Code

雙向同步 Unity UGUI 與 Figma 設計，實現設計迭代循環。

> UI 首次建構使用 `unity-ui-builder`。UGUI 規則與 MCP 工具注意事項參考 `unity-mcp-workflow`。

## 前置條件

| MCP Server | 必要性 | 設定 |
|------------|--------|------|
| MCP Unity | 必備 | 已隨專案配置 |
| Figma Dev Mode MCP | 必備 | `claude mcp add --transport sse figma-dev-mode-mcp-server http://127.0.0.1:3845/sse` |
| Figma Remote MCP | Push 模式需要 | `generate_figma_design` 僅支援 remote server |

## 操作模式

| 模式 | 方向 | 觸發 |
|------|------|------|
| **Push** | Unity → Figma | 「推到 Figma」、「同步到 Figma」、「讓設計師看」 |
| **Pull** | Figma → Unity | 「讀取 Figma 修改」、「更新 Unity」、「Figma 有改」 |
| **Loop** | 雙向循環 | 「設計迭代」、「UI 迭代循環」 |

## Mode A：Unity → Figma Push

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

1. **讀取 Figma**：使用者選取修改後 frame → `get_design_context` + `get_variable_defs` + `get_screenshot`。
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

## Mode C：Loop（迭代循環）

1. 初次建構 → `unity-ui-builder`
2. Push → Mode A
3. 設計師修改 Figma
4. Pull → Mode B
5. 重複 2-4 直到雙方滿意

**注意**：每次 Pull 後再 Push，應重新生成 HTML preview。結構性變更累積後建議重整 Prefab。

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
