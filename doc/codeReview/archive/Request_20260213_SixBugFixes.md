# Code Review Request: 六項問題修復

**日期**: 2026-02-13
**功能**: MCP 工具六項 Bug 修復（場景引用、RectTransform、命名空間解析、TMP alpha、重複元件、深度限制）
**作者**: Claude Opus 4.6

---

## 1. 背景與目標

### 背景
使用者在業務環境中以 AI Agent 操作 Unity MCP 時，發現六項影響工作流程的問題。其中「場景物件引用接線失敗」影響最大，迫使使用者改用手動編輯場景 YAML 的方式繞過。

### 目標
針對六項問題進行修復，依優先級排序：
- **P0**: update_component 場景物件引用、TMP 文字 alpha 預設為 0
- **P1**: Canvas 下自動建立 RectTransform、ComponentTypeResolver 命名空間解析、get_gameobject 深度限制
- **P2**: 防止 update_component 重複新增元件

---

## 2. 變更摘要

### 修改檔案

| 檔案 | 增/刪 | 說明 |
|------|-------|------|
| `Editor/Tools/UpdateComponentTool.cs` | +127/-8 | 場景物件引用解析 + 重複元件防禦 |
| `Editor/Resources/GetGameObjectResource.cs` | +52/-13 | GameObjectToJObject 深度控制 |
| `Editor/Utils/ComponentTypeResolver.cs` | +34/-10 | ReflectionTypeLoadException 處理 + 部分命名空間匹配 |
| `Editor/Utils/GameObjectHierarchyCreator.cs` | +7/-0 | Canvas 下自動新增 RectTransform |
| `Editor/Tools/GetGameObjectTool.cs` | +7/-1 | 解析 maxDepth/includeChildren 參數 |
| `Editor/Tools/UGUITools.cs` | +1/-1 | ParseColor 預設值改為 Color.white |
| `Server~/src/tools/getGameObjectTool.ts` | +16/-2 | 新增 maxDepth/includeChildren schema |
| `Server~/src/tools/updateComponentTool.ts` | +1/-1 | componentData description 更新 |

**合計**: 8 檔案, +216/-31

---

## 3. 關鍵代碼

### 修復 #1: 場景物件引用（P0, UpdateComponentTool.cs）

`ConvertJTokenToValue()` 新增兩個處理分支，在既有 `UnityEngine.Object` + `JTokenType.String`（資產路徑）之前攔截：

**分支 A — 整數值 → Instance ID 查找**
```csharp
// token 為整數時，視為場景物件的 Instance ID
if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Integer)
{
    int id = token.ToObject<int>();
    UnityEngine.Object obj = EditorUtility.InstanceIDToObject(id);
    // → 依 targetType 回傳 GameObject / GetComponent(targetType)
}
```

**分支 B — 結構化物件 → instanceId 或 objectPath 查找**
```csharp
// token 為 JObject 且包含 "instanceId" 或 "objectPath"
if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Object)
{
    JObject refObj = (JObject)token;
    if (refObj.ContainsKey("instanceId") || refObj.ContainsKey("objectPath"))
    {
        // → EditorUtility.InstanceIDToObject() 或 GameObject.Find() + 多場景搜尋
    }
}
```

**設計決策**: 使用 `ContainsKey("instanceId") || ContainsKey("objectPath")` 來區分場景引用 JObject 與 Vector/Color 等既有 JObject 型別，避免誤判。

### 修復 #2: Canvas 下自動建立 RectTransform（P1, GameObjectHierarchyCreator.cs）

```csharp
// SetParent 之後
if (currentParent.GetComponentInParent<Canvas>() != null
    && newObj.GetComponent<RectTransform>() == null)
{
    Undo.AddComponent<RectTransform>(newObj);
}
```

### 修復 #3: ComponentTypeResolver 改善（P1, ComponentTypeResolver.cs）

```csharp
catch (ReflectionTypeLoadException ex)
{
    // 部分型別載入失敗，仍檢查已載入的型別
    foreach (Type t in ex.Types)
    {
        if (t == null || !typeof(Component).IsAssignableFrom(t))
            continue;
        if (t.Name == componentName || t.FullName == componentName)
            return t;
        // 部分命名空間尾端匹配
        if (hasNamespaceSeparator && t.FullName != null && t.FullName.EndsWith("." + componentName))
            return t;
    }
}
```

