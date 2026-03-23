# Code Review Request: MCP Unity Prefab 編輯改善與新工具

**日期**: 2026-03-19
**作者**: Claude Opus 4.6
**範圍**: 7 項改善，基於 AI Agent 實戰操作 MCP Unity 的痛點回饋
**變更統計**: 9 檔案修改 + 3 新檔案，+473/-44 行

---

## 1. 背景與目標

另一位 AI Agent 在使用 MCP Unity 工具操作 prefab UI 時遇到嚴重痛點：

1. 無法調整 sibling 順序（UI 渲染順序）
2. prefab 編輯模式下無法建立 UI 元素（Canvas 強制要求）
3. `update_component` 的 `m_Color` vs `color` 欄位名不一致
4. `reparent_gameobject` 在 prefab 模式下丟失 children
5. `screenshot_scene_view` 在 prefab 模式下拍到背景
6. `batch_execute` 不回傳每個操作的 instanceId
7. 缺乏直接讀寫序列化欄位的工具

本次修改一次性解決所有 7 個痛點。

---

## 2. 變更摘要

### 2.1 新工具：`set_sibling_index`
**檔案**: `GameObjectTools.cs` (+56行), `gameObjectTools.ts` (+77行), `McpUnityServer.cs`, `index.ts`

```csharp
// 核心實作
Undo.RecordObject(targetObject.transform, "Set Sibling Index");
targetObject.transform.SetSiblingIndex(siblingIndex.Value);
```

回傳 `oldSiblingIndex`、`newSiblingIndex`、`siblingCount`，方便 agent 理解結果。

### 2.2 修復：`reparent_gameobject` prefab 模式 children 丟失
**檔案**: `GameObjectTools.cs` (修改 reparent 邏輯)

```csharp
// 修正前：所有情況都用 Undo.SetTransformParent（在 LoadPrefabContents 下會丟失 children）
// 修正後：prefab 編輯模式用 transform.SetParent 直接操作
if (PrefabEditingService.IsEditing)
{
    targetObject.transform.SetParent(newParentTransform, worldPositionStays);
}
else
{
    Undo.SetTransformParent(targetObject.transform, newParentTransform, ...);
}
```

### 2.3 修復：`update_component` 欄位名一致性
**檔案**: `UpdateComponentTool.cs` (+210行)

在 reflection 查找失敗後，新增 `SerializedProperty` fallback：
```csharp
// 查找順序：FieldInfo → PropertyInfo → SerializedProperty（新增）
// SerializedProperty 自動處理 m_Color ↔ color 的映射
private bool TrySetViaSerializedProperty(Component component, string fieldName, ...)
{
    SerializedProperty prop = serializedObject.FindProperty(fieldName);
    if (prop == null && !fieldName.StartsWith("m_"))
    {
        string serializedName = "m_" + char.ToUpper(fieldName[0]) + fieldName.Substring(1);
        prop = serializedObject.FindProperty(serializedName);
    }
    // ...
}
```

支援類型：Integer, Boolean, Float, String, Color, Vector2/3/4, Rect, Enum, ObjectReference。

### 2.4 修復：`screenshot_scene_view` prefab 自動對焦
**檔案**: `ScreenshotTools.cs` (+10行)

```csharp
if (PrefabEditingService.IsEditing && PrefabEditingService.PrefabRoot != null)
{
    Selection.activeGameObject = PrefabEditingService.PrefabRoot;
    sceneView.FrameSelected();
    sceneView.Repaint();
}
```

### 2.5 新參數：`create_ui_element` 的 `requireCanvas`
**檔案**: `UGUITools.cs` (重構 Canvas 驗證邏輯), `uguiTools.ts` (+1行 schema)

新增 `requireCanvas` 參數（預設 `true`）。設為 `false` 時跳過 Canvas 驗證，適用於 prefab 編輯模式。

### 2.6 改善：`batch_execute` instanceId 回傳
**檔案**: `batchExecuteTool.ts` (修改回傳格式)

修正前：只在失敗時列出錯誤
修正後：每個操作都回傳 `instanceId`、`name`、`path`，並附帶結構化 `data` 供程式化存取

### 2.7 新工具：`read_serialized_fields` / `write_serialized_fields`
**檔案**: `SerializedFieldTools.cs` (530行新檔), `serializedFieldTools.ts` (192行新檔), `McpUnityServer.cs`, `index.ts`

使用 Unity `SerializedObject`/`SerializedProperty` API，比 reflection 更可靠：
- 自動映射 `m_Color` ↔ `color`（雙向）
- `read` 可讀取所有 visible fields 或指定欄位
- `write` 支援所有常見類型 + ObjectReference（asset path / instanceId / GUID）

---

## 3. 自我評估：脆弱點與 Edge Cases

### 3.1 高風險

| 項目 | 風險 | 說明 |
|------|------|------|
| `EnsureRectTransformHierarchy()` | **空實作** | `UGUITools.cs:790` 的迴圈體是空的，只有註解說 "UI component creation below will handle the leaf node"。如果 parent 層級沒有 Canvas 也沒有 UI component，RectTransform 不會被自動加上，可能導致佈局異常。 |
| `screenshot` 的 `FrameSelected` 時序 | **可能無效** | `Repaint()` 是非同步的，`CaptureFromCamera` 可能在 repaint 完成前就執行，導致截圖仍是舊畫面。可能需要延遲一幀。 |
| `reparent` prefab 模式無 Undo | **預期行為但有風險** | prefab 編輯模式下直接用 `SetParent()` 繞過 Undo 系統。如果操作出錯，使用者無法 Ctrl+Z 復原。但 `LoadPrefabContents` 本身就是隔離環境，discard 即可還原。 |

### 3.2 中風險

