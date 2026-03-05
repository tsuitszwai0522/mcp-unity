---
name: unity-ui-builder
description: 透過 MCP Unity 工具建構 Unity UGUI。支援多種設計輸入：Figma 設計稿、文字描述、wireframe、截圖等。
---

# Unity UI Builder

此 Skill 用於規範 Unity UGUI 的完整建構流程。透過 MCP Unity 工具組，將各種設計輸入（Figma 設計稿、文字描述、wireframe、截圖、規格表）轉化為 Unity 場景中的 UI 階層。

> **前置知識**：此 Skill 搭配 `unity-mcp-workflow` 使用。以下通用知識請參考該 Skill：
> - Prefab 操作（新建與修改既有 Prefab 的完整流程）
> - 批次操作（`batch_execute`）、路徑格式、錯誤處理

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 使用者提供 Figma 連結要求建構 UI（→ Figma 模式）
- 使用者口頭描述想要的 UI（→ 描述模式）
- 使用者提供 wireframe / 截圖（→ 視覺參考模式）
- 使用者提供 UI 規格表（→ 規格模式）
- 使用者說「建 UI」、「做一個介面」、「建構 UI」
- 使用者說「用 MCP 建立 UI」、「在 Unity 裡建一個選單」
- 使用者說「Figma 轉 Unity」、「把這個設計做成 Unity UI」

## 核心規則 (Core Rules)

1. **計劃先行 (Plan First)**：必須先完成完整的建構計劃（含 Hierarchy Plan 階層樹 + 屬性表格），**呈現給使用者確認後**才能開始呼叫任何 MCP 建構工具。
2. **Layout Group 強制分析**：對每個擁有 ≥2 個同類子元素的父節點，執行 Layout Group 判斷流程（詳見「Layout Group 判斷規則」）。
3. **ScrollRect 判斷**：當使用 Layout Group 且子元素總尺寸超過容器可視範圍時，必須包一層 ScrollRect（詳見「ScrollRect 結構規範」）。
4. **批次優先 (Batch First)**：相關聯的 UI 元素必須使用 `batch_execute` 一次建立（詳見 `unity-mcp-workflow`）。
5. **由外而內 (Outside-In)**：建構順序為 Canvas → 容器 → 區塊 → 子元素 → Layout 組件，確保父物件存在後才建立子物件。
6. **每步驗證**：每個主要區塊完成後，使用 `get_gameobject` 或 `get_ui_element_info` 確認結構正確。

## UGUI 建構規則 (UGUI Build Rules)

### Canvas 標準設定

建立 UI 時使用以下固定設定：

```
create_canvas:
  objectPath: "TestCanvas"
  renderMode: ScreenSpaceOverlay
  scaler:
    uiScaleMode: ScaleWithScreenSize
    referenceResolution: {x: 1920, y: 1080}  # 固定值
    screenMatchMode: Expand
```

標準階層結構：

```
TestCanvas                    (Overlay, ScaleWithScreenSize, 1920×1080, Expand)
  └── View                    (stretch-fill, 透明背景)
      └── Container           (stretch-fill 或 middleCenter + 指定尺寸)
```

### Anchor Preset 選用表

| 使用情境 | anchorPreset | pivot |
|----------|-------------|-------|
| 從父層左上角絕對定位 | `topLeft` | (0, 1) |
| 水平填滿的區塊（NavBar、標題列） | `topStretch` | (0.5, 1) |
| 填滿父層（容器） | `stretch` | (0.5, 0.5) |
| 置中元素 | `middleCenter` | (0.5, 0.5) |
| 右對齊元素 | `topRight` | (1, 1) |
| 垂直置中靠左 | `middleLeft` | (0, 0.5) |

### 色彩轉換

Hex → Unity RGB (0-1)：每個通道值除以 255。

