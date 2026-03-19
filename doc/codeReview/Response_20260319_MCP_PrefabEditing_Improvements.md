# Code Review Response: MCP Unity Prefab 編輯改善與新工具

**日期**: 2026-03-19
**對應 Request**: `doc/codeReview/Request_20260319_MCP_PrefabEditing_Improvements.md`
**Reviewer**: Claude Opus 4.6

---

## 1. 代碼質素 (Code Quality)

整體設計方向正確，7 個痛點的修復策略都合理。新增的 `set_sibling_index` 實作乾淨、`reparent_gameobject` 的 prefab 模式修復精準、`batch_execute` 的 instanceId 回傳大幅提升 agent 的後續操作能力。

但存在一個 **空實作（no-op）**、一個**非同步時序問題**、以及約 150 行的**重複邏輯**尚未收斂，需要在合併前處理。

---

## 2. 優點 (Pros)

1. **`set_sibling_index`** 實作完整 — Undo 支援、回傳 old/new/count 三組數據方便 agent 理解結果，TS 端 schema 的 `-1 or large value = last` 文件也很貼心。
2. **`reparent_gameobject` prefab 修復** 精準定位問題根因（`Undo.SetTransformParent` 在 `LoadPrefabContents` 隔離環境下丟失 children），prefab/非 prefab 分支切換清晰。
3. **`update_component` SerializedProperty fallback** 優雅解決 `m_Color` vs `color` 命名不一致問題，三層查找（FieldInfo → PropertyInfo → SerializedProperty）的 fallback chain 設計合理。
4. **`read/write_serialized_fields`** 的 `FindSerializedProperty` 雙向映射（加 `m_` / 去 `m_`）完善，`WriteSerializedFieldsTool` 正確地將 `SerializedObject` 建立一次、最後統一 `ApplyModifiedProperties()`。
5. **`batch_execute`** 在 text 中已包含 instanceId fallback，不依賴非標準 `data` 欄位，向下相容處理得當。
6. **自我評估（Section 3）** 品質極高 — 所有高風險項均被準確識別，節省了 reviewer 大量時間。

---

## 3. 缺點與風險 (Cons & Risks)

### [P0] `EnsureRectTransformHierarchy` 是完全的空實作

- **位置**: `Editor/Tools/UGUITools.cs:787-801`
- **問題**: 迴圈體為空，整個方法是 no-op。在 `requireCanvas=false`（prefab 編輯模式）下，如果 parent chain 上的 GameObject 只有 `Transform` 而非 `RectTransform`，UI element 佈局計算會異常。Unity 的 RectTransform layout 系統要求整條 parent chain 都是 RectTransform。
- **影響**: prefab 中多層巢狀結構（例如 `PrefabRoot/Panel/SubPanel/Button`）中，中間層若無任何 UI component，`SubPanel` 不會自動獲得 RectTransform，導致 `Button` 定位/尺寸計算錯誤。
- **Request 自我評估**: 已識別（3.1 高風險表第一項），但標為「空實作」而非 P0。

### [P1] Screenshot `FrameSelected` + `Repaint` 時序問題

- **位置**: `Editor/Tools/ScreenshotTools.cs:117-123`
- **問題**: `sceneView.Repaint()` 是非同步操作（排入 EditorApplication repaint queue），但 `CaptureFromCamera` 在同一個同步 `Execute()` 中立即執行。截圖時 SceneView 的渲染緩衝區可能仍是舊畫面。
- **影響**: prefab 模式下截圖可能仍顯示未對焦到 prefab root 的舊視角。
- **Request 自我評估**: 已識別（3.1 高風險表第二項）。

### [P1] `SetSerializedPropertyValue` 與 `FindSerializedProperty` 大量重複

