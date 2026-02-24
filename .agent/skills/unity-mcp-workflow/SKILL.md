---
name: unity-mcp-workflow
description: Guide for using Unity MCP tools to manipulate Unity Editor. Use when user wants to create GameObjects, UI, scenes, materials, prefabs, or any Unity Editor operations.
---

# Unity MCP Workflow Guide

此 Skill 提供 Unity MCP 工具的使用指引，幫助 AI Agent 理解何時、如何使用這些工具來操作 Unity Editor。

> **工具與資源清單**：所有可用的 MCP 工具與資源已由 MCP Server 提供定義，無需在此重複列出。使用 `ListMcpResourcesTool` 可查詢可用資源。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 「幫我在 Unity 建立...」
- 「新增一個 GameObject / UI / Material / Scene」
- 「修改場景中的物件」
- 「建立一個 Prefab / ScriptableObject」
- 「幫我設定 UI 介面」
- 「安裝 XXX Package」「加入套件」
- 「執行 XXX 選單」「用 Menu Item 觸發」
- 「建立一個 ScriptableObject 資產」
- 任何涉及 Unity Editor 操作的請求

## 最佳實踐 (Best Practices)

### 1. 先查詢再操作

在修改場景前，先用 `get_scenes_hierarchy` 或 `get_gameobject` 了解現有結構，不要假設物件存在。

### 2. 使用 batch_execute 提升效能

當需要執行多個獨立操作時，使用 `batch_execute` 減少往返延遲。
- 最多 100 個操作
- 支援 `atomic: true` 回滾（使用 Unity Undo）
- 效能提升 10-100 倍

### 3. 路徑格式

- **GameObject 路徑**：`Parent/Child/Grandchild`
- **Asset 路徑**：`Assets/Folder/File.extension`（必須以 `Assets/` 開頭）

### 4. UI 建立順序

1. Canvas（必須先有）
2. Panel / Container
3. UI 元素（Button、Text 等）
4. Layout 組件

### 5. Material 建立

- 自動偵測 Render Pipeline（URP 用 `Universal Render Pipeline/Lit`，Built-in 用 `Standard`）
- 使用 `color` 參數快速設定基本顏色

### 6. 善用 instanceId

`get_gameobject` 回傳的 `instanceId` 比路徑更可靠，優先使用。

### 7. 修改後驗證規則

修改 C# 代碼後，必須執行 `recompile_scripts` 確認編譯通過，才能繼續後續操作（跑測試、建立 ScriptableObject 等）。若編譯失敗，優先讀取錯誤訊息修正，不可跳過。

### 8. EditMode 優先原則

撰寫 Unity 測試時預設使用 EditMode。PlayMode 僅在需要 MonoBehaviour 生命週期、Coroutine、Physics、場景載入時使用，且必須通過 Pre-flight Checklist。完整測試與除錯指引參見 `unity-test-debug`。

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

1. **優先看 Auto Layout 屬性**：若節點有 `layoutMode: "HORIZONTAL"` 或 `"VERTICAL"`（如 Figma Auto Layout），直接對應 Layout Group，並提取 `itemSpacing`、`padding` 等屬性。

2. **Fallback — 座標規律演算法**（無 Auto Layout 時必須執行）：

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

## Prefab 操作 (Prefab Operations)

### A. 新建 Prefab（場景建構 → 存為 Prefab）

1. **在場景中建構完整實例**：先用一般工具建好第一個完整元件。

2. **存為 Prefab**：
   ```
   save_as_prefab:
     objectPath: "TestCanvas/View/.../ProductCard_Tomato"
     savePath: "Assets/Prefabs/ProductCard.prefab"
   ```
   場景物件自動成為 Prefab 實例（藍色圖示）。

3. **放置更多實例**：
   ```
   add_asset_to_scene:
     assetPath: "Assets/Prefabs/ProductCard.prefab"
     parentPath: "TestCanvas/View/.../Content"
   → 回傳 instanceId: -27572

   update_gameobject:
     instanceId: -27572
     gameObjectData: {name: "ProductCard_Ginger"}
   ```

4. **更新差異資料**：用 `batch_execute` + `update_component` 批次更新各實例的文字、顏色等。

5. **驗證 localScale**：確認所有實例 localScale 為 (1,1,1)，若異常則修正：
   ```
   scale_gameobject:
     objectPath: ".../ProductCard_Ginger"
     scale: {x: 1, y: 1, z: 1}
   ```

