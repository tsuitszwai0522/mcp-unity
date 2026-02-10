---
name: unity-ui-builder
description: Build Unity UGUI from Figma designs using MCP Unity tools. Use when user provides a Figma design and wants to recreate it in Unity, or asks to build UI with MCP tools.
---

# Unity UI Builder for Claude Code

此規則為 Claude Code 透過 MCP Unity 工具將 Figma 設計稿建構為 Unity UGUI 的行為規範。

## 核心規則 (Core Rules)

1. **座標 1:1 映射**：Figma 像素座標直接對應 Unity anchoredPosition（Y 軸取負）。
2. **計劃先行**：必須先完成建構計劃（階層樹 + 屬性表格），**呈現給使用者確認後**才能呼叫任何 MCP 建構工具。
3. **Layout Group 自動判斷**：優先看 Figma Auto Layout；若無則 AI 從座標規律推斷 Horizontal/Vertical/Grid Layout Group。
4. **ScrollRect 判斷**：Layout Group 子元素總尺寸超過容器時，包一層 ScrollRect（結構：ScrollRect+Image(a=0) → Viewport+RectMask2D → Content+LayoutGroup → Children）。若 Figma 有 Scrollbar UI，在 ScrollRect 下（與 Viewport 同層）加入 `Scrollbar`，並將 ScrollRect 的 `verticalScrollbar`/`horizontalScrollbar` 指向它。
5. **批次優先**：使用 `batch_execute` 批次建立相關元素，單次上限 100 個操作。
6. **由外而內**：建構順序 Canvas → 容器 → 區塊 → 子元素。

## 觸發條件 (When to Activate)

- 使用者提供 Figma 連結或設計稿，要求建構 Unity UI
- 使用者說「Figma 轉 Unity」、「用 MCP 建 UI」
- 使用者說「在 Unity 裡重現這個設計」

## 執行流程 (Workflow)

### 第一階段：Figma 分析

1. **取得設計資料**：用 `get_figma_data` 取得節點佈局。
2. **下載圖片**：用 `download_figma_images` 下載所有圖片資源至 `Assets/Images/`。
3. **分析結構**：識別可複用元件、建立色彩表、字型表、階層樹。

### 第二階段：建構規劃（強制門檻）

1. **撰寫 Hierarchy Plan（階層樹）**：標註 elementType、anchorPreset、Layout Group、ScrollRect。
2. **輸出屬性表格**：每個元素的 Layout/ScrollRect/備註。
3. **確認 Prefab 策略**：標記重複元件，規劃 duplicate + update 流程。
4. **等待使用者確認**：獲得批准後才進入建構階段。

### 第三階段：Canvas 建構

1. **檢查 TestCanvas**：用 `ReadMcpResourceTool(uri: "unity://scenes_hierarchy")` 確認是否存在。
2. **建立 TestCanvas**（僅在不存在時）：`create_canvas(objectPath: "TestCanvas")`，ScreenSpaceOverlay，ScaleWithScreenSize，Expand。
3. **View**：`TestCanvas/View`，stretch-fill 容器。
4. **設計框架**：middleCenter，尺寸對應 Figma 畫面。
5. **Container**：stretch-fill，CanvasGroup，背景色。

> 所有 UI 元素均建立在 `TestCanvas/View/` 之下。

### 第四階段：區塊建構

逐區塊建構，每個區塊使用 `batch_execute`：
- 全寬區塊：`topStretch` + 高度
- 絕對定位：`topLeft` + pivot (0,1) + Figma 座標（Y 取負）
- 右對齊：`topRight` + pivot (1,1) + 負 X offset

### 第五階段：可複用元件

**A. 新建 Prefab**：
1. 建構第一個完整實例。
2. `save_as_prefab` 將場景中建好的實例存為 Prefab（存放 `Assets/Prefabs/{DesignName}/`），場景物件自動成為 Prefab 實例。
3. `add_asset_to_scene` 放置更多 Prefab 實例，用回傳的 `instanceId` 搭配 `update_gameobject` 逐一重新命名。
4. `batch_execute` + `update_component` 更新差異文字/顏色。
5. 驗證所有實例 localScale 為 (1,1,1)，若異常則用 `scale_gameobject` 修正。

**B. 修改既有 Prefab**（Prefab Edit Mode）：
1. `open_prefab_contents(prefabPath)` 開啟 Prefab → 回傳 root 資訊與 children 階層。
2. 使用 `reparent_gameobject`、`set_rect_transform`、`update_component` 等工具修改（objectPath 以 Prefab root 名稱開頭，如 `ProductCard/Container`）。
3. `save_prefab_contents()` 儲存 → 所有場景實例自動同步。或 `save_prefab_contents(discard: true)` 放棄。

### 第六階段：儲存

使用 `save_scene` 儲存場景。

## 關鍵注意事項

| 項目 | 規則 |
|------|------|
| Anchor 定位 | `topLeft` + pivot (0,1) 最常用，直接映射 Figma 座標 |
| Y 軸 | Figma Y 正值 → Unity anchoredPosition Y 負值 |
| Hex 轉 RGB | 每通道除以 255（如 #42 = 0x42/255 = 0.259） |
| Button 文字 | 子物件名為 `Text`，元件為 `UnityEngine.UI.Text`，非 TMP |
| Button 背景 | `elementData.color` 設定 Image 背景色，非文字色 |
| TMP 更新 | componentName 為 `TMPro.TextMeshProUGUI` |
| Outline | componentName 為 `Outline`（非 `UnityEngine.UI.Outline`） |
| Prefab 工作流 | 可複用元件須用 `save_as_prefab` 存為 Prefab（`Assets/Prefabs/{DesignName}/`），再用 `add_asset_to_scene` 放置實例，不可只用 duplicate |
| Prefab Edit Mode | 修改**既有 Prefab** 內部結構時，使用 `open_prefab_contents` → 修改（objectPath 以 Prefab root 名稱開頭）→ `save_prefab_contents`，所有實例自動同步。同一時間只能編輯一個 Prefab |

## 禁止事項 (Don'ts)

1. ❌ 未經使用者確認計劃就開始建構
2. ❌ 未分析 Figma 結構就開始建構
3. ❌ 不使用 `batch_execute` 逐個建立元素
4. ❌ 忘記 Y 軸翻轉
5. ❌ 假設 Button 文字為 TMP
6. ❌ 規律排列子元素不使用 Layout Group
7. ❌ ScrollRect 結構不按規範（缺 Viewport/RectMask2D 或 Content/LayoutGroup）
8. ❌ 跳過場景儲存
9. ❌ 直接修改場景中 Prefab 實例的結構（應用 `open_prefab_contents` 編輯 Prefab 資產）
10. ❌ Prefab Edit Mode 中忘記呼叫 `save_prefab_contents` 結束編輯