```
#426B1F → (0x42/255, 0x6B/255, 0x1F/255) = (0.259, 0.420, 0.122)
#FAFAF5 → (0.980, 0.980, 0.957)
#E6E6E6 → (0.902, 0.902, 0.902)
```

### Layout Group 判斷規則

> **強制要求**：對**每個擁有 ≥2 個同類子元素的父節點**執行以下判斷流程。

**判斷順序**：

1. **優先看 Auto Layout 屬性**（僅 Figma 模式）：若節點有 `layoutMode: "HORIZONTAL"` 或 `"VERTICAL"`（如 Figma Auto Layout），直接對應 Layout Group，並提取 `itemSpacing`、`padding` 等屬性。

2. **座標規律演算法**（Figma 無 Auto Layout 時，或非 Figma 輸入源時使用）：

   對每個擁有 ≥2 個同類型子元素的父節點，取所有子元素的 `(x, y, w, h)`，依序計算：

   **步驟 A — 檢查垂直排列（VerticalLayoutGroup）**：
   ```
   若所有子元素 x 相同（或差距 ≤2px）且 w 相同：
     計算 gap = y[i+1] - y[i] - h[i]（對每對相鄰元素）
     若所有 gap 相等 → VerticalLayoutGroup, spacing = gap
   ```

   **步驟 B — 檢查水平排列（HorizontalLayoutGroup）**：
   ```
   若所有子元素 y 相同（或差距 ≤2px）：
     計算 gap = x[i+1] - x[i] - w[i]（對每對相鄰元素）
     若所有 gap 相等 → HorizontalLayoutGroup, spacing = gap
   ```

   **步驟 C — 檢查網格排列（GridLayoutGroup）**：
   ```
   若子元素呈多行多列規律排列（行內 y 相同、列內 x 相同）
     → GridLayoutGroup, cellSize = (w, h), spacing = (gapX, gapY)
   ```

   **步驟 D — 無規律**：以上皆不符 → 絕對定位，不使用 Layout Group。

   **示例**：
   ```
   子元素座標：
     Item1: x=96, y=301, w=821, h=159
     Item2: x=96, y=491, w=821, h=159
     Item3: x=96, y=681, w=821, h=159

   步驟 A：x 全為 96 ✓, w 全為 821 ✓
     gap1 = 491 - 301 - 159 = 31
     gap2 = 681 - 491 - 159 = 31
     gap 相等 → VerticalLayoutGroup, spacing=31
   ```

### ScrollRect 結構規範

當判定使用 Layout Group 且子元素總尺寸**超過**容器可視範圍時，必須使用以下固定結構：

```
{ScrollArea}              (ScrollRect 元件 + Image 元件，無背景圖時 alpha=0)
  ├── Viewport            (RectMask2D 元件，stretch-fill)
  │   └── Content         (Layout Group: Horizontal/Vertical/Grid)
  │       ├── Child1
  │       ├── Child2
  │       └── ...
  └── Scrollbar           (可選，僅當設計中有 Scrollbar 時加入)
```

**建構步驟**：
1. 建立外層 Panel 作為 ScrollRect 容器，加入 `ScrollRect` 元件與 `Image` 元件（無背景圖時設 `color.a = 0`）。
2. 建立子物件 `Viewport`，加入 `RectMask2D` 元件，設為 stretch-fill。
3. 建立子物件 `Content`，加入對應的 Layout Group。
4. **Scrollbar（可選）**：若設計中存在 Scrollbar，在 ScrollRect 下（與 Viewport 同層）建立 `Scrollbar`：
   - 使用 `create_ui_element(elementType: "Scrollbar")`，設定 `direction`（垂直用 `BottomToTop`，水平用 `LeftToRight`）。
   - 垂直 Scrollbar：anchor `middleRight`，寬度對應設計。
   - 水平 Scrollbar：anchor `bottomStretch`，高度對應設計。
