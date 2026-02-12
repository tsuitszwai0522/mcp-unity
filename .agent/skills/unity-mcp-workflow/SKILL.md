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

## 工具依賴關係 (Tool Dependencies)

```
create_canvas ──→ create_ui_element ──→ add_layout_component
                         │
                         └──→ set_rect_transform

create_material ──→ assign_material

create_prefab / create_scriptable_object ──→ add_asset_to_scene

save_as_prefab ──→ add_asset_to_scene          # 場景物件 → Prefab 資產

open_prefab_contents ──→ 修改工具 ──→ save_prefab_contents  # 編輯既有 Prefab

update_gameobject ──→ update_component / remove_component
         │
         ├──→ move_gameobject / rotate_gameobject / scale_gameobject
         │
         └──→ reparent_gameobject
```

## Prefab Edit Mode（編輯既有 Prefab）

用於直接編輯既有 Prefab 資產的內部結構（reparent、新增/移除元件、調整 RectTransform 等），修改會自動同步至所有場景中的 Prefab 實例。

### 工作流程

```
open_prefab_contents(prefabPath) → 修改 → save_prefab_contents()
```

1. **開啟 Prefab**：`open_prefab_contents(prefabPath: "Assets/Prefabs/.../MyPrefab.prefab")`
   - 回傳 root instanceId、rootName、children 階層
   - Prefab 載入至隔離編輯環境，同一時間只能編輯一個 Prefab
2. **修改 Prefab 內部結構**：
   - 使用 `objectPath` 格式：`PrefabRoot/Child/SubChild`（以 Prefab root 名稱開頭）
   - 支援的工具：`create_ui_element`、`reparent_gameobject`、`update_component`、`set_rect_transform`、`get_gameobject` 等
3. **儲存或放棄**：
   - 儲存：`save_prefab_contents()` → 所有場景中的 Prefab 實例自動同步
   - 放棄：`save_prefab_contents(discard: true)` → 捨棄所有修改

### `save_as_prefab` vs `open_prefab_contents` 選擇指引

| 場景 | 使用工具 |
|------|---------|
| **新建** Prefab（場景中建好 → 存為 Prefab） | `save_as_prefab` |
| **修改既有** Prefab 內部結構（reparent、新增元件等） | `open_prefab_contents` → 修改 → `save_prefab_contents` |
| 更新 Prefab 實例的個別資料（文字、顏色） | 直接用 `update_component` 修改場景實例 |

### 範例：修改既有 Prefab 的階層

```
1. 開啟 Prefab → open_prefab_contents(prefabPath: "Assets/Prefabs/Shop/ProductCard.prefab")
   → 回傳 rootName: "ProductCard", rootInstanceId: 69086, children: [...]

2. 修改結構（以 PrefabRoot 名稱為路徑起點）：
   → reparent_gameobject(objectPath: "ProductCard/ProductImage", newParent: "ProductCard/Container")
   → set_rect_transform(objectPath: "ProductCard/Container/ProductImage", ...)

3. 儲存 → save_prefab_contents()
   → 所有場景中的 ProductCard 實例自動反映新結構
```

## 錯誤處理 (Error Handling)

| 錯誤 | 原因 | 解決方案 |
|------|------|---------|
| GameObject not found | 路徑錯誤或物件不存在 | 先用 `get_scenes_hierarchy` 確認路徑 |
| Canvas required | UI 元素沒有 Canvas | 先建立 Canvas |
| Material not found | Asset 路徑錯誤 | 確認路徑以 `Assets/` 開頭且包含 `.mat` |
| Type not found | ScriptableObject 類別不存在 | 確認類別名稱正確，先 `recompile_scripts` |
| No Prefab is currently being edited | 未開啟 Prefab | 需先呼叫 `open_prefab_contents` |
| A Prefab is already being edited | 上一個 Prefab 未關閉 | 先 `save_prefab_contents` 關閉目前的 Prefab |

## 禁止事項 (Don'ts)

1. 不要在沒有 Canvas 的情況下建立 UI 元素
2. 不要忘記 Asset 路徑的 `Assets/` 前綴
3. 不要一次執行大量獨立操作而不使用 `batch_execute`
4. 不要假設物件存在，先查詢確認
5. 不要忽略 `get_gameobject` 回傳的 instanceId，它比路徑更可靠
6. 不要在 Prefab Edit Mode 中使用場景物件的 instanceId（隔離環境的 instanceId 與場景不同）
7. 不要忘記呼叫 `save_prefab_contents` 結束編輯（未儲存的 Prefab 會阻擋下次開啟）
