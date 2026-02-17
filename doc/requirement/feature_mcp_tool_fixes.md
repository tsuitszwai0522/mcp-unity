# Feature Design: MCP 工具修復（陣列序列化 + TMP 複合元件）

> **狀態**：已實作，待手動測試
> **建立日期**：2026-02-17
> **相關模組**：`Editor/Tools/UpdateComponentTool.cs`、`Editor/Tools/CreateScriptableObjectTool.cs`、`Editor/Tools/UGUITools.cs`

## 1. 需求描述

### 問題 A：`update_component` 無法序列化 UnityEngine.Object 陣列/List 參考

**現狀**：

`UpdateComponentTool.ConvertJTokenToValue` 缺少陣列與 `List<T>` 的處理分支。當目標欄位為 `ScriptableObject[]`、`Material[]`、`List<Sprite>` 等類型時，JSON 陣列直接落入 Newtonsoft `token.ToObject(targetType)` fallback，因 Newtonsoft 不懂 Unity asset 系統而失敗。

**單一參考正常運作的原因**：`ConvertJTokenToValue` 已有 `typeof(UnityEngine.Object).IsAssignableFrom(targetType)` + `JTokenType.String` 分支，會走 `AssetDatabase.LoadAssetAtPath` 正確解析。問題純粹是缺少陣列展開邏輯，無法讓每個元素進入此分支。

**影響範圍**：
- `UpdateComponentTool`：所有 `UnityEngine.Object` 子類的 `T[]` 和 `List<T>` 欄位
- `CreateScriptableObjectTool`：`ConvertValue` 有陣列展開邏輯，但元素級別缺少 `AssetDatabase.LoadAssetAtPath` / `InstanceIDToObject` 解析，同樣無法正確處理 Unity asset 參考陣列

**重現範例**：
```json
// update_component 呼叫
{
  "componentName": "RewardConfig",
  "componentData": {
    "rewards": ["Assets/Data/CoinReward.asset", "Assets/Data/GemReward.asset"]
  }
}
// 預期：rewards 欄位設定為 2 個 ScriptableObject 參考
// 實際：Newtonsoft fallback 失敗，rewards 為 null
```

### 問題 B：`create_ui_element` TMP 複合元件缺少子層級

**現狀**：

`CreateDropdownTMP` 和 `CreateInputFieldTMP` 只建立根物件 + TMP 元件，無子層級。元件建立後不可用（無 Label、Arrow、Template、Placeholder 等）。

**對比**：

| 方法 | 子層級 | 狀態 |
|------|--------|------|
| `CreateDropdown`（legacy） | Label + Arrow + Template（含 ScrollRect 完整結構，約 170 行） | 完整 |
| `CreateDropdownTMP` | 僅根物件 + `TMP_Dropdown` 元件（約 30 行） | **缺子層級** |
| `CreateInputField`（legacy） | Text Area → Placeholder + Text，已 wire 內部欄位（約 80 行） | 完整 |
| `CreateInputFieldTMP` | 僅根物件 + `TMP_InputField` 元件（約 30 行） | **缺子層級** |

程式碼中已有註釋承認簡化：
> *"This is complex due to TMP's structure - simplified version. In production, you'd want to instantiate from a prefab."*

**根本原因**：TMP 型別透過反射載入（`Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro")`），子物件的元件建立和欄位 wiring 也需要全部走反射，複雜度顯著提高。

---

## 2. 技術方案

### 問題 A：陣列/List 序列化 — 方案 A-2（共用 Utility）✅ 已選定

#### 概述

建立 `Editor/Utils/SerializedFieldConverter.cs`，提供統一的 `ConvertJTokenToValue(JToken, Type)` 靜態方法，取代 `UpdateComponentTool` 和 `CreateScriptableObjectTool` 各自的私有轉換方法。

#### 統一方法的能力矩陣

