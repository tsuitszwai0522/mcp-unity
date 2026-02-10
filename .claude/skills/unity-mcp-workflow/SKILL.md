---
name: unity-mcp-workflow
description: Guide for using Unity MCP tools to manipulate Unity Editor. Use when user wants to create GameObjects, UI, scenes, materials, prefabs, or any Unity Editor operations.
---

# Unity MCP Workflow for Claude Code

此規則為 Claude Code 使用 MCP Unity 工具操作 Unity Editor 的行為規範。

> 所有可用工具與資源已由 MCP Server 提供定義，使用 `ListMcpResourcesTool` 可查詢可用資源。

## 觸發條件 (When to Activate)

- 使用者要求在 Unity 中建立或修改 GameObject、UI、Material、Scene、Prefab 等
- 任何涉及 Unity Editor 操作的請求

## 核心規則 (Core Rules)

1. **先查詢再操作**：修改前先用 `get_scenes_hierarchy` 或 `get_gameobject` 確認現有結構。
2. **批次優先**：多個獨立操作使用 `batch_execute`（上限 100，支援 `atomic: true` 回滾）。
3. **路徑格式**：GameObject 用 `Parent/Child`，Asset 必須以 `Assets/` 開頭。
4. **UI 建立順序**：Canvas → Container → UI 元素 → Layout 組件。
5. **善用 instanceId**：`get_gameobject` 回傳的 instanceId 比路徑更可靠。

## 工具依賴關係

```
create_canvas → create_ui_element → add_layout_component / set_rect_transform
create_material → assign_material
create_prefab / create_scriptable_object → add_asset_to_scene
save_as_prefab → add_asset_to_scene          # 場景物件 → Prefab 資產
open_prefab_contents → 修改工具 → save_prefab_contents  # 編輯既有 Prefab
update_gameobject → update_component / move / rotate / scale / reparent
```

## Prefab Edit Mode（編輯既有 Prefab）

用於直接編輯既有 Prefab 資產的內部結構（reparent、新增/移除元件、調整 RectTransform 等），修改會自動同步至所有場景中的 Prefab 實例。

### 工作流程

```
open_prefab_contents(prefabPath) → 修改 → save_prefab_contents()
```

1. **開啟**：`open_prefab_contents(prefabPath: "Assets/Prefabs/.../MyPrefab.prefab")`
   - 回傳 root instanceId、rootName、children 階層
   - Prefab 載入至隔離環境，同一時間只能編輯一個 Prefab
2. **修改**：使用 `objectPath` 格式 `PrefabRoot/Child/SubChild`（以 Prefab root 名稱開頭）
   - 支援：`create_ui_element`、`reparent_gameobject`、`update_component`、`set_rect_transform`、`get_gameobject` 等
3. **儲存**：`save_prefab_contents()` → 所有實例自動同步
   - 放棄：`save_prefab_contents(discard: true)`

### `save_as_prefab` vs `open_prefab_contents`

| 場景 | 使用工具 |
|------|---------|
| 新建 Prefab（場景中建好 → 存為 Prefab） | `save_as_prefab` |
| 修改既有 Prefab 內部結構 | `open_prefab_contents` → 修改 → `save_prefab_contents` |
| 更新 Prefab 實例的個別資料（文字、顏色） | 直接 `update_component` 修改場景實例 |

## 錯誤處理

| 錯誤 | 解決方案 |
|------|---------|
| GameObject not found | 先用 `get_scenes_hierarchy` 確認路徑 |
| Canvas required | 先建立 Canvas |
| Material not found | 確認路徑以 `Assets/` 開頭且含 `.mat` |
| Type not found | 確認類別名稱正確，先 `recompile_scripts` |
| No Prefab is currently being edited | 需先呼叫 `open_prefab_contents` |
| A Prefab is already being edited | 先 `save_prefab_contents` 關閉目前的 Prefab |

## 禁止事項 (Don'ts)

1. 不要在沒有 Canvas 的情況下建立 UI 元素
2. 不要忘記 Asset 路徑的 `Assets/` 前綴
3. 不要一次執行大量獨立操作而不使用 `batch_execute`
4. 不要假設物件存在，先查詢確認
5. 不要忽略 instanceId
6. 不要在 Prefab Edit Mode 中使用 instanceId 參照場景物件（隔離環境的 instanceId 與場景不同）
7. 不要忘記呼叫 `save_prefab_contents` 結束編輯（未儲存的 Prefab 會阻擋下次開啟）