5. 使用 `update_component` 將 ScrollRect 的 `content` 指向 Content、`viewport` 指向 Viewport。若有 Scrollbar，將 `verticalScrollbar` 或 `horizontalScrollbar` 指向對應物件。
6. 在 Content 下建立子元素。

**不需要 ScrollRect 的情況**：子元素總尺寸未超過容器 → 僅使用 Layout Group，不包 ScrollRect。

## MCP 工具注意事項 — UI 專屬 (UI-Specific MCP Pitfalls)

| 陷阱 | 說明 |
|------|------|
| CanvasScaler | referenceResolution 固定 **1920×1080** + screenMatchMode **Expand**，不可使用設計畫面尺寸作為 referenceResolution |
| Button 文字子物件 | `create_ui_element` 建立 Button 時，文字子物件名稱為 `Text`（非 `Text (TMP)`），使用 legacy `UnityEngine.UI.Text` 元件 |
| Button 背景色 | `elementData.color` 設定的是 Button 的 Image 背景色，文字顏色需另外透過 `update_component` 修改 `Text` 子物件 |
| TMP 元件名 | 使用 `TMPro.TextMeshProUGUI` 作為 componentName 來更新 TextMeshPro 屬性 |
| TMP 顏色 alpha | `create_ui_element` 建立 TextMeshPro 時，`elementData.color` 未指定 `a` 時預設為 1（不透明）。若需半透明文字，需明確帶 `a` 值 |
| ScrollRect Viewport alpha | Viewport 的 Image alpha 必須為 **1**（不可為 0），Mask 需要 stencil buffer 正常寫入才能顯示子元素。`showMaskGraphic: false` 會隱藏 Image 本身 |
| localScale | 所有 UI 元素的 localScale 必須保持 **(1,1,1)**，不可因 CanvasScaler 變更或其他操作而偏移 |
| UI 物件建議用 create_ui_element | 在 Canvas 下用 `update_gameobject`（objectPath 指向不存在路徑）建立的 GO 會自動加上 RectTransform，但不含 CanvasRenderer 和其他 UI 元件。完整 UI 物件（Button、Text 等）仍建議用 `create_ui_element` 建立 |

> 通用 MCP 工具注意事項（Prefab 工作流、Asset Reference、Scene 物件引用、Material 等）請參考 `unity-mcp-workflow`。

## 執行流程 (Workflow)

### 第一階段：設計輸入分析 (Design Input Analysis)

根據使用者提供的輸入源，選擇對應的分析方式：

#### A. Figma 模式（使用者提供 Figma 連結）

1. **取得設計資料**：
   - 使用 `get_figma_data` 取得指定節點的完整佈局資料。
   - 參數：`fileKey`（從 Figma URL 擷取）、`nodeId`（目標畫面節點）。
   - 若 `get_figma_data` 回傳資料量過大（超出 context 上限），可改用 `get_metadata` 取得 sparse XML 概覽，再對特定區塊逐一用 `get_design_context` 深入分析。

1b. **（Dev Mode MCP）取得語義化設計上下文**：
   - 使用者在 Figma Desktop 選取目標 frame（或提供 frame 連結給 Remote Server）。
   - 使用 `get_design_context` 取得樣式化佈局資訊（預設輸出 React+Tailwind 格式，但其中的 CSS 值可直接用於 Unity 建構）。
   - 此資料包含精確的色彩值、字型設定、間距，作為 `get_figma_data` 原始數據的**語義化補充**。

1c. **（Dev Mode MCP）擷取 Design Token**：
   - 使用 `get_variable_defs` 取得設計系統的 Variables 與 Styles。
   - 回傳的色彩、間距、字型定義直接作為色彩表和字型表的**權威來源**。
   - 若設計使用 Figma Variables（如 `primary/500`、`spacing/lg`），記錄 Token 名稱與實際值的對應。

1d. **（Dev Mode MCP）截圖視覺參考**：
   - 使用 `get_screenshot` 截取選取的 frame。
   - 作為建構完成後對比 layout fidelity 的基準圖。

