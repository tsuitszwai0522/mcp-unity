---
name: unity-ui-builder
description: 當使用者提供 Figma 設計稿並要求在 Unity 中建構對應的 UI 時使用。透過 MCP Unity 工具將 Figma 設計 1:1 還原為 Unity UGUI 元件。
---

# Unity UI Builder (Figma 轉 Unity UI)

此 Skill 用於規範從 Figma 設計稿到 Unity UGUI 的完整建構流程。透過 MCP Unity 工具組，將 Figma 的佈局、色彩、文字與元件結構精確還原為 Unity 場景中的 UI 階層。

> **前置知識**：此 Skill 搭配 `unity-mcp-workflow` 使用。以下通用知識請參考該 Skill：
> - UGUI 建構規則（Canvas 標準設定、Anchor Preset 選用表、色彩轉換）
> - Layout Group 判斷規則（完整座標規律演算法）
> - ScrollRect 結構規範（固定結構與建構步驟）
> - Prefab 操作（新建與修改既有 Prefab 的完整流程）
> - MCP 工具注意事項（11 項陷阱清單）
> - 批次操作（`batch_execute`）、路徑格式、UI 建立順序、錯誤處理

## 核心規則 (Core Rules)

1. **座標 1:1 映射**：Figma 的像素座標直接對應 Unity RectTransform 的 anchoredPosition，不做任何縮放換算。
2. **計劃先行 (Plan First)**：必須先完成完整的建構計劃（含 Hierarchy Plan 階層樹 + 屬性表格），**呈現給使用者確認後**才能開始呼叫任何 MCP 建構工具。
3. **Layout Group 自動判斷**：分析子元素排列方式，判斷是否適用 Layout Group（詳見下方「Figma Layout Group 分析補充」與 `unity-mcp-workflow`「Layout Group 判斷規則」）。
4. **ScrollRect 判斷**：當使用 Layout Group 且子元素總尺寸超過容器可視範圍時，必須包一層 ScrollRect（結構規範詳見 `unity-mcp-workflow`）。
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

### Figma Layout Group 分析補充

在 Figma 分析階段，Layout Group 判斷**優先使用 Figma Auto Layout 屬性**：

- 若節點有 `layoutMode: "HORIZONTAL"` → 直接對應 `HorizontalLayoutGroup`
- 若節點有 `layoutMode: "VERTICAL"` → 直接對應 `VerticalLayoutGroup`
- 提取 `itemSpacing` → `spacing`、`paddingLeft/Right/Top/Bottom` → `padding`

**僅在節點無 Auto Layout 時**，才 fallback 到座標規律演算法（取子元素 x/y/w/h 計算 gap，詳見 `unity-mcp-workflow`「Layout Group 判斷規則」）。

## 執行流程 (Workflow)

### 第一階段：Figma 分析 (Figma Analysis)

1. **取得設計資料**：
   - 使用 `get_figma_data` 取得指定節點的完整佈局資料。
   - 參數：`fileKey`（從 Figma URL 擷取）、`nodeId`（目標畫面節點）。

2. **擷取圖片資源**：
   - 使用 `download_figma_images` 下載所有圖片與圖示。
   - 點陣圖（PNG）需包含 `imageRef`。
   - 向量圖（SVG）僅需 `nodeId` 與 `fileName`。
   - 儲存至 `Assets/Sprites/{DesignName}/` 目錄。

3. **分析設計結構**：
   - 識別 **可複用元件**（出現 2 次以上的相同結構）→ 標記為 Prefab 候選。
   - 記錄色彩表（Hex → Unity RGB）。
   - 記錄字型表（字體、字號、粗細）。
   - 建立完整的 UI 階層樹。

4. **Layout Group 分析（強制）**：
   - 優先檢查 Figma Auto Layout 屬性（`layoutMode`），直接對應 Layout Group。
   - 無 Auto Layout 時，對每個擁有 ≥2 個同類子元素的父節點，執行座標規律演算法（詳見 `unity-mcp-workflow`）。
   - 列出分析過程（取 x/y/w/h → 計算 gap → 判定結果），確保可驗證。
   - 將判定結果（Layout 類型 + spacing）標注在階層樹對應節點上。
   - 判定使用 Layout Group 後，評估子元素總尺寸是否超過容器可視範圍，若超過則標記需要 ScrollRect。