- **位置**:
  - `UpdateComponentTool.cs:310-336` (`FindSerializedProperty` 等效邏輯) + `341-507` (`SetSerializedPropertyValue`)
  - `SerializedFieldTools.cs:103-126` (`FindSerializedProperty` in Read) + `301-321` (`FindSerializedProperty` in Write) + `323-528` (`SetSerializedPropertyValue`)
- **問題**:
  - `SetSerializedPropertyValue` 在兩個檔案中有 ~150 行近乎相同的邏輯
  - `FindSerializedProperty` 出現三次（Read/Write 各一次 + UpdateComponent 的 `TrySetViaSerializedProperty` 一次）
  - 已存在微妙差異：`WriteSerializedFieldsTool` 版本支援 `JTokenType.Null` 清除 ObjectReference（line 478-482），`UpdateComponentTool` 版本不支援
  - 結構化引用的 key 不一致：Write 用 `assetPath`，Update 用 `objectPath`
- **影響**: 修 bug 需改多處，且差異會隨時間擴大。

### [P1] `prop.enumNames` 在 Unity 2022.3 已標記 Obsolete

- **位置**: `UpdateComponentTool.cs:422`, `SerializedFieldTools.cs:159`, `SerializedFieldTools.cs:407`
- **問題**: 專案最低版本要求 Unity 2022.3，`SerializedProperty.enumNames` 在此版本已產生 obsolete warning。
- **影響**: 每次編譯產生 warning，且未來版本可能移除。

### [P2] `UpdateComponentTool.TrySetViaSerializedProperty` 每次呼叫建立新 `SerializedObject`

- **位置**: `UpdateComponentTool.cs:312`
- **問題**: 在 `UpdateComponentData` 的 field 迴圈中，每個無法用 reflection 設定的 field 都觸發 `TrySetViaSerializedProperty`，各自 `new SerializedObject(component)` + `ApplyModifiedProperties()`。相比之下，`WriteSerializedFieldsTool` 正確地建立一次、最後統一 apply。
- **影響**: 在 batch 操作中同一 component 多欄位更新時有效能開銷。

### [P2] `write_serialized_fields` vs `update_component` 職責邊界不清

- **位置**: TS 描述文字 (`serializedFieldTools.ts:103-106`, `updateComponentTool.ts`)
- **問題**: 兩個工具都能寫入 component 欄位，agent 難以選擇。目前描述中未充分區分使用場景。
- **影響**: agent 可能選擇錯誤工具，例如用 `write_serialized_fields` 嘗試 AddComponent（不支援），或用 `update_component` 嘗試寫 `m_Sprite`（reflection 找不到，需 fallback）。

### [P2] `reparent_gameobject` prefab 模式 `worldPositionStays=false` 語義偏差

- **位置**: `GameObjectTools.cs:424-429`
- **問題**: `SetParent(transform, false)` 的原生 Unity 行為是「保持 local transform 不變」。但 code 額外強制 `localPosition = zero, localRotation = identity, localScale = one`，這是「歸零」而非 Unity 原生 `worldPositionStays=false` 的語義。
- **影響**: 與 Unity 開發者對 `worldPositionStays` 的預期不同。非 prefab 模式（line 436-440）也有同樣的歸零邏輯，但因有 Undo 所以影響較小。

### [P3] `batch_execute` 的 `data` 欄位非 MCP 標準

- **位置**: `batchExecuteTool.ts:183-186`
- **問題**: `CallToolResult` 標準只定義 `content` 和 `isError`，`data` 是自定義欄位。TypeScript strict mode 下可能有型別錯誤。
- **影響**: 低 — text 中已有 instanceId fallback，功能不受影響。

### [P3] `set_sibling_index` root 物件的 `siblingCount` 在多場景下不精確

- **位置**: `GameObjectTools.cs:495-497`
- **問題**: 用 `GetActiveScene().GetRootGameObjects().Length`，multi-scene editing 下只計算 active scene。
- **影響**: 極低 — sibling index 設定本身正確，只是回傳的參考數據不精確。

### 自我評估檢查補充

