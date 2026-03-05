---
name: unity-mcp-workflow
description: Guide for using Unity MCP tools to manipulate Unity Editor. Use when user wants to create GameObjects, UI, scenes, materials, prefabs, or any Unity Editor operations.
---

# Unity MCP Workflow for Claude Code

此規則為 Claude Code 使用 MCP Unity 工具操作 Unity Editor 的行為規範。

> 所有可用工具與資源已由 MCP Server 提供定義，使用 `ListMcpResourcesTool` 可查詢可用資源。
> **UI 建構流程**：若使用者要求建構完整的 UI 介面（Canvas + 多層 UI 元素），請使用 `unity-ui-builder`。本規則負責通用 MCP 工具操作。

## 觸發條件 (When to Activate)

- 使用者要求在 Unity 中建立或修改 GameObject、Material、Scene、Prefab 等
- 安裝 Package、執行 Menu Item、建立 ScriptableObject 資產
- 任何涉及 Unity Editor 操作的請求

## 核心規則 (Core Rules)

1. **先查詢再操作**：修改前先用 `get_scenes_hierarchy` 或 `get_gameobject` 確認現有結構。
2. **批次優先**：多個獨立操作使用 `batch_execute`（上限 100，支援 `atomic: true` 回滾）。
3. **路徑格式**：GameObject 用 `Parent/Child`，Asset 必須以 `Assets/` 開頭。
4. **善用 instanceId**：`get_gameobject` 回傳的 instanceId 比路徑更可靠。
5. **修改後驗證**：改 C# → `recompile_scripts` → 確認編譯通過 → 才能繼續後續操作。
6. **EditMode 優先**：預設用 EditMode 測試。PlayMode 僅限生命週期/Coroutine/Physics/場景載入，需通過 Pre-flight Checklist。完整指引見 `unity-test-debug`。

## Prefab 操作

**A. 新建 Prefab**：場景建構完整實例 → `save_as_prefab` 存為 Prefab → `add_asset_to_scene` 放置更多實例 + 用 `instanceId` 重新命名 → `update_component` 更新差異 → 驗證 localScale (1,1,1)。

**B. 修改既有 Prefab**：`open_prefab_contents(prefabPath)` → 用 `reparent_gameobject`/`set_rect_transform`/`update_component` 修改（objectPath 以 root 名稱開頭，如 `ProductCard/Container`）→ `save_prefab_contents()` 儲存（或 `discard: true` 放棄）。同一時間只能編輯一個 Prefab。

**C. Prefab Variant**：`create_prefab(prefabName, basePrefabPath)` 基於現有 Prefab 建立衍生版本 → 可選加 `componentName`/`fieldValues` 覆寫屬性 → `open_prefab_contents` 修改 → `save_prefab_contents`。多個 Prefab 共用基底結構時使用。

## MCP 工具注意事項

| 陷阱 | 說明 |
|------|------|
| Outline | componentName 為 `Outline`（非 `UnityEngine.UI.Outline`） |
| Prefab 工作流 | 可複用元件用 `save_as_prefab` 存 Prefab → `add_asset_to_scene` 放實例，不可只 duplicate |
| Prefab Edit Mode | 修改既有 Prefab：`open_prefab_contents` → 修改 → `save_prefab_contents`，實例自動同步 |
| Asset Reference | `update_component` 支援 asset path 設定 Sprite/Material/Font，如 `{"sprite": "Assets/Sprites/image.png"}` |
| 命名空間元件名 | `componentName` 支援短名、完整名、assembly-qualified 三種格式，工具自動解析 |
| Scene 物件引用 | `componentData` 支援 asset 引用（path 字串）和 scene 物件引用（instance ID 整數值、`{"instanceId": N}` 或 `{"objectPath": "..."}` 結構） |
| remove_component | 使用 `remove_component` 移除組件（支援 Undo），無法移除 Transform |
| Material 先查再改 | 修改前必須 `get_material_info` 確認 shader 屬性名，不可盲猜 |
| Shader property 差異 | URP: `_BaseColor`/`_BaseMap`/`_Smoothness`；Built-in: `_Color`/`_MainTex`/`_Glossiness` |
| Shader Graph | `.shadergraph` 是二進位格式，不可手寫，只能透過 Unity Editor GUI |
| `get_gameobject` 深層掃描 | `includeChildren: true` 深層階層可能超過 100k 字元。用 `maxDepth: 0~2` 限制，或先 `maxDepth: 1` 取概覽再逐層展開 |
| 新建 .cs 需 Refresh | `recompile_scripts` 報 "type not found" 時，先 `execute_menu_item("Assets/Refresh")` 再重新編譯 |
| 外部寫入檔案需 Refresh | 用 curl/Bash 下載或寫入檔案到 Assets/ 後，必須先 `execute_menu_item("Assets/Refresh")` 才能用 MCP 工具操作（如 `import_texture_as_sprite`），否則報 "asset not found" |
| 清除場景物件引用 | `{"field": null}` **無效**，必須用 `{"field": 0}` 清為 null |
| SpriteAtlas platform override | MCP 無法設定 platform override，需手動編輯 `.spriteatlasv2.meta` 的 `platformSettings` 欄位（`overridden: 1`） |
| MCP 連線中斷 | 長時間操作後可能 timeout。程式碼先 commit、場景定期 `save_scene`、中斷後重試 |