### 第 1.5 階段：Sprite 匯入 (Sprite Import)

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
       referenceResolution: {x: 1920, y: 1080}  # 固定值，不可使用 Figma 畫面尺寸
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

Prefab 的完整操作流程（新建與修改既有）詳見 `unity-mcp-workflow`「Prefab 操作」。

**Figma 專案慣例**：
- Prefab 存放路徑：`Assets/Prefabs/{DesignName}/`
- 命名：以設計稿元件名稱為準（如 `ProductCard.prefab`）

**快速流程**：
1. 在場景中建構第一個完整實例。
2. `save_as_prefab` 存為 Prefab（路徑 `Assets/Prefabs/{DesignName}/`）。
3. `add_asset_to_scene` 放置更多實例 + 用 `instanceId` 重新命名 + `update_component` 更新差異。
4. 驗證所有實例 localScale 為 (1,1,1)。
5. 修改既有 Prefab 內部結構時，使用 `open_prefab_contents` → 修改 → `save_prefab_contents`。

### 第六階段：儲存 (Save)

1. **儲存場景**：使用 `save_scene` 儲存當前場景。

## 建構 Checklist

### 計劃審核
- [ ] 建構計劃（階層樹 + 屬性表格）已呈現給使用者
- [ ] 使用者已確認/批准計劃

### 結構
- [ ] Canvas 設定正確（ScaleWithScreenSize, 1920×1080, Expand）
- [ ] View 容器 stretch-fill
- [ ] 設計框架尺寸與 Figma 一致
- [ ] Container 有 CanvasGroup

### Layout Group 分析
- [ ] 每個擁有 ≥2 個同類子元素的父節點已分析（優先 Auto Layout，fallback 座標演算法）
- [ ] 分析過程已列出
- [ ] 判定結果已標注在階層樹中

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
- [ ] 可複用元件已建立 Prefab（存放 `Assets/Prefabs/{DesignName}/`）
- [ ] 重複結構已使用 duplicate/add_asset_to_scene 而非重新建立
- [ ] 各實例的差異資料已更新
- [ ] 所有實例的 localScale 為 (1,1,1)
- [ ] 若修改既有 Prefab，已使用 `open_prefab_contents` → 修改 → `save_prefab_contents` 流程

### Sprite 匯入與指定
- [ ] 所有下載圖片已透過 `import_texture_as_sprite` 設定為 Sprite 類型
- [ ] SpriteAtlas 已建立（若 `com.unity.2d.sprite` 已安裝）
- [ ] 所有 Image 元件已透過 `update_component` 指定對應的 Sprite

### 最終
- [ ] 場景已儲存
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
10. ❌ 跳過 Layout Group 分析，僅憑「感覺」判斷是否需要 Layout Group
11. ❌ ScrollRect 結構不按規範（缺少 Viewport/RectMask2D 或 Content/LayoutGroup）
12. ❌ localScale 不為 (1,1,1) 而未修正
13. ❌ 可複用元件只用 duplicate 而不建立 Prefab
14. ❌ 跳過場景儲存
15. ❌ 直接修改場景中的 Prefab 實例結構（應使用 `open_prefab_contents` 編輯 Prefab 資產）
16. ❌ 在 Prefab Edit Mode 中忘記呼叫 `save_prefab_contents` 結束編輯
17. ❌ 在 Canvas 下用 `update_gameobject` 建立 UI 物件（無 RectTransform，應使用 `create_ui_element`；工具會回傳警告提示）
18. ❌ 組件加錯後 `delete_gameobject` 重建整個 GO，應改用 `remove_component` 移除錯誤組件

## 手動後續步驟 (Post-Implementation)

以下操作目前無法透過 MCP 自動完成，需使用者在 Unity Editor 手動處理：

1. **字型匯入**：匯入設計稿指定的字型（如 Inter、Newsreader），建立 TMP Font Asset。
2. **Font Style**：TMP 的 Semi-Bold 等粗細需透過對應的 Font Asset 設定。