| 能力 | UpdateComponentTool 現有 | CreateScriptableObjectTool 現有 | 統一方法 |
|------|:---:|:---:|:---:|
| 基本型別（string, int, float, double, bool） | ✗（靠 fallback） | ✓ | ✓ |
| Vector2/3/4 | ✓ | ✓ | ✓ |
| Quaternion | ✓ | ✗ | ✓ |
| Color | ✓ | ✓ | ✓ |
| Bounds, Rect | ✓ | ✗ | ✓ |
| UnityEngine.Object — asset path | ✓ | ✗ | ✓ |
| UnityEngine.Object — GUID | ✓ | ✗ | ✓ |
| UnityEngine.Object — instance ID（int） | ✓ | ✗ | ✓ |
| UnityEngine.Object — structured ref（{instanceId}/{objectPath}） | ✓ | ✗ | ✓ |
| Enum | ✓ | ✓ | ✓ |
| **Array (T[])** | **✗** | ✓（元素級缺 asset 解析） | **✓（遞迴）** |
| **List\<T\>** | **✗** | **✗** | **✓（遞迴）** |

#### 實作要點

**新檔案**：`Editor/Utils/SerializedFieldConverter.cs`

```csharp
namespace McpUnity.Utils
{
    public static class SerializedFieldConverter
    {
        /// <summary>
        /// 將 JToken 轉換為指定 C# 型別的值。
        /// 支援 Unity 結構型別、UnityEngine.Object 參考（asset path / GUID / instance ID / objectPath）、
        /// 陣列、List<T>、Enum，以及基本型別。
        /// </summary>
        public static object ConvertJTokenToValue(JToken token, Type targetType) { ... }
    }
}
```

- 方法為 `public static`，兩個 Tool 直接呼叫
- 處理順序（與現有 `UpdateComponentTool` 一致）：
  1. null check
  2. Unity struct（Vector2/3/4、Quaternion、Color、Bounds、Rect）
  3. UnityEngine.Object — instance ID（int token）
  4. UnityEngine.Object — structured ref（object token with instanceId/objectPath）
  5. UnityEngine.Object — asset path / GUID（string token）
  6. Enum
  7. **Array（T[]）— 遞迴呼叫自身**（新增）
  8. **List\<T\> — 遞迴呼叫自身**（新增）
  9. Newtonsoft fallback

**陣列處理邏輯**：
```csharp
// T[]
if (targetType.IsArray && token.Type == JTokenType.Array)
{
    var jArray = (JArray)token;
    var elementType = targetType.GetElementType();
    var arr = Array.CreateInstance(elementType, jArray.Count);
    for (int i = 0; i < jArray.Count; i++)
        arr.SetValue(ConvertJTokenToValue(jArray[i], elementType), i);
    return arr;
}

// List<T>
if (targetType.IsGenericType
    && targetType.GetGenericTypeDefinition() == typeof(List<>)
    && token.Type == JTokenType.Array)
{
    var jArray = (JArray)token;
    var elementType = targetType.GetGenericArguments()[0];
    var list = (IList)Activator.CreateInstance(targetType);
    for (int i = 0; i < jArray.Count; i++)
        list.Add(ConvertJTokenToValue(jArray[i], elementType));
    return list;
}
```

因為遞迴呼叫 `ConvertJTokenToValue`，每個元素都能正確走 Unity asset 解析路徑（string → `LoadAssetAtPath`、int → `InstanceIDToObject`）。

**Tool 側變更**：

| 檔案 | 變更 |
|------|------|
| `UpdateComponentTool.cs` | 刪除私有 `ConvertJTokenToValue` 方法（277-533 行），改呼叫 `SerializedFieldConverter.ConvertJTokenToValue` |
| `CreateScriptableObjectTool.cs` | 刪除私有 `ConvertValue` 方法（225-322 行），改呼叫 `SerializedFieldConverter.ConvertJTokenToValue` |

#### 風險評估