## Figma → Unity 素材匯入工作流

1. **取得素材 URL**：`get_design_context(nodeId, fileKey)` → 回傳圖片 URL（有效期 7 天）
2. **下載到 Assets**：`curl -sL "{url}" -o "Assets/.../filename.png"`（可並行多個 `curl &`）
3. **刷新 AssetDatabase**：`execute_menu_item("Assets/Refresh")` — **必做，否則後續工具找不到檔案**
4. **設定 Sprite 類型**：`import_texture_as_sprite(assetPath)` — 用 `batch_execute` 批量處理
5. **建立 SpriteAtlas**：手寫 `.spriteatlasv2` YAML（packables 引用 Sprites 資料夾 GUID）
6. **設定 Platform Override**：手動編輯 `.spriteatlasv2.meta`，加入 Android/iOS `platformSettings`（MCP 不支援此操作）
7. **最終 Refresh**：`execute_menu_item("Assets/Refresh")`

參考範本：`Assets/ProjectT/AddressablesAssets/Env/Battlefield/Forest2/SpriteAtlas/Forest2Atlas.spriteatlasv2`

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

## ScriptableObject 建立與更新

C# 類別必須已存在且繼承 `ScriptableObject` → `recompile_scripts` 確認編譯 → `create_scriptable_object(typeName, savePath, fieldValues)`。`typeName` 需含命名空間（如 `"MyGame.GameSettings"`）。更新已存在的 SO 欄位值用 `update_scriptable_object(assetPath, fieldValues)`。

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
create_scriptable_object → update_scriptable_object
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

1. 不要忘記 Asset 路徑的 `Assets/` 前綴
2. 不要一次執行大量獨立操作而不使用 `batch_execute`
3. 不要假設物件存在，先查詢確認
4. 不要忽略 instanceId
5. 不要用 `create_prefab` 建場景物件的 Prefab（應用 `save_as_prefab`）
6. 不要直接修改場景中 Prefab 實例結構（應用 `open_prefab_contents`）
7. 不要在 Prefab Edit Mode 中忘記 `save_prefab_contents`
8. 不要修改 C# 後不執行 `recompile_scripts` 驗證編譯結果
9. 不要在未用 `get_material_info` 查詢 shader 屬性前盲目 `modify_material`
10. 不要假設 shader property 名稱（URP 用 `_BaseColor`，Built-in 用 `_Color`，需先查詢）
11. 不要嘗試手寫 `.shadergraph` 檔案（二進位格式，只能 GUI 操作）
12. 不要在未用 `unity://packages` 確認前重複安裝已有的 Package
13. 不要在未用 `unity://menu-items` 確認路徑前執行 `execute_menu_item`
14. 不要在 C# 類別未編譯通過前嘗試 `create_scriptable_object`

## 主動學習 (Active Learning)

- **操作前**：讀取 `doc/lessons/unity-mcp-lessons.md`，避免重蹈已知問題。
- **操作後**：判斷本次操作是否產生新經驗（踩坑、發現隱藏行為、確認可行做法、找到更好方法），若「是」→ 依 `unity-mcp-learning` 協議追加記錄；若「否」→ 不做任何事。