### B. 修改既有 Prefab（Prefab Edit Mode）

當需要修改已存在的 Prefab 內部結構時：

1. **開啟 Prefab**：
   ```
   open_prefab_contents:
     prefabPath: "Assets/Prefabs/ProductCard.prefab"
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

> **注意**：同一時間只能編輯一個 Prefab。完成後必須呼叫 `save_prefab_contents`。

### C. Prefab Variant

當需要基於現有 Prefab 建立衍生版本（共用基底結構，覆寫部分屬性）時：

1. **建立 Variant**：
   ```
   create_prefab:
     prefabName: "Assets/Prefabs/EnemyBoss"
     basePrefabPath: "Assets/Prefabs/EnemyBase.prefab"
     componentName: "BossController"   # 可選：加入額外元件
     fieldValues: { "health": 500 }    # 可選：設定覆寫值
   ```

2. **修改 Variant 內部**：使用 Prefab Edit Mode（同 B 節流程）
   ```
   open_prefab_contents → 修改 override 屬性 → save_prefab_contents
   ```

> **使用時機**：當多個 Prefab 共用相同基底結構、僅部分屬性不同時（如不同等級的敵人、不同顏色的道具），使用 Variant 比複製整個 Prefab 更易維護。

## MCP 工具注意事項 (MCP Tool Pitfalls)

| 陷阱 | 說明 |
|------|------|
| CanvasScaler | referenceResolution 固定 **1920×1080** + screenMatchMode **Expand**，不可使用設計畫面尺寸作為 referenceResolution |
| Button 文字子物件 | `create_ui_element` 建立 Button 時，文字子物件名稱為 `Text`（非 `Text (TMP)`），使用 legacy `UnityEngine.UI.Text` 元件 |
| Button 背景色 | `elementData.color` 設定的是 Button 的 Image 背景色，文字顏色需另外透過 `update_component` 修改 `Text` 子物件 |
| Outline 元件名 | 使用 `Outline` 作為 componentName（非 `UnityEngine.UI.Outline`） |
| TMP 元件名 | 使用 `TMPro.TextMeshProUGUI` 作為 componentName 來更新 TextMeshPro 屬性 |
| TMP 顏色 alpha | `create_ui_element` 建立 TextMeshPro 時，`elementData.color` 未指定 `a` 時預設為 1（不透明）。若需半透明文字，需明確帶 `a` 值 |
| ScrollRect Viewport alpha | Viewport 的 Image alpha 必須為 **1**（不可為 0），Mask 需要 stencil buffer 正常寫入才能顯示子元素。`showMaskGraphic: false` 會隱藏 Image 本身 |
| localScale | 所有 UI 元素的 localScale 必須保持 **(1,1,1)**，不可因 CanvasScaler 變更或其他操作而偏移 |
| Prefab 工作流 | 可複用元件必須用 `save_as_prefab` 將場景中建好的實例存為 Prefab，再用 `add_asset_to_scene` 放置更多實例，不可只用 `duplicate_gameobject` |
| Prefab Edit Mode | 修改**既有 Prefab** 內部結構時，使用 `open_prefab_contents` → 修改 → `save_prefab_contents`。objectPath 以 Prefab root 名稱開頭。同一時間只能編輯一個 Prefab |
| Asset Reference 設定 | `update_component` 的 `componentData` 支援以 asset path 字串設定 Sprite、Material、Font 等資源引用，例如 `{"sprite": "Assets/Sprites/image.png"}`。也支援 GUID |
| 命名空間元件名 | `componentName` 支援短名（`Outline`）、完整名（`TMPro.TextMeshProUGUI`）、assembly-qualified（`"Namespace.ClassName, Assembly-CSharp"`）三種格式，工具會自動解析。若解析失敗才會回報 "Component type not found" |
| Scene 物件引用 | `update_component` 支援 asset 引用（asset path 字串）和 scene 內物件引用。場景物件可用 instance ID 整數值（如 `{"target": 12345}`）或結構化引用（`{"target": {"instanceId": 12345}}` / `{"target": {"objectPath": "Path/To/Object"}}`）。結構化引用支援 instanceId 失敗自動 fallback 到 objectPath |
| remove_component | 使用 `remove_component` 移除組件（支援 Undo）。無法移除 Transform 元件。componentName 格式同 `update_component` |
| UI 物件建議用 create_ui_element | 在 Canvas 下用 `update_gameobject`（objectPath 指向不存在路徑）建立的 GO 會自動加上 RectTransform，但不含 CanvasRenderer 和其他 UI 元件。完整 UI 物件（Button、Text 等）仍建議用 `create_ui_element` 建立 |
| Material 先查再改 | 修改 Material 前必須先用 `get_material_info` 確認 shader 類型和屬性名稱，不可盲猜 |
| Shader property 差異 | URP 用 `_BaseColor`，Built-in 用 `_Color`；metallic/smoothness 等屬性名稱也不同，必須先查詢 |
| Shader Graph 不可手寫 | `.shadergraph` 是 Unity 專屬二進位格式，不可用程式碼建立或修改，只能透過 Unity Editor 的 Shader Graph 視覺化介面操作 |

## Material 工作流 (Material Workflow)

### 標準流程

1. **查詢現有 Material**：`get_material_info(materialPath)` — 取得 shader 類型、所有屬性名稱與當前值
2. **建立 Material**：`create_material(name, savePath, color)` — 自動偵測 Render Pipeline
3. **修改屬性**：`modify_material(materialPath, properties)` — 使用正確的屬性名稱
4. **指派到物件**：`assign_material(objectPath, materialPath, slot)`
5. **批量指派**：用 `batch_execute` 一次指派多個物件

### Shader 使用層級

| 層級 | 做法 | 說明 |
|------|------|------|
| **優先** | 引用既有 Shader | URP Lit / Standard / Unlit 等內建 shader，透過 `create_material` 指定 |
| **次選** | 手寫 ShaderLab (.shader) | 需要自訂效果時，寫 `.shader` 檔案並放入 Assets |
| **不支援** | Shader Graph (.shadergraph) | 二進位格式，只能透過 Unity Editor GUI 操作 |

### Shader Property 名稱差異表

| 屬性 | URP (Universal Render Pipeline/Lit) | Built-in (Standard) |
|------|------|------|
| 基本顏色 | `_BaseColor` | `_Color` |
| 主貼圖 | `_BaseMap` | `_MainTex` |
| 金屬度 | `_Metallic` | `_Metallic` |
| 光滑度 | `_Smoothness` | `_Glossiness` |
| 法線貼圖 | `_BumpMap` | `_BumpMap` |
| 發光色 | `_EmissionColor` | `_EmissionColor` |

> **規則**：修改 Material 前**必須**先 `get_material_info` 確認屬性名稱，不可假設。

### Shader 資源查詢

使用 `ReadMcpResourceTool(uri: "unity://shaders")` 可列出專案中所有可用 shader（含 built-in），回傳 name、isBuiltIn、renderQueue、propertyCount 等資訊。

## Scene 管理 (Scene Management)

### 標準流程

1. **查詢當前場景**：`get_scene_info` — 取得 active scene 名稱、路徑、dirty 狀態
2. **儲存場景**：`save_scene` — dirty 場景在切換前必須先存檔
3. **建立新場景**：`create_scene(sceneName, folderPath, addToBuildSettings, makeActive)`
4. **載入場景**：`load_scene(sceneName/scenePath)` — 支援 `additive: true` 多場景載入
5. **卸載場景**：`unload_scene(sceneName/scenePath)` — 可選 `saveIfDirty: true`

### 注意事項

- **Dirty scene 先 save**：切換場景前先檢查 `get_scene_info` 的 `isDirty`，若為 true 則先 `save_scene`
- **Build Settings**：建立場景時設 `addToBuildSettings: true`，否則 build 時不會包含
- **Additive loading**：用 `additive: true` 同時載入多個場景（如 UI 場景 + 遊戲場景）

## Transform 最佳實踐 (Transform Best Practices)

- **一次設定**：優先使用 `set_transform` 同時設定 position、rotation、scale，減少 API 呼叫次數
- **World vs Local**：`space: "world"` 為全域座標（預設），`space: "local"` 為相對父物件座標。嵌套物件通常用 local
- **增量調整**：`relative: true` 在現有值上加減，適合微調位置或旋轉（如 `move_gameobject(relative: true, position: {x: 0, y: 1, z: 0})`）
- **批量佈置**：先 `duplicate_gameobject` 複製，再用 `batch_execute` + `set_transform` 批量設定位置

## Package 管理 (Package Management)

### 標準流程

1. **查詢已安裝 Package**：`ReadMcpResourceTool(uri: "unity://packages")` — 先確認是否已安裝，避免重複
2. **安裝 Package**：`add_package` — 依來源選擇：
   - `source: "registry"` + `packageName`：官方 Package（如 `com.unity.textmeshpro`），可選指定 `version`
   - `source: "github"` + `repositoryUrl`：社群 Package（如 `https://github.com/user/repo.git`），可選 `branch` / `path`
   - `source: "disk"` + `path`：本地 Package（完整資料夾路徑）
