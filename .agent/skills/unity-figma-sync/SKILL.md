---
name: unity-figma-sync
description: 雙向同步 Unity UI 與 Figma 設計。支援將 Unity UI 推送至 Figma、從 Figma 讀取修改並增量更新 Unity，實現設計迭代循環。
---

# Unity-Figma 雙向同步 (Bidirectional Design Sync)

此 Skill 實現 Unity UGUI 與 Figma 之間的雙向同步迭代循環。

> **前置知識**：此 Skill 搭配 `unity-mcp-workflow` 和 `unity-ui-builder` 使用。
> - Unity UI 首次建構流程請參考 `unity-ui-builder`
> - UGUI 規則與 MCP 工具注意事項請參考 `unity-mcp-workflow`
> - Figma MCP 資料來源策略請參考 `unity-ui-builder`「Figma MCP 資料來源策略」

## 前置條件 (Prerequisites)

### 必要 MCP 連接

| MCP Server | 必要性 | 用途 | 設定方式 |
|------------|--------|------|----------|
| **MCP Unity** | 必備 | 讀取/修改 Unity UI | 已隨專案配置 |
| **Figma Dev Mode MCP** | 必備 | 讀取 Figma 設計上下文、Design Token | Figma Desktop → Preferences → "Dev Mode MCP Server" |
| **Figma Remote MCP** | Push 模式需要 | `generate_figma_design` 推送至 Figma | 需 Figma 帳號授權 Remote Server |

### 連接指令

```bash
# Dev Mode MCP（本機，Pull 模式必備）
claude mcp add --transport sse figma-dev-mode-mcp-server http://127.0.0.1:3845/sse

# Remote MCP（Push 模式需要，generate_figma_design 僅支援 remote server）
# 請參考 Figma Developer Docs 設定 Remote MCP Server
```

## 操作模式

本 Skill 支援三種操作模式：

| 模式 | 方向 | 觸發條件 | 前置 |
|------|------|----------|------|
| **Push** | Unity → Figma | 「把 Unity UI 推到 Figma」、「同步到 Figma」 | 場景中已有 Unity UI |
| **Pull** | Figma → Unity | 「從 Figma 讀取修改」、「更新 Unity UI」 | Figma 上已有修改過的設計 |
| **Loop** | 雙向循環 | 「設計迭代」、「UI 迭代循環」 | 兩端都已有 UI |

---

## Mode A：Unity → Figma 推送

### 第一步：讀取 Unity UI 階層

