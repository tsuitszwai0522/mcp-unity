---
name: unity-ui-builder
description: 當使用者提供 Figma 設計稿並要求在 Unity 中建構對應的 UI 時使用。透過 MCP Unity 工具將 Figma 設計 1:1 還原為 Unity UGUI 元件。
---

# Unity UI Builder (Figma 轉 Unity UI)

此 Skill 用於規範從 Figma 設計稿到 Unity UGUI 的完整建構流程。透過 MCP Unity 工具組，將 Figma 的佈局、色彩、文字與元件結構精確還原為 Unity 場景中的 UI 階層。

> **前置知識**：此 Skill 搭配 `unity-mcp-workflow` 使用。批次操作（`batch_execute`）、路徑格式、UI 建立順序、錯誤處理等通用規則請參考該 Skill。

## 核心規則 (Core Rules)

1. **座標 1:1 映射**：Figma 的像素座標直接對應 Unity RectTransform 的 anchoredPosition，不做任何縮放換算。
2. **計劃先行 (Plan First)**：必須先完成完整的建構計劃（含 Hierarchy Plan 階層樹 + 屬性表格），**呈現給使用者確認後**才能開始呼叫任何 MCP 建構工具。
3. **Layout Group 自動判斷**：分析子元素排列方式，判斷是否適用 `HorizontalLayoutGroup`、`VerticalLayoutGroup` 或 `GridLayoutGroup`（詳見「Layout Group 判斷規則」）。
4. **ScrollRect 判斷**：當使用 Layout Group 且子元素總尺寸超過容器可視範圍時，必須包一層 ScrollRect（詳見「ScrollRect 結構規範」）。
5. **批次優先 (Batch First)**：相關聯的 UI 元素必須使用 `batch_execute` 一次建立（詳見 `unity-mcp-workflow`）。
6. **由外而內 (Outside-In)**：建構順序為 Canvas → 容器 → 區塊 → 子元素，確保父物件存在後才建立子物件。
7. **每步驗證**：每個主要區塊完成後，使用 `get_gameobject` 或 `get_ui_element_info` 確認結構正確。

## 必備知識 (Key Knowledge)

### Figma → Unity 座標對應

| Figma 屬性 | Unity RectTransform 屬性 | 說明 |
|-------------|-------------------------|------|
| X, Y (相對於父層左上角) | anchoredPosition (x, -y) | Y 軸翻轉：Figma 向下為正，Unity 向下為負 |
| Width, Height | sizeDelta (w, h) | 直接對應 |
| 填滿父層 | anchorPreset: `stretch`, sizeDelta: (0, 0) | 四邊 offset 為 0 |
| 水平填滿 | anchorPreset: `topStretch`, sizeDelta.y = 高度 | 常用於 NavBar、標題列 |

### Anchor Preset 選用規則

| 使用情境 | anchorPreset | pivot | 說明 |
|----------|-------------|-------|------|
| 從父層左上角絕對定位 | `topLeft` | (0, 1) | 最常用，直接對應 Figma 座標 |
| 水平填滿的區塊 | `topStretch` | (0.5, 1) | NavBar、PageHeading 等全寬元素 |
| 從父層填滿 | `stretch` | (0.5, 0.5) | View、Container 等容器 |
| 置中元素 | `middleCenter` | (0.5, 0.5) | 主要畫面框架 |
| 右對齊文字 | `topRight` | (1, 1) | 價格、數值等靠右元素 |
| 垂直置中靠左 | `middleLeft` | (0, 0.5) | 左側內容 |

### 色彩轉換

Figma Hex → Unity RGB (0-1)：每個通道值除以 255。

```
#426B1F → (0x42/255, 0x6B/255, 0x1F/255) = (0.259, 0.420, 0.122)
#FAFAF5 → (0.980, 0.980, 0.957)
#E6E6E6 → (0.902, 0.902, 0.902)
```

### Layout Group 判斷規則

**判斷順序**：

1. **優先看 Figma Auto Layout**：若節點有 `layoutMode: "HORIZONTAL"` 或 `"VERTICAL"`，直接對應 Layout Group，並提取 `itemSpacing`、`padding` 等屬性。
2. **Fallback — AI 分析座標規律**：若 Figma 沒有 Auto Layout，根據子元素位置/尺寸推斷：
   - 子元素 Y 相同、X 等距排列 → `HorizontalLayoutGroup`
   - 子元素 X 相同、Y 等距排列 → `VerticalLayoutGroup`
   - 子元素呈網格排列（行列規律） → `GridLayoutGroup`
   - 無規律 → 絕對定位，不使用 Layout Group