3. **驗證安裝**：`recompile_scripts` — 確認無編譯衝突
4. **確認成功**：再次 `ReadMcpResourceTool(uri: "unity://packages")` 確認 Package 出現在清單中

### 注意事項

- **先查再裝**：安裝前必須查詢 `unity://packages`，避免重複安裝或版本衝突
- **編譯驗證**：安裝後務必 `recompile_scripts`，新 Package 可能引入編譯錯誤
- **版本指定**：registry Package 可用 `version` 指定版本（如 `"3.0.6"`），未指定則安裝最新版

## Menu Item 執行 (Menu Item Execution)

### 標準流程

1. **查詢可用 Menu Item**：`ReadMcpResourceTool(uri: "unity://menu-items")` — 確認路徑正確
2. **執行 Menu Item**：`execute_menu_item(menuPath)` — 路徑格式如 `"GameObject/Create Empty"`

### 注意事項

- **先查再執行**：Menu Item 路徑必須完全匹配，執行前先查詢確認
- **用途範例**：觸發 Unity 內建功能（如建立 3D 物件）、Package 提供的選單功能（如 TMP Importer）、自訂選單項目

## ScriptableObject 建立 (ScriptableObject Creation)

### 標準流程

1. **確認類別存在**：目標 C# 類別必須繼承 `ScriptableObject` 且已編譯通過
2. **編譯驗證**：`recompile_scripts` — 確認類別可用
3. **建立資產**：`create_scriptable_object(typeName, savePath, fieldValues)` — `typeName` 為類別名（含命名空間），`savePath` 為資產路徑（如 `"Assets/Data/GameSettings.asset"`）
4. **設定欄位值**：可在建立時透過 `fieldValues` 設定，或之後用 `update_component` 修改

