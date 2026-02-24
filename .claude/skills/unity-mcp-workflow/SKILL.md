---
name: unity-mcp-workflow
description: Guide for using Unity MCP tools to manipulate Unity Editor. Use when user wants to create GameObjects, UI, scenes, materials, prefabs, or any Unity Editor operations.
---

# Unity MCP Workflow for Claude Code

此規則為 Claude Code 使用 MCP Unity 工具操作 Unity Editor 的行為規範。

> 所有可用工具與資源已由 MCP Server 提供定義，使用 `ListMcpResourcesTool` 可查詢可用資源。

## 觸發條件 (When to Activate)

- 使用者要求在 Unity 中建立或修改 GameObject、UI、Material、Scene、Prefab 等
- 安裝 Package、執行 Menu Item、建立 ScriptableObject 資產
- 任何涉及 Unity Editor 操作的請求

## 核心規則 (Core Rules)

1. **先查詢再操作**：修改前先用 `get_scenes_hierarchy` 或 `get_gameobject` 確認現有結構。
2. **批次優先**：多個獨立操作使用 `batch_execute`（上限 100，支援 `atomic: true` 回滾）。
3. **路徑格式**：GameObject 用 `Parent/Child`，Asset 必須以 `Assets/` 開頭。
4. **UI 建立順序**：Canvas → Container → UI 元素 → Layout 組件。
5. **善用 instanceId**：`get_gameobject` 回傳的 instanceId 比路徑更可靠。
6. **修改後驗證**：改 C# → `recompile_scripts` → 確認編譯通過 → 才能繼續後續操作。
7. **EditMode 優先**：預設用 EditMode 測試。PlayMode 僅限生命週期/Coroutine/Physics/場景載入，需通過 Pre-flight Checklist。完整指引見 `unity-test-debug`。

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

### Layout Group 判斷

對每個擁有 ≥2 個同類子元素的父節點：優先使用 Auto Layout 屬性（`layoutMode`）直接對應；若無，取所有子元素 `(x, y, w, h)`，計算相鄰元素 gap — x 相同 + w 相同 + gap 相等 → `VerticalLayoutGroup`；y 相同 + gap 相等 → `HorizontalLayoutGroup`；多行多列規律 → `GridLayoutGroup`；皆不符 → 絕對定位。

### ScrollRect 結構

子元素總尺寸超過容器時，使用固定結構：`ScrollRect+Image(a=0) → Viewport(RectMask2D, stretch-fill) → Content(LayoutGroup) → Children`。用 `update_component` 將 ScrollRect 的 `content` 指向 Content、`viewport` 指向 Viewport。可選加入 Scrollbar（與 Viewport 同層）。

## Prefab 操作

**A. 新建 Prefab**：場景建構完整實例 → `save_as_prefab` 存為 Prefab → `add_asset_to_scene` 放置更多實例 + 用 `instanceId` 重新命名 → `update_component` 更新差異 → 驗證 localScale (1,1,1)。

**B. 修改既有 Prefab**：`open_prefab_contents(prefabPath)` → 用 `reparent_gameobject`/`set_rect_transform`/`update_component` 修改（objectPath 以 root 名稱開頭，如 `ProductCard/Container`）→ `save_prefab_contents()` 儲存（或 `discard: true` 放棄）。同一時間只能編輯一個 Prefab。

**C. Prefab Variant**：`create_prefab(prefabName, basePrefabPath)` 基於現有 Prefab 建立衍生版本 → 可選加 `componentName`/`fieldValues` 覆寫屬性 → `open_prefab_contents` 修改 → `save_prefab_contents`。多個 Prefab 共用基底結構時使用。

## MCP 工具注意事項

| 陷阱 | 說明 |
|------|------|
| CanvasScaler | referenceResolution 固定 1920×1080 + Expand |
| Button 文字 | 子物件名 `Text`，元件 `UnityEngine.UI.Text`，非 TMP |
| Button 背景 | `elementData.color` 是 Image 背景色，非文字色 |
| Outline | componentName 為 `Outline`（非 `UnityEngine.UI.Outline`） |
| TMP 元件名 | componentName 為 `TMPro.TextMeshProUGUI` |
| TMP alpha | 建立 TMP 時 `color` 未指定 `a` 預設為 1（不透明），需半透明時才需明確帶 `a` |
| Viewport alpha | ScrollRect Viewport Image alpha 必須為 1，Mask stencil 才能正常運作 |
| localScale | 所有 UI 元素 localScale 保持 (1,1,1) |
| Prefab 工作流 | 可複用元件用 `save_as_prefab` 存 Prefab → `add_asset_to_scene` 放實例，不可只 duplicate |
| Prefab Edit Mode | 修改既有 Prefab：`open_prefab_contents` → 修改 → `save_prefab_contents`，實例自動同步 |
| Asset Reference | `update_component` 支援 asset path 設定 Sprite/Material/Font，如 `{"sprite": "Assets/Sprites/image.png"}` |
| 命名空間元件名 | `componentName` 支援短名、完整名、assembly-qualified 三種格式，工具自動解析 |
| Scene 物件引用 | `componentData` 支援 asset 引用（path 字串）和 scene 物件引用（instance ID 整數值、`{"instanceId": N}` 或 `{"objectPath": "..."}` 結構） |
| remove_component | 使用 `remove_component` 移除組件（支援 Undo），無法移除 Transform |
| UI 物件建議用 create_ui_element | Canvas 下用 `update_gameobject` 建立的 GO 會自動加 RectTransform，但不含 CanvasRenderer 等 UI 元件。完整 UI 物件仍建議用 `create_ui_element` |
| Material 先查再改 | 修改前必須 `get_material_info` 確認 shader 屬性名，不可盲猜 |
| Shader property 差異 | URP: `_BaseColor`/`_BaseMap`/`_Smoothness`；Built-in: `_Color`/`_MainTex`/`_Glossiness` |
| Shader Graph | `.shadergraph` 是二進位格式，不可手寫，只能透過 Unity Editor GUI |