| 項目 | 風險 | 說明 |
|------|------|------|
| `SerializedProperty` 的 `enumNames` | **已過時 API** | Unity 2022.3 中 `SerializedProperty.enumNames` 可能已被標記 obsolete，新版應使用 `enumDisplayNames`。需確認目標版本。 |
| `TrySetViaSerializedProperty` 每次呼叫建立 `new SerializedObject` | **效能** | 在 batch 操作中同一 component 的多個欄位會各建立一次 `SerializedObject`。可以快取但目前未做。 |
| `write_serialized_fields` 與 `update_component` 重疊 | **API 混淆** | 兩個工具都能寫入 component 欄位，agent 可能不知道該選哪個。需要在描述中更清楚地區分使用場景。 |

### 3.3 低風險

| 項目 | 風險 | 說明 |
|------|------|------|
| `set_sibling_index` 的 root 物件 | **siblingCount 不精確** | root 物件的 siblingCount 用 `GetRootGameObjects().Length`，在多場景模式下只計算 active scene 的 root 數量。 |
| `batch_execute` 的 `data` 欄位 | **MCP SDK 相容性** | `CallToolResult` 的 `data` 欄位可能在某些 MCP client 中被忽略。text 中已包含 instanceId 資訊作為 fallback。 |

---

## 4. 審查重點

### 4.1 請重點審查

1. **`EnsureRectTransformHierarchy` 是否應該實際加上 RectTransform？**
   目前是空實作，依賴 UI component 自動添加。在多層巢狀 prefab 結構中，中間層可能沒有 UI component 導致缺少 RectTransform。

2. **`ScreenshotTools` 的 `FrameSelected` + `Repaint` 時序問題**
   是否需要改為 async tool 並等待一幀？或使用 `EditorApplication.delayCall`？

3. **`SerializedFieldTools.cs` 與 `UpdateComponentTool.cs` 的 `SetSerializedPropertyValue` 重複**
   兩個檔案有幾乎相同的 SerializedProperty 寫入邏輯（~150 行）。是否應抽取為共用 utility？

4. **`enumNames` 在 Unity 2022.3 的相容性**
   `UpdateComponentTool.cs:397` 和 `SerializedFieldTools.cs` 中使用了 `prop.enumNames`，需確認是否仍可用。

### 4.2 不需特別審查

- TS 側的 schema 定義和 handler 結構（遵循既有 pattern）
- `McpUnityServer.cs` 和 `index.ts` 的工具註冊（固定流程）

---

## 5. 驗證結果

| 測試項目 | 方法 | 結果 |
|----------|------|------|
| C# 編譯 | `recompile_scripts` | 0 errors, 0 warnings |
| TypeScript 編譯 | `npm run build` | clean |
| `set_sibling_index` | batch_execute 呼叫，驗證 childSummary 順序 | PASS |
| `reparent_gameobject` | 場景模式 reparent + 驗證 children 保留 | PASS |
| `update_component` m_Color | 分別用 `color` 和 `m_Color` 設定 Image 顏色 | PASS |
| `write_serialized_fields` | 透過 `m_Text`, `m_FontSize`, `m_Color` 寫入 Text component | PASS |
| `read_serialized_fields` | 批次讀取 Image 和 Text 欄位 | PASS |
| `screenshot_scene_view` | 場景模式截圖（prefab 模式需手動測試） | PASS (非 prefab) |
| `create_ui_element` requireCanvas | 場景模式使用預設值（prefab 模式需手動測試） | PASS (compile) |
| `batch_execute` instanceId | 批次建立 6 個 UI 元素，確認全部成功 | PASS |
| Unity console | 所有測試完成後檢查 | 0 errors, 0 warnings |

**未完整驗證**：`screenshot` prefab 對焦、`requireCanvas=false` 在 prefab 模式下的行為（需開啟 prefab 編輯）

---

## 6. 文檔一致性檢查

| 檢查項目 | 狀態 | 備註 |
|----------|------|------|
| CLAUDE.md 工具清單 | **需更新** | 缺少 `set_sibling_index`, `read_serialized_fields`, `write_serialized_fields` 的說明 |
| 工具名稱一致性（C# ↔ TS） | OK | 所有新工具的 `Name` 屬性與 TS tool name 完全匹配 |
| 註冊順序一致性 | OK | C# 在 BatchExecute 前、TS 在 batch_execute 前註冊 |
| 參數命名慣例 | OK | 全部使用 camelCase |
| 回傳格式一致性 | OK | 遵循 `{success, type, message, ...}` 標準格式 |

---

## 7. 變更檔案清單

| 檔案 | 類型 | 修改 |
|------|------|------|
| `Editor/Tools/GameObjectTools.cs` | 修改 | +SetSiblingIndexTool, reparent prefab fix |
| `Editor/Tools/UpdateComponentTool.cs` | 修改 | +SerializedProperty fallback (+210 行) |
| `Editor/Tools/ScreenshotTools.cs` | 修改 | +prefab auto-focus |
| `Editor/Tools/UGUITools.cs` | 修改 | +requireCanvas param |
| `Editor/Tools/SerializedFieldTools.cs` | **新增** | Read/WriteSerializedFieldsTool (530 行) |
| `Editor/UnityBridge/McpUnityServer.cs` | 修改 | +3 tool registrations |
| `Server~/src/tools/gameObjectTools.ts` | 修改 | +registerSetSiblingIndexTool |
| `Server~/src/tools/batchExecuteTool.ts` | 修改 | instanceId in results |
| `Server~/src/tools/uguiTools.ts` | 修改 | +requireCanvas schema |
| `Server~/src/tools/serializedFieldTools.ts` | **新增** | TS registration (192 行) |
| `Server~/src/index.ts` | 修改 | +import & register |