### 修復 #4: TMP 文字 alpha（P0, UGUITools.cs）

```csharp
// 修改前：ParseColor(colorObj)          → default(Color).a == 0
// 修改後：ParseColor(colorObj, Color.white) → Color.white.a == 1
```

### 修復 #5: 重複元件防禦（P2, UpdateComponentTool.cs）

```csharp
// 改用 GetComponents (plural) + FirstOrDefault() 取代 GetComponent (singular)
Component component = gameObject.GetComponents(componentType).FirstOrDefault();
// ...
if (component == null && componentType != null)
{
    // 新增前再次防禦性檢查
    var existing = gameObject.GetComponents(componentType);
    if (existing.Length > 0)
        component = existing[0];
    else
        component = Undo.AddComponent(gameObject, componentType);
}
```

### 修復 #6: get_gameobject 深度限制（P1, GetGameObjectResource.cs）

```csharp
public static JObject GameObjectToJObject(
    GameObject gameObject,
    bool includeDetailedComponents,
    int maxDepth = -1,        // 新增：-1 = 無限
    int currentDepth = 0,     // 新增：內部遞迴計數
    bool includeChildren = true)  // 新增：是否包含子物件
{
    // 深度未超限 → 完整遞迴
    if (includeChildren && (maxDepth < 0 || currentDepth < maxDepth)) { ... }
    // 深度超限但有子物件 → 僅輸出 childSummary
    else if (gameObject.transform.childCount > 0) { ... }
}
```

---

## 4. 自我評估

### 已知脆弱點

| # | 位置 | 風險 | 說明 |
|---|------|------|------|
| 1 | `UpdateComponentTool.cs` — objectPath 場景搜尋 | **中** | 使用 `SceneManager.sceneCount` 遍歷所有已載入場景，若場景數量多且物件層級深，可能有效能問題。但考量 MCP 工具非即時呼叫，應可接受。 |
| 2 | `UpdateComponentTool.cs` — Integer token 攔截 | **低-中** | 若目標欄位型別為 `UnityEngine.Object` 子類（如 `Sprite`）且值恰好是整數，會被誤解為 Instance ID 而非整數值。但 `Sprite` 等資產型別通常不會傳整數，且既有 string 路徑已覆蓋常見情境。 |
| 3 | `ComponentTypeResolver.cs` — EndsWith 匹配 | **低** | `FullName.EndsWith("." + componentName)` 可能在極端情況下匹配到非預期的型別（如 `A.B.Foo` vs `X.B.Foo`），但先遍歷所有 assembly 且返回第一個匹配的行為與既有邏輯一致。 |
| 4 | `GameObjectHierarchyCreator.cs` — Canvas 偵測 | **低** | `GetComponentInParent<Canvas>()` 會向上搜尋整條 parent chain，若 Canvas 巢狀過深可能稍慢。但 UI 層級通常不會太深。 |
| 5 | `GetGameObjectResource.cs` — childSummary vs children 並存 | **低** | 深度超限時同時輸出空的 `children` 陣列和 `childSummary`，可能讓消費者困惑。但空陣列可保持 API 結構一致性。 |
| 6 | `UpdateComponentTool.cs` — 重複元件防禦 | **低** | 雙重 `GetComponents` 呼叫在正常情況下是多餘的，但在 batch 執行的競態條件下能提供安全保障。效能代價極小。 |

### Edge Cases

| 場景 | 預期行為 | 是否已處理 |
|------|----------|-----------|
| Instance ID 為 0 或無效值 | `InstanceIDToObject` 回傳 null → 輸出警告並回傳 null | ✅ |
| objectPath 含多場景同名物件 | 回傳第一個找到的物件（依場景載入順序） | ✅（與 `GameObject.Find` 行為一致） |
| objectPath 指向未載入場景的物件 | `scene.isLoaded` 檢查跳過 → 回傳 null + 警告 | ✅ |
| `maxDepth: 0` | 只回傳目標物件本身，子物件以 childSummary 呈現 | ✅ |
| `maxDepth: -1`（預設）| 完整遞迴，與原行為一致 | ✅ |
| `includeChildren: false` | 同 maxDepth=0 的行為 | ✅ |
| Canvas 本身作為 root 建立 | `currentParent == null` → 跳過 Canvas 檢查 | ✅ |
| ReflectionTypeLoadException 中 `ex.Types` 含 null 元素 | 已加入 `t == null` 檢查 | ✅ |
| 元件欄位型別為非 UnityEngine.Object 的整數 | 整數攔截僅在 `typeof(UnityEngine.Object).IsAssignableFrom(targetType)` 成立時觸發 | ✅ |