### ScrollRect 結構規範

當判定使用 Layout Group 且子元素總尺寸**超過**容器可視範圍時，必須使用以下固定結構：

```
{ScrollArea}              (ScrollRect 元件 + Image 元件，無背景圖時 alpha=0)
  ├── Viewport            (RectMask2D 元件，stretch-fill)
  │   └── Content         (Layout Group: Horizontal/Vertical/Grid)
  │       ├── Child1
  │       ├── Child2
  │       └── ...
  └── Scrollbar           (可選，僅當 Figma 設計中有 Scrollbar 時加入)
```

**建構步驟**：
1. 建立外層 Panel 作為 ScrollRect 容器，加入 `ScrollRect` 元件與 `Image` 元件（無背景圖時設 `color.a = 0`）。
2. 建立子物件 `Viewport`，加入 `RectMask2D` 元件，設為 stretch-fill。
3. 建立子物件 `Content`，加入對應的 Layout Group（`VerticalLayoutGroup` / `HorizontalLayoutGroup` / `GridLayoutGroup`）。
4. **Scrollbar（可選）**：若 Figma 設計中存在類似 Scrollbar 的 UI 元素，在 ScrollRect 下（與 Viewport 同層）建立 `Scrollbar`：
   - 使用 `create_ui_element(elementType: "Scrollbar")`，設定 `direction`（垂直滾動用 `BottomToTop`，水平用 `LeftToRight`）。
   - 垂直 Scrollbar：anchor `middleRight`，寬度對應 Figma 設計。
   - 水平 Scrollbar：anchor `bottomStretch`，高度對應 Figma 設計。
5. 使用 `update_component` 將 ScrollRect 的 `content` 指向 Content、`viewport` 指向 Viewport。若有 Scrollbar，將 `verticalScrollbar` 或 `horizontalScrollbar` 指向對應的 Scrollbar 物件。
6. 在 Content 下建立子元素。

**不需要 ScrollRect 的情況**：子元素總尺寸未超過容器 → 僅使用 Layout Group，不包 ScrollRect。

### MCP 工具注意事項

| 陷阱 | 說明 |
|------|------|
| Button 文字子物件 | `create_ui_element` 建立 Button 時，文字子物件名稱為 `Text`（非 `Text (TMP)`），使用 legacy `UnityEngine.UI.Text` 元件 |
| Button 背景色 | `elementData.color` 設定的是 Button 的 Image 背景色，文字顏色需另外透過 `update_component` 修改 `Text` 子物件 |
| Outline 元件名 | 使用 `Outline` 作為 componentName（非 `UnityEngine.UI.Outline`） |
| TMP 元件名 | 使用 `TMPro.TextMeshProUGUI` 作為 componentName 來更新 TextMeshPro 屬性 |
| Prefab 工作流 | 可複用元件須用 `save_as_prefab` 存為 Prefab（`Assets/Prefabs/{DesignName}/`），再用 `add_asset_to_scene` 放置實例，不可只用 duplicate |
| Prefab Edit Mode | 修改**既有 Prefab** 內部結構（reparent、新增元件等）時，使用 `open_prefab_contents` → 修改（objectPath 以 Prefab root 名稱開頭）→ `save_prefab_contents`，所有實例自動同步。同一時間只能編輯一個 Prefab |
| Asset Reference 設定 | `update_component` 的 `componentData` 支援以 asset path 字串設定 Sprite、Material、Font 等資源引用，例如 `{"sprite": "Assets/Sprites/{DesignName}/image.png"}`。也支援 GUID |

## 執行流程 (Workflow)

### 第一階段：Figma 分析 (Figma Analysis)

1. **取得設計資料**：
   - 使用 `get_figma_data` 取得指定節點的完整佈局資料。
   - 參數：`fileKey`（從 Figma URL 擷取）、`nodeId`（目標畫面節點）。

2. **擷取圖片資源**：
   - 使用 `download_figma_images` 下載所有圖片與圖示。
   - 點陣圖（PNG）需包含 `imageRef`。
   - 向量圖（SVG）僅需 `nodeId` 與 `fileName`。
   - 儲存至 `Assets/Images/` 目錄。