Request Section 3 的自我評估已涵蓋所有主要風險點。額外未覆蓋的有：
- `SetSerializedPropertyValue` 兩版本間 ObjectReference `Null` 處理差異
- 結構化引用 key 不一致（`assetPath` vs `objectPath`）
- `worldPositionStays=false` 歸零語義偏差

---

## 4. 改善建議 (Improvement Suggestions)

### 建議 A: 實作 `EnsureRectTransformHierarchy`

```csharp
// UGUITools.cs
private void EnsureRectTransformHierarchy(GameObject go)
{
    // Walk up the hierarchy and ensure RectTransform exists on each level
    // Collect nodes that need RectTransform (bottom-up), then add top-down
    var needsRect = new List<GameObject>();
    Transform current = go.transform;
    while (current != null)
    {
        if (current.GetComponent<RectTransform>() == null)
        {
            needsRect.Add(current.gameObject);
        }
        current = current.parent;
    }

    // Add RectTransform top-down (parent first) to ensure hierarchy consistency
    needsRect.Reverse();
    foreach (var obj in needsRect)
    {
        // Adding RectTransform to a GameObject that already has Transform
        // will replace the Transform component automatically in Unity
        Undo.AddComponent<RectTransform>(obj);
    }
}
```

### 建議 B: Screenshot 改為 async + 延遲一幀

```csharp
// ScreenshotTools.cs — ScreenshotSceneViewTool
public ScreenshotSceneViewTool()
{
    Name = "screenshot_scene_view";
    Description = "...";
    IsAsync = true;  // 改為 async
}

public override async void ExecuteAsync(JObject parameters, Action<JObject> callback)
{
    int width = parameters?["width"]?.ToObject<int>() ?? 960;
    int height = parameters?["height"]?.ToObject<int>() ?? 540;

    SceneView sceneView = SceneView.lastActiveSceneView;
    if (sceneView == null) { callback(/* error */); return; }

    if (PrefabEditingService.IsEditing && PrefabEditingService.PrefabRoot != null)
    {
        Selection.activeGameObject = PrefabEditingService.PrefabRoot;
        sceneView.FrameSelected();
        sceneView.Repaint();
    }

    // Delay one frame to allow Repaint to complete
    EditorApplication.delayCall += () =>
    {
        Camera sceneCamera = sceneView.camera;
        if (sceneCamera == null) { callback(/* error */); return; }
        callback(ScreenshotHelper.CaptureFromCamera(sceneCamera, width, height, "Scene View"));
    };
}
```

### 建議 C: 抽取共用 `SerializedPropertyHelper`

```csharp
// Editor/Utils/SerializedPropertyHelper.cs (新增)
namespace McpUnity.Utils
{
    public static class SerializedPropertyHelper
    {
        /// <summary>
        /// Find a SerializedProperty by name, with bidirectional m_ prefix mapping.
        /// </summary>
        public static SerializedProperty FindProperty(SerializedObject so, string name)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null) return prop;

            if (!name.StartsWith("m_"))
            {
                string serializedName = "m_" + char.ToUpper(name[0]) + name.Substring(1);
                prop = so.FindProperty(serializedName);
                if (prop != null) return prop;
            }

            if (name.StartsWith("m_") && name.Length > 2)
            {
                string withoutPrefix = char.ToLower(name[2]) + name.Substring(3);
                prop = so.FindProperty(withoutPrefix);
                if (prop != null) return prop;
            }

            return null;
        }

        /// <summary>
        /// Set a SerializedProperty value from a JToken.
        /// Supports: Integer, Boolean, Float, String, Color, Vector2/3/4, Rect,
        /// Enum, ObjectReference (asset path/instanceId/GUID/structured), Bounds,
        /// Quaternion, and null clearing for ObjectReference.
        /// </summary>
        public static bool SetValue(SerializedProperty prop, JToken value,
            List<string> warnings, string fieldName)
        {
            // Use WriteSerializedFieldsTool's complete implementation as the single source
            // (includes Null support, Bounds, Quaternion, try-catch)
            // ...
        }
    }
}
```