### 注意事項

- **類別必須先存在**：`create_scriptable_object` 不會建立 C# 腳本，只建立 `.asset` 資產
- **命名空間**：若類別有命名空間，`typeName` 需包含完整名稱（如 `"MyGame.GameSettings"`）
- **編譯優先**：若剛寫完 C# 類別，必須先 `recompile_scripts` 確認編譯通過才能建立

## 工作流程範例 (Workflow Examples)

### 範例 1：建立 UI 介面

```
1. 查詢場景階層 → ReadMcpResourceTool(uri: "unity://scenes_hierarchy")
2. 建立 Canvas → create_canvas(objectPath: "MainCanvas", renderMode: "ScreenSpaceOverlay")
3. 批次建立 UI 元素 → batch_execute([
     create_ui_element(Panel), create_ui_element(TextMeshPro), create_ui_element(Button)
   ])
4. 加入 Layout → add_layout_component(layoutType: "VerticalLayoutGroup")
```

### 範例 2：建立帶 Material 的物件

```
1. 建立 Material → create_material(name, savePath, color)
2. 建立 GameObject → execute_menu_item("GameObject/3D Object/Cube")
3. 指派 Material → assign_material(objectPath, materialPath)
4. 調整 Transform → set_transform(objectPath, position, scale)
```

### 範例 3：批量建立物件

```
使用 batch_execute 一次執行多個操作：
→ batch_execute(operations: [
    { tool: "update_gameobject", params: { objectPath: "Enemy1", gameObjectData: { tag: "Enemy" } } },
    { tool: "update_gameobject", params: { objectPath: "Enemy2", gameObjectData: { tag: "Enemy" } } },
    { tool: "update_gameobject", params: { objectPath: "Enemy3", gameObjectData: { tag: "Enemy" } } }
  ])
```

### 範例 4：安裝 Package 並驗證

```
1. 查詢已安裝 Package → ReadMcpResourceTool(uri: "unity://packages")
2. 安裝 Package → add_package(source: "registry", packageName: "com.unity.textmeshpro")
3. 驗證編譯 → recompile_scripts
4. 確認安裝 → ReadMcpResourceTool(uri: "unity://packages")
```