1. **取得場景階層**：
   - 使用 `ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 取得完整場景結構。
   - 確認目標 Canvas 路徑（如 `TestCanvas/View/{DesignName}`）。

2. **批次取得 UI 元素資訊**：
   - 使用 `batch_execute` + `get_ui_element_info` 批次查詢所有 UI 元素：
   ```json
   {
     "operations": [
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/{DesignName}/Container"}},
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/{DesignName}/Container/Header"}},
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/{DesignName}/Container/Header/Title"}}
     ]
   }
   ```
   - 記錄每個元素的：elementType、anchoredPosition、sizeDelta、anchorPreset、色彩、文字內容、Layout Group 設定。

3. **建立 UI 結構清單**：
   ```
   Container (Panel, stretch, bg: #FFFFFF)
   ├── Header (Panel, topStretch, h:56, HLG spacing:8)
   │   ├── Title (TMP, "標題", 20px, #000000)
   │   └── CloseBtn (Button, 48×48, bg:#FF4444)
   └── Content (Panel, VLG spacing:12)
       ├── Item1 (Panel, h:120)
       └── Item2 (Panel, h:120)
   ```

### 第二步：生成 HTML/CSS Preview

1. **建立 HTML 檔案**：
   - 儲存至 `Temp/FigmaSync/preview.html`。
   - 使用下方「Unity UGUI → HTML/CSS 對應表」轉換。
   - 包含所有可用的 Sprite 圖片（以相對路徑引用或內嵌 base64）。

2. **Unity UGUI → HTML/CSS 對應表**：

   | Unity 概念 | HTML/CSS 等價 |
   |-----------|--------------|
   | Canvas (1920×1080) | `<div style="width:1920px; height:1080px; position:relative; overflow:hidden">` |
   | RectTransform anchoredPosition (x, -y) | `position:absolute; left:{x}px; top:{abs(y)}px` |
   | anchorPreset: stretch | `position:absolute; inset:0` |
   | anchorPreset: topStretch | `position:absolute; top:{y}px; left:0; right:0; height:{h}px` |
   | anchorPreset: topLeft + pivot(0,1) | `position:absolute; top:{abs(y)}px; left:{x}px` |
   | anchorPreset: topRight + pivot(1,1) | `position:absolute; top:{abs(y)}px; right:{abs(x)}px` |
   | sizeDelta (w, h) | `width:{w}px; height:{h}px` |
   | Image (solid color) | `background-color: rgba({r*255},{g*255},{b*255},{a})` |
   | Image (sprite) | `<img src="...">` 或 `background-image: url(...)` |
   | TextMeshPro | `<div style="font-size:{fs}px; color:rgba({r*255},{g*255},{b*255},{a}); font-family:sans-serif">` |
   | TMP alignment: MiddleLeft | `display:flex; align-items:center; justify-content:flex-start` |
   | TMP alignment: MiddleCenter | `display:flex; align-items:center; justify-content:center` |
   | Button | `<button style="background:rgba(...)">` |
   | HorizontalLayoutGroup | `display:flex; flex-direction:row; gap:{spacing}px` |
   | VerticalLayoutGroup | `display:flex; flex-direction:column; gap:{spacing}px` |
   | LayoutGroup padding (L,R,T,B) | `padding:{T}px {R}px {B}px {L}px` |
   | LayoutGroup childAlignment | 對應 `justify-content` + `align-items` |
   | ScrollRect (vertical) | `overflow-y:auto` |
   | ScrollRect (horizontal) | `overflow-x:auto` |
   | CanvasGroup alpha | `opacity:{alpha}` |
   | RectMask2D | `overflow:hidden` |

   **座標轉換注意**：
   - Unity anchoredPosition Y 為負值 → CSS `top` 取絕對值（`top = abs(y)`）
   - Unity color 為 0-1 float → CSS rgba 的 R/G/B 為 0-255（`css = unity * 255`），alpha 保持 0-1
   - Unity pivot (0,1) 代表左上角定位，與 CSS `top`/`left` 直接對應

3. **HTML 範本結構**：
   ```html
   <!DOCTYPE html>
   <html>
   <head>
     <meta charset="utf-8">
     <title>{DesignName} - Unity UI Preview</title>
     <style>
       * { margin:0; padding:0; box-sizing:border-box; }
       body { display:flex; justify-content:center; align-items:center;
              min-height:100vh; background:#888; }
     </style>
   </head>
   <body>
     <div style="width:1920px; height:1080px; position:relative; overflow:hidden; background:#fff;">
       <!-- 按 Unity 階層結構生成巢狀 div -->
     </div>
   </body>
   </html>
   ```

### 第三步：啟動預覽伺服器

```bash
# 方案一：Python
cd Temp/FigmaSync && python3 -m http.server 8080

# 方案二：Node.js
npx serve Temp/FigmaSync -l 8080
```

使用者在瀏覽器中開啟 `http://localhost:8080/preview.html` 確認 preview 正確。

### 第四步：推送至 Figma

1. **使用 `generate_figma_design`**：
   - 將 localhost 上的 HTML preview 推送至 Figma canvas。
   - 支援推送至：新檔案、既有檔案、或剪貼簿。

2. **清理**：
   - 停止本地伺服器（Ctrl+C 或 kill process）。
   - 保留 HTML 檔案供後續迭代比對使用。

---

## Mode B：Figma → Unity 迭代拉取

> 此模式用於設計師在 Figma 上修改後，將變更增量套用回 Unity。

### 第一步：讀取 Figma 修改

1. **使用者在 Figma Desktop 選取修改後的 frame**。

2. **取得修改後的設計資訊**：
   - `get_design_context` → 完整樣式/佈局資訊
   - `get_variable_defs` → 更新後的 Design Token
   - `get_screenshot` → 修改後的畫面截圖作為視覺參考

3. **記錄 Figma 修改清單**：
   - 色彩變更（如 Header 背景 `#FF0000` → `#0066FF`）
   - 尺寸變更（如 Item 高度 `120px` → `140px`）
   - 文字變更（內容、字號、對齊方式）
   - 間距變更（Layout Group spacing、padding）
   - 結構變更（新增/移除/重排元素）

### 第二步：讀取 Unity 當前狀態

1. **批次查詢對應的 Unity UI 元素**：
   ```json
   {
     "operations": [
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/.../Header"}},
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/.../Content"}},
       {"tool": "get_ui_element_info", "params": {"objectPath": "TestCanvas/View/.../Title"}}
     ]
   }
   ```

2. **建立對照表**：

   | 元素 | 屬性 | Figma 修改值 | Unity 當前值 | 差異類型 |
   |------|------|-------------|-------------|----------|
   | Header | background | #0066FF | rgba(1,0,0,1) | 色彩變更 |
   | Item1 | height | 140px | sizeDelta.y=120 | 尺寸變更 |
   | Title | text | "新標題" | "舊標題" | 文字變更 |
   | Content | spacing | 16px | 12 | 間距變更 |

### 第三步：呈現差異報告

> **強制門檻**：差異報告必須呈現給使用者，經確認後才能執行更新。

向使用者呈現：

1. **變更摘要**：列出所有偵測到的差異（含具體數值）
2. **影響範圍**：哪些 Unity 物件會被修改（含完整 objectPath）
3. **不支援的變更**：標記無法自動處理的項目
   - 結構性變更（新增/移除元素）需額外確認
   - 新字型需手動匯入
   - 新圖片需先下載匯入

4. **等待使用者確認**：獲得批准後才執行更新。

### 第四步：套用增量更新

使用 `batch_execute` 批次套用所有已確認的變更：

1. **色彩/樣式更新**：
   ```json
   {"tool": "update_component", "params": {
     "objectPath": ".../Header",
     "componentName": "Image",
     "componentData": {"color": {"r": 0, "g": 0.4, "b": 1, "a": 1}}
   }}
   ```

2. **尺寸更新**：
   ```json
   {"tool": "set_rect_transform", "params": {
     "objectPath": ".../Item1",
     "sizeDelta": {"x": 300, "y": 140}
   }}
   ```

3. **文字更新**：
   ```json
   {"tool": "update_component", "params": {
     "objectPath": ".../Title",
     "componentName": "TMPro.TextMeshProUGUI",
     "componentData": {"text": "新標題", "fontSize": 24}
   }}
   ```

4. **Layout Group 更新**：
   ```json
   {"tool": "update_component", "params": {
     "objectPath": ".../Content",
     "componentName": "VerticalLayoutGroup",
     "componentData": {"spacing": 16}
   }}
   ```

5. **結構性變更**（若使用者已確認）：
   - 新增元素：使用 `create_ui_element`
   - 移除元素：使用 `delete_gameobject`
   - 重排元素：使用 `reparent_gameobject` 或 `move_gameobject`

### 第五步：驗證與儲存

1. **驗證更新**：使用 `get_ui_element_info` 逐一確認變更已正確套用。
2. **截圖對比**：比較 `get_screenshot` 的 Figma 截圖與更新後的 Unity UI。
3. **儲存場景**：`save_scene`。

---

## Mode C：完整迭代循環

結合 Mode A 和 Mode B 的端到端流程：

```
1. 初次建構 → unity-ui-builder（Figma → Unity）
       ↓
2. Push → Mode A（Unity → Figma，供設計師審閱）
       ↓
3. 設計師在 Figma 標註/修改
       ↓
4. Pull → Mode B（Figma → Unity，增量更新）
       ↓
5. 重複 2-4 直到雙方滿意
       ↓
6. 最終儲存 + Prefab 整理
```

### 迭代注意事項

- 每次 Pull 更新後，若需再次 Push，應**重新生成 HTML preview**（不要複用上一輪的）。
- 結構性變更（新增/移除元素）累積後，建議重新整理 Prefab（`open_prefab_contents` → 修改 → `save_prefab_contents`）。
- 保留每次差異報告的紀錄，方便追蹤迭代歷史。

---

## Figma → Unity 值轉換速查

| 來源（Figma / CSS） | 目標（Unity） | 轉換公式 |
|---------------------|-------------|----------|
| Hex `#RRGGBB` | Color `{r, g, b, a}` | 每通道 `/255`，如 `#42` → `0x42/255 = 0.259` |
| `font-size: 20px` | `fontSize: 20` | 直接對應 |
| `gap: 12px` | `spacing: 12` | 直接對應 |
| `padding: T R B L` | `padding {top, right, bottom, left}` | 直接對應 |
| `top: 56px; left: 24px` | `anchoredPosition: {x:24, y:-56}` | Y 軸取負 |
| `width:300px; height:140px` | `sizeDelta: {x:300, y:140}` | 直接對應 |
| `opacity: 0.5` | CanvasGroup `alpha: 0.5` | 直接對應 |

## 建構 Checklist

### Mode A：Unity → Figma
- [ ] Unity UI 階層已完整讀取（`get_scene_info` + `get_ui_element_info`）
- [ ] UI 結構清單已建立（含所有元素屬性）
- [ ] HTML/CSS preview 已生成
- [ ] Unity → CSS 座標/色彩/佈局轉換正確
- [ ] Preview 可在瀏覽器中正確顯示
- [ ] 本地伺服器已啟動
- [ ] `generate_figma_design` 已成功推送至 Figma
- [ ] 本地伺服器已停止

### Mode B：Figma → Unity
- [ ] Figma 修改已透過 `get_design_context` + `get_variable_defs` 讀取
- [ ] Unity 當前狀態已透過 `get_ui_element_info` 讀取
- [ ] 差異對照表已建立
- [ ] 差異報告已呈現給使用者
- [ ] 使用者已確認/批准更新清單
- [ ] 增量更新已透過 `batch_execute` 套用
- [ ] 更新結果已驗證
- [ ] 場景已儲存

---

## 觸發時機 (When to use)

- 使用者說：「把 Unity UI 推到 Figma」
- 使用者說：「同步到 Figma」、「讓設計師看」
- 使用者說：「從 Figma 讀取修改」、「更新 Unity UI」
- 使用者說：「設計迭代」、「UI 迭代循環」
- 使用者說：「Figma 有改，幫我同步」

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認差異報告就執行更新
2. ❌ 整體重建 UI 而非增量更新（除非結構性變更無法避免且使用者同意）
3. ❌ Unity → CSS 轉換忽略 Y 軸翻轉（Unity Y 負 → CSS top 正）
4. ❌ Unity → CSS 色彩轉換忽略範圍差異（0-1 → 0-255）
5. ❌ 在 HTML preview 中使用不對應的 CSS 佈局（如用 grid 模擬 flex layout）
6. ❌ 修改 Prefab 實例的結構而非 Prefab 資產（結構性變更應用 `open_prefab_contents`）
7. ❌ 跳過驗證步驟直接儲存
8. ❌ 未停止本地伺服器就結束流程
9. ❌ 複用上一輪的 HTML preview 進行新一輪 Push（應重新生成）
10. ❌ 忽略不支援的變更而不告知使用者（如新字型、新圖片需手動處理）

## 手動後續步驟 (Post-Implementation)

以下操作目前無法全自動完成，需使用者配合：

1. **新字型**：Figma 修改中若引入新字型，需手動匯入並建立 TMP Font Asset。
2. **新圖片**：Figma 新增的圖片需先用 `download_figma_images` 下載 + `import_texture_as_sprite` 匯入。
3. **複雜結構變更**：大規模的元素新增/移除/重組可能需改用 `unity-ui-builder` 重新建構該區塊。
4. **動畫/互動**：Figma 的 prototype 互動與動畫無法自動轉換至 Unity。