替換三處呼叫：
- `UpdateComponentTool.TrySetViaSerializedProperty` → 使用 `SerializedPropertyHelper.FindProperty` + `SetValue`
- `ReadSerializedFieldsTool.FindSerializedProperty` → `SerializedPropertyHelper.FindProperty`
- `WriteSerializedFieldsTool.FindSerializedProperty` + `SetSerializedPropertyValue` → `SerializedPropertyHelper`

### 建議 D: `enumNames` 改用非 obsolete API

```csharp
// SerializedPropertyHelper.cs (統一修改一處即可)
case SerializedPropertyType.Enum:
    if (value.Type == JTokenType.String)
    {
        string strValue = value.ToObject<string>();
        // Use enumDisplayNames (non-obsolete) instead of enumNames
        string[] displayNames = prop.enumDisplayNames;
        for (int i = 0; i < displayNames.Length; i++)
        {
            if (string.Equals(displayNames[i], strValue, StringComparison.OrdinalIgnoreCase))
            {
                prop.enumValueIndex = i;
                return true;
            }
        }
        // Also try matching internal enum names via enumValueIndex iteration
        for (int i = 0; i < displayNames.Length; i++)
        {
            prop.enumValueIndex = i;
            if (string.Equals(prop.enumNames[i], strValue, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        warnings?.Add($"Enum value '{strValue}' not found for '{fieldName}'");
    }
    break;
```

> **注意**: `enumDisplayNames` 回傳的是 Inspector 顯示名稱（可能有空格、已翻譯），與 C# enum 名稱不同。建議同時比對 display name 和 internal name，提高匹配率。

### 建議 E: 在 TS description 中明確區分工具職責

```typescript
// updateComponentTool.ts
const toolDescription = `Updates component fields on a GameObject, or adds the component if missing.
Best for: adding new components, setting fields by C# property/field name.
For Unity built-in serialized fields (m_Color, m_Sprite, m_FontSize), prefer write_serialized_fields.`;

// serializedFieldTools.ts
const writeToolDescription = `Writes serialized fields on a component using Unity's SerializedProperty API.
Best for: Unity built-in component fields (m_Color, m_Sprite, etc.) and when exact serialized field control is needed.
Does NOT add missing components — use update_component for that.
Accepts both serialized names (m_Color) and property names (color).`;
```

### 建議 F: 統一結構化 ObjectReference 的 key 名稱

在 `UpdateComponentTool.SetSerializedPropertyValue` 和 `WriteSerializedFieldsTool.SetSerializedPropertyValue` 中，結構化引用應支援相同的 key set：

```csharp
// 兩處都應同時接受 assetPath 和 objectPath
if (refObj.ContainsKey("assetPath"))
{
    string path = refObj["assetPath"].ToObject<string>();
    // ...
}
if (refObj.ContainsKey("objectPath"))
{
    string path = refObj["objectPath"].ToObject<string>();
    var go = GameObject.Find(path);
    // ...
}
```

---

## 5. 修復優先級摘要

| 優先級 | 項目 | 修復成本 | 建議 | 狀態 |
|--------|------|---------|------|------|
| **P0** | `EnsureRectTransformHierarchy` 空實作 | 低（~15 行） | 建議 A | ✅ 已修復（含異議修正） |
| **P1** | Screenshot 時序問題 | 中（改 async） | 建議 B | ✅ 已修復 |
| **P1** | `SetSerializedPropertyValue` 重複 | 中（抽 utility） | 建議 C | ✅ 已修復 |
| **P1** | `enumNames` obsolete | 低（改 API call） | 建議 D | ✅ 已修復（含異議修正） |
| **P2** | `SerializedObject` 效能 | 低（提升變數） | 合併至建議 C | ✅ 已修復 |
| **P2** | 工具職責描述 | 低（改文字） | 建議 E | ✅ 已修復 |
| **P2** | `worldPositionStays` 語義 | 低（確認意圖） | 需討論 | ⏭ 不改行為（設計意圖） |
| **P2** | ObjectReference key 不一致 | 低 | 建議 F | ✅ 已修復 |
| **P3** | `data` 非標準欄位 | 無需修 | text fallback 已足夠 | ⏭ 不修 |
| **P3** | `siblingCount` 多場景 | 無需修 | 不影響功能 | ⏭ 不修 |

