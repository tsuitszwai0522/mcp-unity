---
name: unity-mcp-workflow
description: Guide for using Unity MCP tools to manipulate Unity Editor. Use when user wants to create GameObjects, UI, scenes, materials, prefabs, or any Unity Editor operations.
---

# Unity MCP Workflow Guide

此 Skill 提供 Unity MCP 工具的使用指引，幫助 Claude 理解何時、如何使用這些工具來操作 Unity Editor。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 「幫我在 Unity 建立...」
- 「新增一個 GameObject / UI / Material / Scene」
- 「修改場景中的物件」
- 「建立一個 Prefab / ScriptableObject」
- 「幫我設定 UI 介面」
- 任何涉及 Unity Editor 操作的請求

## 可用工具分類 (Available Tools)

### 1. 場景管理 (Scene Management)
| 工具 | 用途 |
|------|------|
| `create_scene` | 建立新場景 |
| `load_scene` | 載入場景（支援 additive） |
| `save_scene` | 儲存當前場景 |
| `unload_scene` | 卸載場景 |
| `delete_scene` | 刪除場景檔案 |
| `get_scene_info` | 取得當前場景資訊 |

### 2. GameObject 操作 (GameObject Operations)
| 工具 | 用途 |
|------|------|
| `update_gameobject` | 建立或更新 GameObject（名稱、Layer、Tag、Active 狀態）|
| `get_gameobject` | 取得 GameObject 詳細資訊（含所有 Component）|
| `select_gameobject` | 在 Editor 中選取 GameObject |
| `duplicate_gameobject` | 複製 GameObject（支援多個副本）|
| `delete_gameobject` | 刪除 GameObject |
| `reparent_gameobject` | 變更 GameObject 的 Parent |

### 3. Transform 操作 (Transform Operations)
| 工具 | 用途 |
|------|------|
| `move_gameobject` | 移動位置（支援 world/local、絕對/相對）|
| `rotate_gameobject` | 旋轉（支援 world/local、絕對/相對）|
| `scale_gameobject` | 縮放（支援絕對/相對）|
| `set_transform` | 一次設定 position、rotation、scale |

### 4. Component 操作 (Component Operations)
| 工具 | 用途 |
|------|------|
| `update_component` | 新增或更新 Component 的欄位值 |

### 5. 資產建立 (Asset Creation)
| 工具 | 用途 |
|------|------|
| `create_prefab` | 建立 Prefab（可附加 MonoBehaviour）|
| `create_scriptable_object` | 建立 ScriptableObject 資產 |
| `add_asset_to_scene` | 將資產加入場景 |

### 6. Material 操作 (Material Operations)
| 工具 | 用途 |
|------|------|
| `create_material` | 建立 Material（自動偵測 URP/Built-in）|
| `assign_material` | 指派 Material 給 Renderer |
| `modify_material` | 修改 Material 屬性 |
| `get_material_info` | 取得 Material 詳細資訊 |

### 7. UI 操作 (UGUI Operations)
| 工具 | 用途 |
|------|------|
| `create_canvas` | 建立 Canvas（含 CanvasScaler、EventSystem）|
| `create_ui_element` | 建立 UI 元素（Button、Text、Image、Panel 等）|
| `set_rect_transform` | 設定 RectTransform（anchors、pivot、size）|
| `add_layout_component` | 新增 Layout 組件（Horizontal/Vertical/Grid Layout）|
| `get_ui_element_info` | 取得 UI 元素資訊 |

**支援的 UI 元素類型**：
- Button, Text, TextMeshPro
- Image, RawImage, Panel
- InputField, InputFieldTMP
- Toggle, Slider
- Dropdown, DropdownTMP
- ScrollView, Scrollbar

### 8. 其他工具 (Utilities)
| 工具 | 用途 |
|------|------|
| `execute_menu_item` | 執行 Unity 選單項目 |
| `add_package` | 新增 Package（Registry、GitHub、本地）|
| `run_tests` | 執行 Test Runner 測試 |
| `recompile_scripts` | 重新編譯腳本 |
| `send_console_log` | 發送訊息到 Unity Console |
| `get_console_logs` | 取得 Console 日誌 |
| `batch_execute` | **批量執行多個操作（重要！）** |

## 可用資源 (Available Resources)

使用 `ReadMcpResourceTool` 查詢：
| URI | 用途 |
|-----|------|
| `unity://scenes_hierarchy` | 取得場景階層結構（所有 GameObject）|
| `unity://assets` | 搜尋 AssetDatabase 中的資產 |
| `unity://packages` | 取得已安裝的 Package 列表 |
| `unity://menu-items` | 取得可執行的選單項目 |
| `unity://tests/` | 取得測試列表 |
| `unity://logs/` | 取得 Console 日誌 |