2. **擷取圖片資源**：
   - 使用 `download_figma_images` 下載所有圖片與圖示。
   - 點陣圖（PNG）需包含 `imageRef`。
   - 向量圖（SVG）僅需 `nodeId` 與 `fileName`。
   - 儲存至 `Assets/Sprites/{DesignName}/` 目錄。

3. **分析設計結構**：
   - 識別 **可複用元件**（出現 2 次以上的相同結構）→ 標記為 Prefab 候選。
   - 記錄色彩表（Hex → Unity RGB）。若有 `get_variable_defs` 的 Design Token，以 Token 值為權威來源。
   - 記錄字型表（字體、字號、粗細）。若 Design Token 包含 typography 定義，以其為準。
   - 建立完整的 UI 階層樹。若有 `get_design_context` 的語義資訊，用於交叉驗證結構判斷。

4. **Layout Group 分析（強制）**：
   - 優先檢查 Figma Auto Layout 屬性（`layoutMode`），直接對應 Layout Group。
   - 無 Auto Layout 時，對每個擁有 ≥2 個同類子元素的父節點，執行座標規律演算法。
   - 列出分析過程（取 x/y/w/h → 計算 gap → 判定結果），確保可驗證。
   - 將判定結果（Layout 類型 + spacing）標注在階層樹對應節點上。
   - 判定使用 Layout Group 後，評估子元素總尺寸是否超過容器可視範圍，若超過則標記需要 ScrollRect。

#### B. 描述模式（使用者口頭描述）

1. **提取 UI 結構需求**：從使用者描述中識別所需的 UI 元素類型、佈局方式、功能需求。
2. **確認不明確的部分**：主動詢問使用者以下未明確的細節：
   - 元素數量（如「幾個按鈕？」）
   - 排列方式（水平/垂直/網格）
   - 色彩偏好（是否有品牌色？）
   - 尺寸需求（全屏/彈窗/固定尺寸）
   - 是否需要滾動（內容量是否超出畫面）
3. **建立初步階層結構**：根據確認後的需求，建立 UI 階層樹。
4. **Layout Group 分析（強制）**：根據描述中的排列需求判斷 Layout Group 類型。

#### C. 視覺參考模式（截圖 / wireframe）

1. **分析圖片中的 UI 元素與佈局**：識別所有可見的 UI 元素（按鈕、文字、圖片、輸入框等）。
2. **推斷色彩、尺寸、間距**：從視覺參考中估算各元素的屬性。
3. **建立階層結構**：將分析結果轉化為 UI 階層樹。
4. **Layout Group 分析（強制）**：根據視覺排列判斷 Layout Group 類型與 spacing。

#### D. 規格模式（結構化規格表）

1. **直接解析規格中的元素定義**：讀取規格表中的元素類型、屬性、階層關係。
2. **轉換為階層結構**：將規格直接映射為 UI 階層樹。
3. **Layout Group 分析（強制）**：根據規格中的排列定義判斷 Layout Group 類型。

### 第 1.5 階段：Sprite 匯入（僅 Figma 模式）

1. **批量設定 Sprite Import Settings**：
   - 使用 `batch_execute` + `import_texture_as_sprite` 將所有下載的圖片設定為 Sprite 類型：
   ```json
   {
     "operations": [
       {
         "tool": "import_texture_as_sprite",
         "params": { "assetPath": "Assets/Sprites/{DesignName}/image1.png" }
       },
       {
         "tool": "import_texture_as_sprite",
         "params": { "assetPath": "Assets/Sprites/{DesignName}/image2.png" }
       }
     ]
   }
   ```
   - 預設參數：`spriteMode: "Single"`, `meshType: "FullRect"`, `compression: "None"`（適合 UI 用途）。