3. **分析設計結構**：
   - 識別 **可複用元件**（出現 2 次以上的相同結構）→ 標記為 Prefab 候選。
   - 記錄色彩表（Hex → Unity RGB）。
   - 記錄字型表（字體、字號、粗細）。
   - 建立完整的 UI 階層樹。

### 第二階段：建構規劃 (Build Planning)

> **強制門檻**：此階段產出的計劃必須呈現給使用者，經確認後才能進入第三階段。

1. **撰寫 Hierarchy Plan（階層樹）**：

   標註每個節點的 elementType、anchorPreset、Layout Group 類型、ScrollRect 等資訊：

   ```
   TestCanvas                    (Overlay, ScaleWithScreenSize, 參考解析度, Expand)
     └── View                    (stretch-fill)
         └── {DesignName}        (middleCenter, Figma 畫面尺寸)
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
   - 使用 `save_as_prefab` 將場景物件存為 Prefab。
   - 使用 `add_asset_to_scene` 放置更多 Prefab 實例，用 `instanceId` 重新命名。
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
       referenceResolution: {x: 1920, y: 1080}  # 或依專案設定
       screenMatchMode: Expand
   ```

3. **建立 View 容器**（`TestCanvas/View`，stretch-fill，透明背景）。

4. **建立設計框架**（middleCenter，尺寸對應 Figma 畫面）。

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
             "color": {"r": 0, "g": 0, "b": 0},
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

4. **指定 Sprite 給 Image 元件**：
   使用 `update_component` 將已匯入的 Sprite 指定給 Image：
   ```json
   {"tool": "update_component", "params": {
     "objectPath": ".../ProductImage",
     "componentName": "Image",
     "componentData": {"sprite": "Assets/Sprites/{DesignName}/tomato.png"}
   }}
   ```

### 第五階段：可複用元件 (Reusable Components)

#### A. 新建 Prefab（首次建構）

1. **建構第一個實例**：完整建立所有子元素。

2. **存為 Prefab**：
   使用 `save_as_prefab` 將場景中建好的實例存為 Prefab 資產，場景物件自動成為 Prefab 實例：
   ```
   save_as_prefab:
     objectPath: "TestCanvas/View/.../ProductCard_Tomato"
     savePath: "Assets/Prefabs/{DesignName}/ProductCard.prefab"
   ```

3. **放置更多實例**：
   使用 `add_asset_to_scene` 放置 Prefab 實例，用回傳的 `instanceId` 逐一重新命名：
   ```
   add_asset_to_scene:
     assetPath: "Assets/Prefabs/{DesignName}/ProductCard.prefab"
     parentPath: "TestCanvas/View/.../Content"
   → 回傳 instanceId: -27572

   update_gameobject:
     instanceId: -27572
     gameObjectData: {name: "ProductCard_Ginger"}
   ```

4. **更新差異資料**：
   使用 `batch_execute` 批次更新各實例的文字、顏色等：
   ```json
   {
     "operations": [
       {"tool": "update_component", "params": {
         "objectPath": ".../ProductCard_Ginger/ProductName",
         "componentName": "TMPro.TextMeshProUGUI",
         "componentData": {"text": "新名稱"}
       }}
     ]
   }
   ```

5. **驗證 localScale**：
   確認所有實例 localScale 為 (1,1,1)，若異常則用 `scale_gameobject` 修正。

#### B. 修改既有 Prefab（Prefab Edit Mode）

當需要修改已存在的 Prefab 內部結構（如調整階層、新增元件、修改 RectTransform）時：

1. **開啟 Prefab**：
   ```
   open_prefab_contents:
     prefabPath: "Assets/Prefabs/{DesignName}/ProductCard.prefab"
   → 回傳 rootName, rootInstanceId, children 階層
   ```

2. **修改結構**（objectPath 以 Prefab root 名稱開頭）：
   ```
   reparent_gameobject:
     objectPath: "ProductCard/ProductImage"
     newParent: "ProductCard/Container"

   set_rect_transform:
     objectPath: "ProductCard/Container/ProductImage"
     anchorPreset: topLeft
     ...
   ```