---

## 6. Refactoring 執行結果

**執行日期**: 2026-03-19
**執行者**: Claude Opus 4.6

### 與原 Review 建議的異議與調整

1. **建議 A 調整**: `EnsureRectTransformHierarchy` 在 prefab 編輯模式下用 `obj.AddComponent<RectTransform>()` 直接操作，非 prefab 模式才用 `Undo.AddComponent`。原因：與 `reparent_gameobject` 修復一致 — `Undo.*` 在 `LoadPrefabContents` 隔離環境下不可靠。

2. **建議 D 調整**: 不完全替換 `enumNames`，改為先比對 `enumDisplayNames`（非 obsolete），再用 `#pragma warning disable CS0618` 包住 `enumNames` fallback。原因：Unity 無非 obsolete 的 internal enum name API，而 agent 通常傳 C# enum name 而非 display name。

3. **`worldPositionStays` 不改行為**: 歸零語義是刻意設計，MCP agent reparent 時 `worldPositionStays=false` 的意圖是「放到新 parent 的原點」。保持現有行為，未來可在 TS schema description 中補充說明。

### 修改清單

| 檔案 | 修改內容 |
|------|---------|
| `Editor/Utils/SerializedPropertyHelper.cs` | **新增** — 共用 `FindProperty` + `SetValue`，統一支援 `assetPath`/`objectPath`/Null/Bounds/Quaternion |
| `Editor/Utils/SerializedPropertyHelper.cs.meta` | **新增** — Unity meta 檔 |
| `Editor/Tools/UpdateComponentTool.cs` | 移除 ~170 行重複邏輯，改用 `SerializedPropertyHelper`，新增 `Dictionary<Component, SerializedObject>` 快取 |
| `Editor/Tools/SerializedFieldTools.cs` | 移除 Read/Write 兩處 `FindSerializedProperty` + Write 的 `SetSerializedPropertyValue`（~230 行），改用共用 helper |
| `Editor/Tools/UGUITools.cs` | 實作 `EnsureRectTransformHierarchy`，含 prefab 模式判斷 |
| `Editor/Tools/ScreenshotTools.cs` | `ScreenshotSceneViewTool` 改為 `IsAsync=true`，prefab 模式用 `delayCall` 延遲截圖 |
| `Server~/src/tools/updateComponentTool.ts` | 更新 description，區分與 `write_serialized_fields` 的職責 |
| `Server~/src/tools/serializedFieldTools.ts` | 更新 write description，明確不支援 AddComponent，新增 `objectPath` 說明 |

### 驗證結果

| 測試項目 | 方法 | 結果 |
|----------|------|------|
| C# 編譯 | `recompile_scripts` | 0 errors, 0 warnings |
| TypeScript 編譯 | `npm run build` | clean |
| `read_serialized_fields` m_ 映射 | MCP 呼叫 `color`/`m_Color` | PASS |
| `write_serialized_fields` Color + Bool | MCP 寫入 Image color/raycastTarget | PASS |
| `update_component` reflection 路徑 | MCP 用 `color` 設定 Image | PASS |
| `update_component` SerializedProperty fallback | MCP 用 `m_Color` 設定 Image | PASS |
| `screenshot_scene_view` async | MCP 截圖回傳 | PASS |
| `batch_execute` 混合操作 | 2 write + 2 read，4/4 成功 | PASS |