2. **建立 SpriteAtlas（可選）**：
   - 先透過 `ReadMcpResourceTool(uri: "unity://packages")` 確認 `com.unity.2d.sprite` package 已安裝。
   - 若已安裝，使用 `create_sprite_atlas` 建立 SpriteAtlas：
   ```json
   {
     "tool": "create_sprite_atlas",
     "params": {
       "atlasName": "{DesignName}",
       "savePath": "Assets/SpriteAtlas/{DesignName}/{DesignName}.spriteatlas",
       "folderPath": "Assets/Sprites/{DesignName}"
     }
   }
   ```

### 第二階段：建構規劃 (Build Planning)

> **強制門檻**：此階段產出的計劃必須呈現給使用者，經確認後才能進入第三階段。

1. **撰寫 Hierarchy Plan（階層樹）**：

   標註每個節點的 elementType、anchorPreset、Layout Group 類型、ScrollRect 等資訊：

   ```
   TestCanvas                    (Overlay, ScaleWithScreenSize, 1920×1080, Expand)
     └── View                    (stretch-fill)
         └── {DesignName}        (middleCenter, 畫面尺寸)
             └── Container       (CanvasGroup, stretch-fill, 背景色)
                 ├── Header      (topStretch, h:56, HorizontalLayoutGroup)
                 │   ├── Title   (TMP, "標題")
                 │   └── CloseBtn(Button)
                 └── ItemList    (topStretch, ScrollRect)
                     └── Viewport(RectMask2D, stretch)
                         └── Content (VerticalLayoutGroup, spacing:12)
                             ├── Item1 (Panel, h:120)
                             └── Item2 (Panel, h:120, duplicate)
   ```

2. **輸出屬性表格**：

   | 元素 | elementType | anchorPreset | Layout | ScrollRect | 備註 |
   |------|------------|-------------|--------|-----------|------|
   | Header | Panel | topStretch | HorizontalLayoutGroup(spacing:8) | No | 固定高度 |
   | ItemList | Panel | topStretch | — | Yes (vertical) | 外層 ScrollRect 容器，Image alpha=0 |
   | Content | Panel | stretch | VerticalLayoutGroup(spacing:12) | — | ScrollRect 的 content 目標 |

3. **確認 Prefab 策略**：
   - 先建構一個完整實例。
   - 使用 `duplicate_gameobject` 複製為其他實例。
   - 用 `update_component` 更新各實例的差異資料（文字、顏色等）。

4. **等待使用者確認**：將上述階層樹與屬性表格呈現給使用者，**獲得批准後**才進入第三階段。

### 第三階段：Canvas 建構 (Canvas Setup)