## Material 工作流

`get_material_info` → `create_material` → `modify_material` → `assign_material` → `batch_execute` 批量指派。

| Shader 層級 | 做法 |
|------------|------|
| 優先 | 引用既有 Shader（URP Lit / Standard / Unlit） |
| 次選 | 手寫 ShaderLab (.shader) |
| 不支援 | Shader Graph (.shadergraph)，只能 GUI 操作 |

Shader property 差異：URP `_BaseColor`/`_BaseMap`/`_Smoothness` vs Built-in `_Color`/`_MainTex`/`_Glossiness`。修改前**必須** `get_material_info` 確認。

查詢可用 shader：`ReadMcpResourceTool(uri: "unity://shaders")`。

## Scene 管理

- `get_scene_info` → `save_scene` → `create_scene` → `load_scene(additive)` → `unload_scene`
- Dirty scene 先 `save_scene` 再切換
- `addToBuildSettings: true` 確保場景包含在 build 中
- `additive: true` 用於多場景同時載入

## Transform 最佳實踐

- 優先 `set_transform` 一次設定 position/rotation/scale
- `space: "world"`（預設）vs `"local"`（嵌套物件）
- `relative: true` 增量調整
- `duplicate_gameobject` + `batch_execute` + `set_transform` 批量佈置

## Package 管理

`ReadMcpResourceTool(uri: "unity://packages")` 查詢已安裝 → `add_package(source, packageName/repositoryUrl/path)` 安裝 → `recompile_scripts` 驗證 → 再次查詢確認。

- `source: "registry"` + `packageName`：官方 Package（可選 `version`）
- `source: "github"` + `repositoryUrl`：社群 Package（可選 `branch`/`path`）
- `source: "disk"` + `path`：本地 Package

## Menu Item 執行

`ReadMcpResourceTool(uri: "unity://menu-items")` 查詢可用項目 → `execute_menu_item(menuPath)` 執行。路徑必須完全匹配（如 `"GameObject/Create Empty"`）。

## ScriptableObject 建立

C# 類別必須已存在且繼承 `ScriptableObject` → `recompile_scripts` 確認編譯 → `create_scriptable_object(typeName, savePath, fieldValues)`。`typeName` 需含命名空間（如 `"MyGame.GameSettings"`）。

## 工具依賴關係

```
create_canvas → create_ui_element → add_layout_component / set_rect_transform
create_material → assign_material
create_prefab / create_scriptable_object → add_asset_to_scene
save_as_prefab → add_asset_to_scene
open_prefab_contents → save_prefab_contents
update_gameobject → update_component / remove_component / move / rotate / scale / reparent
unity://packages → add_package → recompile_scripts
unity://menu-items → execute_menu_item
recompile_scripts → create_scriptable_object
```

## 錯誤處理

| 錯誤 | 解決方案 |
|------|---------|
| GameObject not found | 先用 `get_scenes_hierarchy` 確認路徑 |
| Canvas required | 先建立 Canvas |
| Material not found | 確認路徑以 `Assets/` 開頭且含 `.mat` |
| Type not found | 確認類別名稱正確，先 `recompile_scripts` |
| Component not found | 確認類別名稱正確（支援短名、完整名、assembly-qualified 格式） |
| 組件加錯需移除 | 使用 `remove_component` 移除組件 |
| Package already installed | 先 `unity://packages` 查詢已安裝清單 |
| Menu item not found | 先 `unity://menu-items` 查詢可用項目 |
| ScriptableObject type not found | 先 `recompile_scripts`，確認 `typeName` 含命名空間 |

## 禁止事項 (Don'ts)

1. 不要在沒有 Canvas 的情況下建立 UI 元素
2. 不要忘記 Asset 路徑的 `Assets/` 前綴
3. 不要一次執行大量獨立操作而不使用 `batch_execute`
4. 不要假設物件存在，先查詢確認
5. 不要忽略 instanceId
6. 不要手動設定 localScale 為非 (1,1,1) 的值
7. 不要用 `create_prefab` 建場景物件的 Prefab（應用 `save_as_prefab`）
8. 不要直接修改場景中 Prefab 實例結構（應用 `open_prefab_contents`）
9. 不要在 Prefab Edit Mode 中忘記 `save_prefab_contents`
10. 不要建構 ScrollRect 時不按規範結構
11. 不要在 Canvas 下用 `update_gameobject` 建立 UI 物件（應用 `create_ui_element`；工具會回傳警告）
12. 不要忽略 `update_gameobject` 回傳的 Canvas/RectTransform 警告訊息
13. 不要修改 C# 後不執行 `recompile_scripts` 驗證編譯結果
14. 不要在未用 `get_material_info` 查詢 shader 屬性前盲目 `modify_material`
15. 不要假設 shader property 名稱（URP 用 `_BaseColor`，Built-in 用 `_Color`，需先查詢）
16. 不要嘗試手寫 `.shadergraph` 檔案（二進位格式，只能 GUI 操作）
17. 不要在未用 `unity://packages` 確認前重複安裝已有的 Package
18. 不要在未用 `unity://menu-items` 確認路徑前執行 `execute_menu_item`
19. 不要在 C# 類別未編譯通過前嘗試 `create_scriptable_object`