- **行為一致性**：統一方法合併兩邊的超集能力，不會減少任何既有功能。`CreateScriptableObjectTool` 反而獲得更多型別支援（Quaternion、Bounds、Rect、UnityEngine.Object 參考）。
- **場景物件引用**：`ConvertJTokenToValue` 中的 structured ref 邏輯用 `GameObject.Find` 和 `SceneManager`，在 `CreateScriptableObjectTool` 場景中不太會用到（SO 通常引用 asset 而非場景物件），但放在統一方法中不會造成問題。
- **測試策略**：以 EditMode test 驗證各型別的轉換正確性。

---

### 問題 B：TMP 複合元件 — 方案 B-2（TMP_DefaultControls）✅ 已選定

#### 概述

透過反射呼叫 `TMPro.TMP_DefaultControls.CreateDropdown(Resources)` 和 `CreateInputField(Resources)` 建立完整子層級，取代現有的 stub 實作。

#### 核心挑戰：GO 替換

現有呼叫流程：
```
Execute() → elementGO = FindOrCreateHierarchicalGameObject(objectPath)
          → CreateDropdownTMP(elementGO, data)    ← 在既有 GO 上加元件
          → 套用 rectTransformParams 到 elementGO
          → 回傳 elementGO 資訊
```

`TMP_DefaultControls.CreateDropdown()` 會建立**全新的 GO 樹**（root + 所有子物件），root 上的 `TMP_Dropdown` 元件已 wire 好子物件參考。我們不能直接使用 `elementGO`，需要將建立出來的結構轉移過去。

#### 實作策略：子物件轉移 + 元件 CopySerialized

```csharp
private void CreateDropdownTMP(GameObject go, JObject data)
{
    // 1. 反射取得 TMP_DefaultControls 和 Resources 型別
    Type defaultControlsType = Type.GetType("TMPro.TMP_DefaultControls, Unity.TextMeshPro");
    if (defaultControlsType == null) { CreateDropdown(go, data); return; }

    Type resourcesType = defaultControlsType.GetNestedType("Resources");
    object resources = Activator.CreateInstance(resourcesType);

    // 2. 呼叫 CreateDropdown 建立完整結構
    var method = defaultControlsType.GetMethod("CreateDropdown", BindingFlags.Public | BindingFlags.Static);
    GameObject tmpGO = (GameObject)method.Invoke(null, new[] { resources });

    // 3. 轉移子物件到 elementGO
    while (tmpGO.transform.childCount > 0)
        tmpGO.transform.GetChild(0).SetParent(go.transform, false);

    // 4. 複製元件（Image + TMP_Dropdown）到 elementGO
    //    使用 EditorUtility.CopySerialized 保留子物件參考
    CopyComponent<Image>(tmpGO, go);
    CopyComponentByType(tmpGO, go, Type.GetType("TMPro.TMP_Dropdown, Unity.TextMeshPro"));

    // 5. 修正 targetGraphic（原指向 tmpGO 上的 Image，需改指向 go 上的 Image）
    FixTargetGraphic(go);

    // 6. 銷毀暫存 GO
    Object.DestroyImmediate(tmpGO);

    // 7. 套用使用者指定的 elementData（options、text、interactable 等）
    ApplyDropdownData(go, data);
}
```

**`EditorUtility.CopySerialized` 的關鍵特性**：
- 複製序列化資料（含物件引用）從 source 到 destination
- 子物件已先轉移到 `go` 下，其 instance ID 不變
- `TMP_Dropdown.captionText`、`template`、`itemText` 等欄位引用的是子物件的 component instance（by ID），reparent 不影響

**需修正的 self-reference**：
- `Selectable.targetGraphic`：原指向 `tmpGO` 的 `Image`（已被 destroy），需改指向 `go` 的 `Image`
- `ScrollRect.viewport`、`ScrollRect.content`：指向子物件，不受影響

#### 兩個 TMP 方法的處理

| 方法 | TMP_DefaultControls 對應方法 | elementData 後處理 |
|------|---------------------------|-------------------|
| `CreateDropdownTMP` | `CreateDropdown(Resources)` | options、value、interactable |
| `CreateInputFieldTMP` | `CreateInputField(Resources)` | text、placeholder、interactable |