1. **檢查 TestCanvas 是否存在**：
   - 使用 `ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 查詢場景階層。
   - 若已存在 `TestCanvas`，跳至步驟 3。

2. **建立 TestCanvas**（僅在不存在時）：
   ```
   create_canvas:
     objectPath: "TestCanvas"
     renderMode: ScreenSpaceOverlay
     scaler:
       uiScaleMode: ScaleWithScreenSize
       referenceResolution: {x: 1920, y: 1080}  # 固定值，不可使用設計畫面尺寸
       screenMatchMode: Expand
   ```

3. **建立 View 容器**（`TestCanvas/View`，stretch-fill，透明背景）。

4. **建立設計框架**（middleCenter，尺寸對應設計畫面）。

5. **建立 Container**（stretch-fill，CanvasGroup，白色或設計背景色）。

> 所有後續 UI 元素均建立在 `TestCanvas/View/` 之下。

### 第四階段：區塊建構 (Section Building)

針對每個 UI 區塊，依以下順序執行：

1. **建立區塊容器**：
   - 全寬區塊：`topStretch`，設定高度，Y 位置為上方區塊累計高度的負值。
   - 固定尺寸區塊：`topLeft`，設定位置與尺寸。

2. **批次建立子元素**：
   使用 `batch_execute` 一次建立所有子元素：
   ```json
   {
     "operations": [
       {
         "id": "element_1",
         "tool": "create_ui_element",
         "params": {
           "objectPath": "TestCanvas/View/.../ParentName/ElementName",
           "elementType": "TextMeshPro",
           "rectTransform": {
             "anchorPreset": "topLeft",
             "pivot": {"x": 0, "y": 1},
             "anchoredPosition": {"x": 96, "y": -24},
             "sizeDelta": {"x": 200, "y": 28}
           },
           "elementData": {
             "text": "...",
             "fontSize": 20,
             "color": {"r": 0, "g": 0, "b": 0, "a": 1},
             "alignment": "MiddleLeft"
           }
         }
       }
     ]
   }
   ```

3. **處理 Button 特殊邏輯**：
   - 先建立 Button，`elementData.color` 設定背景色。
   - 再用 `update_component` 修改 `{ButtonPath}/Text` 子物件的 `UnityEngine.UI.Text` 元件來設定文字顏色。

4. **指定 Sprite 給 Image 元件**（Figma 模式或有圖片資源時）：
   使用 `update_component` 將已匯入的 Sprite 指定給 Image：
   ```json
   {"tool": "update_component", "params": {
     "objectPath": ".../ProductImage",
     "componentName": "Image",
     "componentData": {"sprite": "Assets/Sprites/{DesignName}/tomato.png"}
   }}
   ```

### 第五階段：可複用元件 (Reusable Components)

Prefab 的完整操作流程（新建與修改既有）詳見 `unity-mcp-workflow`「Prefab 操作」。

**慣例**：
- Prefab 存放路徑：`Assets/Prefabs/{DesignName}/`
- 命名：以設計稿元件名稱或功能名稱為準（如 `ProductCard.prefab`、`SettingsRow.prefab`）

**快速流程**：
1. 在場景中建構第一個完整實例。
2. `save_as_prefab` 存為 Prefab（路徑 `Assets/Prefabs/{DesignName}/`）。
3. `add_asset_to_scene` 放置更多實例 + 用 `instanceId` 重新命名 + `update_component` 更新差異。
4. 驗證所有實例 localScale 為 (1,1,1)。
5. 修改既有 Prefab 內部結構時，使用 `open_prefab_contents` → 修改 → `save_prefab_contents`。

### 第六階段：儲存 (Save)

1. **儲存場景**：使用 `save_scene` 儲存當前場景。

## Figma 專屬參考 (Figma-Specific Reference)

本節僅在 Figma 模式下使用。

### Figma MCP 資料來源策略

本 Skill 支援兩組 Figma MCP 工具，可獨立或搭配使用：

| MCP 來源 | 工具 | 用途 | 前置條件 |
|----------|------|------|----------|
| **Figma Plugin MCP**（必備） | `get_figma_data`, `download_figma_images` | 取得原始結構資料與圖片 | Figma URL 中的 fileKey + nodeId |
| **Figma Dev Mode MCP**（推薦） | `get_design_context`, `get_variable_defs`, `get_metadata`, `get_screenshot` | 語義化設計上下文、Design Token、截圖 | Figma Desktop 已啟用 Dev Mode MCP Server |

#### Dev Mode MCP 設定（可選）

> 若使用者尚未設定 Dev Mode MCP，可跳過所有標記「Dev Mode MCP」的步驟，僅用 Plugin MCP 完成建構。

1. **啟用**：Figma Desktop → Preferences → 開啟 "Dev Mode MCP Server"（本機 `http://127.0.0.1:3845/sse`）。
2. **連接**：
   ```bash
   claude mcp add --transport sse figma-dev-mode-mcp-server http://127.0.0.1:3845/sse
   ```
3. **遠端方案**（團隊共用）：使用 Figma Remote MCP Server，需提供 frame/layer 連結而非依賴桌面選取。

#### 資料來源優先順序