### 範例 5：建立 ScriptableObject 資產

```
1. 確認 C# 類別已存在且編譯通過 → recompile_scripts
2. 建立資產 → create_scriptable_object(
     typeName: "GameSettings",
     savePath: "Assets/Data/GameSettings.asset",
     fieldValues: { "maxPlayers": 4, "gameDuration": 300 }
   )
```

## 工具依賴關係 (Tool Dependencies)

```
create_canvas ──→ create_ui_element ──→ add_layout_component
                         │
                         └──→ set_rect_transform

create_material ──→ assign_material

create_prefab / create_scriptable_object ──→ add_asset_to_scene

save_as_prefab ──→ add_asset_to_scene

open_prefab_contents ──→ save_prefab_contents

update_gameobject ──→ update_component / remove_component
         │
         ├──→ move_gameobject / rotate_gameobject / scale_gameobject
         │
         └──→ reparent_gameobject

unity://packages ──→ add_package ──→ recompile_scripts

unity://menu-items ──→ execute_menu_item

recompile_scripts ──→ create_scriptable_object
```

## 錯誤處理 (Error Handling)

| 錯誤 | 原因 | 解決方案 |
|------|------|---------|
| GameObject not found | 路徑錯誤或物件不存在 | 先用 `get_scenes_hierarchy` 確認路徑 |
| Canvas required | UI 元素沒有 Canvas | 先建立 Canvas |
| Material not found | Asset 路徑錯誤 | 確認路徑以 `Assets/` 開頭且包含 `.mat` |
| Type not found | ScriptableObject 類別不存在 | 確認類別名稱正確，先 `recompile_scripts` |
| Component not found | componentName 無法解析為有效類型 | 確認類別名稱正確（支援短名、完整名、assembly-qualified 格式） |
| 組件加錯需移除 | 誤加了錯誤組件 | 使用 `remove_component` 移除組件 |
| Package already installed | 重複安裝相同 Package | 先用 `unity://packages` 查詢已安裝清單 |
| Menu item not found | menuPath 不正確 | 先用 `unity://menu-items` 查詢可用項目 |
| ScriptableObject type not found | C# 類別未編譯或名稱錯誤 | 先 `recompile_scripts`，確認 `typeName` 含命名空間 |

## 禁止事項 (Don'ts)

1. 不要在沒有 Canvas 的情況下建立 UI 元素
2. 不要忘記 Asset 路徑的 `Assets/` 前綴
3. 不要一次執行大量獨立操作而不使用 `batch_execute`
4. 不要假設物件存在，先查詢確認
5. 不要忽略 `get_gameobject` 回傳的 instanceId，它比路徑更可靠
6. 不要手動設定 localScale 為非 (1,1,1) 的值
7. 不要用 `create_prefab` 企圖從場景物件建立 Prefab（它只建空 Prefab，應使用 `save_as_prefab`）
8. 不要直接修改場景中 Prefab 實例的結構，應使用 `open_prefab_contents` 編輯 Prefab 資產
9. 不要在 Prefab Edit Mode 中忘記呼叫 `save_prefab_contents` 結束編輯
10. 不要建構 ScrollRect 時不按「ScrollRect+Image → Viewport+RectMask2D → Content+LayoutGroup」規範
11. 不要在 Canvas 下用 `update_gameobject` 建立 UI 物件（它不會加 RectTransform，應使用 `create_ui_element`；工具會回傳警告提示）
12. 不要忽略 `update_gameobject` 回傳的 Canvas/RectTransform 警告訊息
13. 不要修改 C# 後不執行 `recompile_scripts` 驗證編譯結果
14. 不要在未用 `get_material_info` 查詢 shader 屬性前盲目 `modify_material`
15. 不要假設 shader property 名稱（URP 用 `_BaseColor`，Built-in 用 `_Color`，需先查詢）
16. 不要嘗試手寫 `.shadergraph` 檔案（二進位格式，只能透過 Unity Editor GUI 操作）
17. 不要在未用 `unity://packages` 確認前重複安裝已有的 Package
18. 不要在未用 `unity://menu-items` 確認路徑前執行 `execute_menu_item`
19. 不要在 C# 類別未編譯通過前嘗試 `create_scriptable_object`