#### 共用 Helper 方法

建議在 `UGUITools.cs`（或 `UGUIToolUtils`）新增：

```csharp
/// <summary>
/// 透過反射呼叫 TMP_DefaultControls 的靜態建立方法，
/// 將建立的完整子層級轉移至目標 GO。
/// </summary>
private static bool TryCreateViaTMPDefaultControls(
    string methodName, GameObject targetGO, out GameObject createdRoot)

/// <summary>
/// 複製指定型別的元件從 source GO 到 target GO（使用 EditorUtility.CopySerialized）。
/// </summary>
private static void CopyComponentByType(GameObject source, GameObject target, Type componentType)
```

#### 風險評估

- **`TMP_DefaultControls` 穩定性**：此類在 TMP 中為 `public` 且自 TMP 1.x 起存在，API 穩定。若未來版本移除，fallback 到 legacy 版本（現有行為）。
- **`EditorUtility.CopySerialized` 可靠性**：Unity 官方 API，用於 Prefab/Asset 複製，可靠度高。
- **Undo 支援**：`TMP_DefaultControls` 建立的物件不帶 Undo 記錄，轉移後需補上 `Undo.RegisterCreatedObjectUndo`。
- **測試策略**：在 Unity Editor 中手動測試建立 DropdownTMP / InputFieldTMP，確認結構與手動建立一致。

---

## 3. 已解決的討論事項

- [x] ~~`List<T>` 支援~~：需要支援
- [x] ~~`CreateScriptableObjectTool` 一併修復~~：是，統一到共用 Utility
- [x] ~~TMP 複合元件範圍~~：僅 `DropdownTMP` 和 `InputFieldTMP`，TextMeshPro 本身沒問題
- [x] ~~TMP 最低版本假設~~：維持反射 + fallback 策略
- [x] ~~方案選擇~~：A-2（共用 Utility）+ B-2（TMP_DefaultControls）

## 4. 決策記錄

| 日期 | 決策 | 理由 |
|------|------|------|
| 2026-02-17 | 問題 A 採用 A-2（共用 Utility） | 消除兩個 Tool 之間的重複型別轉換邏輯，建立單一維護點 |
| 2026-02-17 | 問題 B 採用 B-2（TMP_DefaultControls） | 與 Unity Editor 手動建立的結構完全一致、程式碼簡潔、TMP 更新時自動跟進 |

---

## 5. 任務清單

### 問題 A：共用 SerializedFieldConverter

- [x] 建立 `Editor/Utils/SerializedFieldConverter.cs`，合併兩個轉換方法的超集能力
- [x] 新增 `T[]` 陣列支援（遞迴呼叫自身）
- [x] 新增 `List<T>` 支援（遞迴呼叫自身）
- [x] 修改 `UpdateComponentTool.cs`：刪除私有 `ConvertJTokenToValue`，改呼叫 `SerializedFieldConverter`
- [x] 修改 `CreateScriptableObjectTool.cs`：刪除私有 `ConvertValue`，改呼叫 `SerializedFieldConverter`
- [ ] 撰寫 EditMode 測試：驗證陣列/List + Unity asset 參考的轉換

### 問題 B：TMP 複合元件完整建構

- [x] 實作 `TryCreateViaTMPDefaultControls` + `TransferTMPHierarchy` helper
- [x] 重寫 `CreateDropdownTMP`：使用 helper 建立完整結構 + 套用 elementData
- [x] 重寫 `CreateInputFieldTMP`：使用 helper 建立完整結構 + 套用 elementData
- [ ] 手動測試：在 Unity Editor 中驗證建立的 DropdownTMP / InputFieldTMP 結構正確、可操作

### TypeScript 側

- [x] 確認 `Server~/src/tools/updateComponent.ts` 的 schema 不需更新（`z.record(z.any())` 已支援陣列）
- [x] TypeScript 編譯通過