當 Dev Mode MCP 可用時：
- **色彩/字型/間距** → 優先用 `get_variable_defs`（Design Token，最權威）
- **佈局結構** → `get_figma_data`（主要）+ `get_design_context`（補充語義）
- **大型設計** → 先用 `get_metadata`（sparse XML 概覽），再對特定區塊用 `get_design_context` 深入
- **視覺對照** → `get_screenshot` 截取 frame 作為 layout fidelity 基準

### Figma → Unity 座標對應

| Figma 屬性 | Unity RectTransform 屬性 | 說明 |
|-------------|-------------------------|------|
| X, Y (相對於父層左上角) | anchoredPosition (x, -y) | Y 軸翻轉：Figma 向下為正，Unity 向下為負 |
| Width, Height | sizeDelta (w, h) | 直接對應 |
| 填滿父層 | anchorPreset: `stretch`, sizeDelta: (0, 0) | 四邊 offset 為 0 |
| 水平填滿 | anchorPreset: `topStretch`, sizeDelta.y = 高度 | 常用於 NavBar、標題列 |

### Figma Layout Group 分析補充

在 Figma 分析階段，Layout Group 判斷**優先使用 Figma Auto Layout 屬性**：

- 若節點有 `layoutMode: "HORIZONTAL"` → 直接對應 `HorizontalLayoutGroup`
- 若節點有 `layoutMode: "VERTICAL"` → 直接對應 `VerticalLayoutGroup`
- 提取 `itemSpacing` → `spacing`、`paddingLeft/Right/Top/Bottom` → `padding`

**僅在節點無 Auto Layout 時**，才 fallback 到座標規律演算法（取子元素 x/y/w/h 計算 gap，詳見上方「Layout Group 判斷規則」）。

## 建構 Checklist

### 計劃審核
- [ ] 建構計劃（階層樹 + 屬性表格）已呈現給使用者
- [ ] 使用者已確認/批准計劃

### 結構
- [ ] Canvas 設定正確（ScaleWithScreenSize, 1920×1080, Expand）
- [ ] View 容器 stretch-fill
- [ ] Container 有 CanvasGroup

### 設計輸入分析（依模式）
- [ ] **Figma 模式**：Figma 資料已取得、圖片已下載、Sprite 已匯入
- [ ] **描述模式**：不明確的細節已向使用者確認
- [ ] **視覺參考模式**：UI 元素與佈局已從圖片中分析
- [ ] **規格模式**：規格中的元素定義已解析

### Layout Group 分析
- [ ] 每個擁有 ≥2 個同類子元素的父節點已分析
- [ ] 分析過程已列出（Figma Auto Layout 或座標演算法）
- [ ] 判定結果已標注在階層樹中

### Layout Group 與 ScrollRect
- [ ] 規律排列的子元素已使用對應的 Layout Group
- [ ] 超出容器的 Layout Group 已包 ScrollRect
- [ ] ScrollRect 結構正確（ScrollRect+Image → Viewport+RectMask2D → Content+LayoutGroup）
- [ ] 無背景圖的 ScrollRect Image alpha 設為 0

### 座標
- [ ] 所有元素的 anchoredPosition 與設計座標一致（Figma 模式：Y 軸取負）
- [ ] anchorPreset 與 pivot 搭配正確
- [ ] sizeDelta 與設計尺寸一致

### 色彩與文字
- [ ] 所有 Hex 色碼正確轉換為 0-1 RGB
- [ ] 字號、對齊方式與設計一致
- [ ] Button 文字顏色已單獨設定

### 可複用元件
- [ ] 可複用元件已建立 Prefab（存放 `Assets/Prefabs/{DesignName}/`）
- [ ] 重複結構已使用 duplicate/add_asset_to_scene 而非重新建立
- [ ] 各實例的差異資料已更新
- [ ] 所有實例的 localScale 為 (1,1,1)
- [ ] 若修改既有 Prefab，已使用 `open_prefab_contents` → 修改 → `save_prefab_contents` 流程