3. **儲存**：
   ```
   save_prefab_contents    → 儲存修改，所有場景實例自動同步
   save_prefab_contents(discard: true)  → 放棄修改
   ```

> **注意**：同一時間只能編輯一個 Prefab。Prefab Edit Mode 使用隔離環境，`GameObject.Find()` 無法搜尋場景物件，但工具已內建 fallback 支援。

### 第六階段：儲存 (Save)

1. **儲存場景**：使用 `save_scene` 儲存當前場景。

## 建構 Checklist

### 計劃審核
- [ ] 建構計劃（階層樹 + 屬性表格）已呈現給使用者
- [ ] 使用者已確認/批准計劃

### 結構
- [ ] Canvas 設定正確（ScaleWithScreenSize, 參考解析度, Expand）
- [ ] View 容器 stretch-fill
- [ ] 設計框架尺寸與 Figma 一致
- [ ] Container 有 CanvasGroup

### Layout Group 與 ScrollRect
- [ ] 規律排列的子元素已使用對應的 Layout Group
- [ ] 超出容器的 Layout Group 已包 ScrollRect
- [ ] ScrollRect 結構正確（ScrollRect+Image → Viewport+RectMask2D → Content+LayoutGroup）
- [ ] 無背景圖的 ScrollRect Image alpha 設為 0

### 座標
- [ ] 所有元素的 anchoredPosition 與 Figma 座標一致（Y 軸取負）
- [ ] anchorPreset 與 pivot 搭配正確
- [ ] sizeDelta 與 Figma 尺寸一致

### 色彩與文字
- [ ] 所有 Hex 色碼正確轉換為 0-1 RGB
- [ ] 字號、對齊方式與 Figma 一致
- [ ] Button 文字顏色已單獨設定

### 可複用元件
- [ ] 可複用元件已用 `save_as_prefab` 存為 Prefab，更多實例用 `add_asset_to_scene` 放置
- [ ] 各實例的差異資料已更新
- [ ] 位置已正確調整
- [ ] 若修改既有 Prefab，已使用 `open_prefab_contents` → 修改 → `save_prefab_contents` 流程
- [ ] Prefab Edit Mode 結束後已呼叫 `save_prefab_contents`（儲存或放棄）

### 最終
- [ ] 場景已儲存
- [ ] 所有 Image 元件已透過 `update_component` 指定對應的 Sprite
- [ ] 記錄需手動完成的項目（字型匯入等）

## 觸發時機 (When to use)

- 使用者說：「把這個 Figma 設計做成 Unity UI」
- 使用者提供 Figma 連結並要求建構 UI
- 使用者說：「幫我在 Unity 裡重現這個設計」
- 使用者說：「用 MCP 建立 UI」
- 使用者說：「Figma 轉 Unity」

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認建構計劃就開始呼叫 MCP 建構工具
2. ❌ 未分析 Figma 結構就開始建構
3. ❌ 逐個呼叫 `create_ui_element` 而不使用 `batch_execute`
4. ❌ 建立子元素時父物件尚未存在
5. ❌ 忘記 Y 軸翻轉（Figma Y 正 → Unity Y 負）
6. ❌ 用 `create_prefab` 企圖從場景物件建立 Prefab（它只建空 Prefab，應使用 `save_as_prefab`）
7. ❌ 假設 Button 文字子物件為 TMP（實際為 legacy Text，名稱為 `Text`）
8. ❌ 直接在 `elementData.color` 設定 Button 文字顏色（那是背景色）
9. ❌ 規律排列的子元素不使用 Layout Group 而逐個絕對定位
10. ❌ ScrollRect 結構不按規範（缺少 Viewport/RectMask2D 或 Content/LayoutGroup）
11. ❌ 跳過場景儲存
12. ❌ 直接修改場景中的 Prefab 實例結構（reparent 等），應使用 `open_prefab_contents` 編輯 Prefab 資產本身
13. ❌ 在 Prefab Edit Mode 中忘記呼叫 `save_prefab_contents` 結束編輯

## 手動後續步驟 (Post-Implementation)

以下操作目前無法透過 MCP 自動完成，需使用者在 Unity Editor 手動處理：

1. **字型匯入**：匯入設計稿指定的字型（如 Inter、Newsreader），建立 TMP Font Asset。
2. **Font Style**：TMP 的 Semi-Bold 等粗細需透過對應的 Font Asset 設定。