## 工作流程範例 (Workflow Examples)

### 範例 1：建立 UI 介面

```
1. 先查詢場景階層，確認是否已有 Canvas
   → ReadMcpResourceTool(uri: "unity://scenes_hierarchy")

2. 如果沒有 Canvas，先建立
   → create_canvas(objectPath: "MainCanvas", renderMode: "ScreenSpaceOverlay")

3. 建立 UI 元素
   → create_ui_element(objectPath: "MainCanvas/Panel", elementType: "Panel")
   → create_ui_element(objectPath: "MainCanvas/Panel/Title", elementType: "TextMeshPro",
       elementData: { text: "Welcome", fontSize: 36 })
   → create_ui_element(objectPath: "MainCanvas/Panel/StartButton", elementType: "Button",
       elementData: { text: "Start Game" })

4. 調整 Layout
   → add_layout_component(objectPath: "MainCanvas/Panel", layoutType: "VerticalLayoutGroup",
       layoutData: { spacing: 20, childAlignment: "MiddleCenter" })
```

### 範例 2：建立帶 Material 的物件

```
1. 建立 Material
   → create_material(name: "RedMaterial", savePath: "Assets/Materials/RedMaterial.mat",
       color: { r: 1, g: 0, b: 0 })

2. 建立 GameObject（使用 Primitive）
   → execute_menu_item(menuPath: "GameObject/3D Object/Cube")

3. 指派 Material
   → assign_material(objectPath: "Cube", materialPath: "Assets/Materials/RedMaterial.mat")

4. 調整 Transform
   → set_transform(objectPath: "Cube", position: { x: 0, y: 1, z: 0 }, scale: { x: 2, y: 2, z: 2 })
```

### 範例 3：批量建立物件（高效能）

```
使用 batch_execute 一次執行多個操作：
→ batch_execute(operations: [
    { tool: "update_gameobject", params: { objectPath: "Enemy1", gameObjectData: { tag: "Enemy" } } },
    { tool: "update_gameobject", params: { objectPath: "Enemy2", gameObjectData: { tag: "Enemy" } } },
    { tool: "update_gameobject", params: { objectPath: "Enemy3", gameObjectData: { tag: "Enemy" } } }
  ])
```

## 最佳實踐 (Best Practices)

### 1. 先查詢再操作
在修改場景前，先用 `get_scenes_hierarchy` 或 `get_gameobject` 了解現有結構。

### 2. 使用 batch_execute 提升效能
當需要執行多個獨立操作時，使用 `batch_execute` 減少往返延遲。
- 最多 100 個操作
- 支援 `atomic: true` 回滾（使用 Unity Undo）
- 效能提升 10-100 倍

### 3. 路徑格式
- **GameObject 路徑**：`Parent/Child/Grandchild`
- **Asset 路徑**：`Assets/Folder/File.extension`
- Asset 路徑必須以 `Assets/` 開頭

### 4. UI 建立順序
1. Canvas（必須先有）
2. Panel / Container
3. UI 元素（Button、Text 等）
4. Layout 組件

### 5. Material 建立
- 自動偵測 Render Pipeline（URP 用 `Universal Render Pipeline/Lit`，Built-in 用 `Standard`）
- 使用 `color` 參數快速設定基本顏色

## 工具依賴關係 (Tool Dependencies)

```
create_canvas ──→ create_ui_element ──→ add_layout_component
                         │
                         └──→ set_rect_transform

create_material ──→ assign_material

create_prefab / create_scriptable_object ──→ add_asset_to_scene

update_gameobject ──→ update_component
         │
         ├──→ move_gameobject / rotate_gameobject / scale_gameobject
         │
         └──→ reparent_gameobject
```

## 錯誤處理 (Error Handling)

常見錯誤與解決方案：
| 錯誤 | 原因 | 解決方案 |
|------|------|---------|
| GameObject not found | 路徑錯誤或物件不存在 | 先用 `get_scenes_hierarchy` 確認路徑 |
| Canvas required | UI 元素沒有 Canvas | 先建立 Canvas |
| Material not found | Asset 路徑錯誤 | 確認路徑以 `Assets/` 開頭且包含 `.mat` |
| Type not found | ScriptableObject 類別不存在 | 確認類別名稱正確，先 `recompile_scripts` |

## 禁止事項 (Don'ts)

1. ❌ 不要在沒有 Canvas 的情況下建立 UI 元素
2. ❌ 不要忘記 Asset 路徑的 `Assets/` 前綴
3. ❌ 不要一次執行大量獨立操作而不使用 `batch_execute`
4. ❌ 不要假設物件存在，先查詢確認
5. ❌ 不要忽略 `get_gameobject` 回傳的 instanceId，它比路徑更可靠