---

## 5. 審查重點

請 Reviewer 特別關注以下項目：

### 高優先
1. **場景引用解析的 JObject 判斷邏輯**（`UpdateComponentTool.cs`）
   → `ContainsKey("instanceId") || ContainsKey("objectPath")` 是否足以區分場景引用與其他 JObject 型別（Vector、Color 等）？是否有遺漏的型別？

2. **Integer token 對 UnityEngine.Object 子類的攔截**
   → 是否存在合理場景中，用戶會希望傳入整數值作為 `UnityEngine.Object` 欄位的非 Instance ID 值？

### 中優先
3. **GameObjectToJObject 簽名變更的向後相容性**
   → 新增參數都有預設值，但是否有其他 caller 需要更新？（已確認 `GetGameObjectResource.Fetch()` 使用預設值，`GetGameObjectTool.Execute()` 傳入新參數。）

4. **ComponentTypeResolver 的 `EndsWith` 匹配精度**
   → 在大型專案中是否可能誤匹配？是否需要更嚴格的匹配策略？

### 低優先
5. **重複元件防禦的雙重 GetComponents 呼叫**
   → 是否過度防禦？是否應簡化為單次檢查？

---

## 6. 文檔一致性檢查

| 項目 | 狀態 | 說明 |
|------|------|------|
| `CLAUDE.md` | ✅ 無需更新 | 無新增工具，現有指引仍適用 |
| `README.md` | ⚠️ 可考慮更新 | `get_gameobject` 工具說明可加入 `maxDepth`/`includeChildren` 參數；但 README 中工具列表僅為簡介，不含參數細節，暫不更新 |
| `README-ja.md` / `README_zh-CN.md` | ⚠️ 同上 | 與 `README.md` 同步考量 |
| `AGENTS.md` | ✅ 無需更新 | 工具名稱未變更 |
| TS tool description | ✅ 已更新 | `updateComponentTool.ts` 的 `componentData` description 已加入場景引用說明 |
| C# tool description | ✅ 已更新 | `UpdateComponentTool.cs` 的 Description 已加入 "Prefer passing componentData in the same call" 提示 |
| SKILL.md 系列 | ✅ 無需更新 | 工具用法未根本改變，僅擴充功能 |

---

## 7. 測試狀態

| 測試類型 | 狀態 | 說明 |
|----------|------|------|
| TypeScript 編譯 | ✅ 通過 | `npm run build` 無錯誤 |
| Jest 單元測試 | ✅ 通過 | 96/96 測試通過 |
| Unity 編譯 | ⏳ 待驗證 | 需開啟 Unity 專案確認無編譯錯誤 |
| 手動整合測試 | ⏳ 待驗證 | 見下方建議測試步驟 |

### 建議手動測試步驟

1. **#1 場景引用**: `update_component` 將場景內 Transform 引用接到另一個物件 → 確認 Inspector 顯示正確引用（非 `None`）
2. **#2 RectTransform**: `update_gameobject` 在 Canvas 下建立新物件 → 確認有 RectTransform 元件
3. **#3 命名空間**: `update_component` 傳入短類別名（如 `PuzzleBattleUI`）→ 確認找到元件
4. **#4 TMP alpha**: `create_ui_element` 建立 TextMeshPro，僅指定 `{r:1, g:1, b:1}` → 確認文字可見
5. **#5 重複元件**: 連續兩次 `update_component`（第一次無 data，第二次有 data）→ 確認只有一個元件
6. **#6 深度限制**: `get_gameobject` 查詢大型層級，傳入 `maxDepth: 1` → 確認輸出精簡含 childSummary