### Sprite 匯入與指定（Figma 模式）
- [ ] 所有下載圖片已透過 `import_texture_as_sprite` 設定為 Sprite 類型
- [ ] SpriteAtlas 已建立（若 `com.unity.2d.sprite` 已安裝）
- [ ] 所有 Image 元件已透過 `update_component` 指定對應的 Sprite

### Dev Mode MCP 增強（可選，當 Dev Mode MCP 已連接時）
- [ ] 已用 `get_variable_defs` 取得 Design Token，色彩表/字型表以 Token 為權威來源
- [ ] 已用 `get_design_context` 取得語義化設計上下文，用於交叉驗證結構判斷
- [ ] 大型設計已用 `get_metadata` 取得概覽，避免 context 溢出
- [ ] 已用 `get_screenshot` 取得截圖作為 layout fidelity 對照基準

### 最終
- [ ] 場景已儲存
- [ ] 記錄需手動完成的項目（字型匯入等）

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認建構計劃就開始呼叫 MCP 建構工具
2. ❌ 未分析設計輸入就開始建構（Figma 模式需分析結構、描述模式需確認細節）
3. ❌ 逐個呼叫 `create_ui_element` 而不使用 `batch_execute`
4. ❌ 建立子元素時父物件尚未存在
5. ❌ Figma 模式下忘記 Y 軸翻轉（Figma Y 正 → Unity Y 負）
6. ❌ 用 `create_prefab` 企圖從場景物件建立 Prefab（它只建空 Prefab，應使用 `save_as_prefab`）
7. ❌ 假設 Button 文字子物件為 TMP（實際為 legacy Text，名稱為 `Text`）
8. ❌ 直接在 `elementData.color` 設定 Button 文字顏色（那是背景色）
9. ❌ 規律排列的子元素不使用 Layout Group 而逐個絕對定位
10. ❌ 跳過 Layout Group 分析，僅憑「感覺」判斷是否需要 Layout Group
11. ❌ ScrollRect 結構不按規範（缺少 Viewport/RectMask2D 或 Content/LayoutGroup）
12. ❌ localScale 不為 (1,1,1) 而未修正
13. ❌ 可複用元件只用 duplicate 而不建立 Prefab
14. ❌ 跳過場景儲存
15. ❌ 直接修改場景中的 Prefab 實例結構（應使用 `open_prefab_contents` 編輯 Prefab 資產）
16. ❌ 在 Prefab Edit Mode 中忘記呼叫 `save_prefab_contents` 結束編輯
17. ❌ 在 Canvas 下用 `update_gameobject` 建立 UI 物件（無 RectTransform，應使用 `create_ui_element`；工具會回傳警告提示）
18. ❌ 組件加錯後 `delete_gameobject` 重建整個 GO，應改用 `remove_component` 移除錯誤組件
19. ❌ 在沒有 Canvas 的情況下建立 UI 元素
20. ❌ 手動設定 localScale 為非 (1,1,1) 的值
21. ❌ 忽略 `update_gameobject` 回傳的 Canvas/RectTransform 警告訊息

## 手動後續步驟 (Post-Implementation)

以下操作目前無法透過 MCP 自動完成，需使用者在 Unity Editor 手動處理：

1. **字型匯入**：匯入設計稿指定的字型（如 Inter、Newsreader），建立 TMP Font Asset。
2. **Font Style**：TMP 的 Semi-Bold 等粗細需透過對應的 Font Asset 設定。

## 主動學習 (Active Learning)

- **操作前**：讀取 `doc/lessons/unity-mcp-lessons.md`，避免重蹈已知問題。
- **操作後**：判斷本次操作是否產生新經驗（踩坑、發現隱藏行為、確認可行做法、找到更好方法），若「是」→ 依 `unity-mcp-learning` 協議追加記錄；若「否」→ 不做任何事。
