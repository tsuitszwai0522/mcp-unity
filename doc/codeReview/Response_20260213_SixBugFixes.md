# Code Review Response: 六項問題修復

**日期**: 2026-02-13  
**對應 Request**: `doc/codeReview/Request_20260213_SixBugFixes.md`  
**Reviewer**: Codex (GPT-5)

---

## 1. 代碼質素 (Code Quality)

整體修復方向正確，且改動集中在問題點，沒有出現大範圍耦合擴散。`GetGameObjectResource.GameObjectToJObject()` 的參數擴充採用預設值，向後相容性處理得當。  
但在 `UpdateComponentTool.ConvertJTokenToValue()` 的場景引用分支，仍有兩個邏輯分歧沒有收斂（`instanceId` 優先且不 fallback、`UnityEngine.Object` 基底型別未覆蓋），在邊界情境下會造成實際接線失敗。

---

## 2. 優點 (Pros)

1. `update_component` 的場景引用能力補齊了先前最痛的工作流缺口，方向完全正確。  
2. `get_gameobject` 深度控制（`maxDepth/includeChildren`）落地在 C# + TS 兩側，契約一致。  
3. `ReflectionTypeLoadException` 的處理補強實用，能避免大型專案中單一 Assembly 型別載入失敗拖垮整體解析。  
4. TMP 顏色 alpha 預設修正為可見值（`Color.white`）是正確且低風險的 hotfix。  

---

## 3. 缺點與風險 (Cons & Risks)

### [P1] 結構化場景引用無法寫入 `UnityEngine.Object` 欄位
- **位置**: `Editor/Tools/UpdateComponentTool.cs:440`  
- **問題**: `JObject` 場景引用分支只處理 `GameObject` 與 `Component` 目標型別，若欄位型別是 `UnityEngine.Object`（常見於通用引用欄位），即使已解析到 `resolved`，仍會回傳 `null`。  
- **影響**: 這類欄位使用 `{"instanceId": ...}` 或 `{"objectPath": ...}` 時會接線失敗。  

### [P1] `instanceId` 與 `objectPath` 同時提供時，失敗不會 fallback
- **位置**: `Editor/Tools/UpdateComponentTool.cs:395`, `Editor/Tools/UpdateComponentTool.cs:404`  
- **問題**: 分支為 `if (instanceId) ... else if (objectPath) ...`，只要 `instanceId` key 存在就不再嘗試 `objectPath`。  
- **影響**: 客戶端若採「雙保險」傳兩者（常見做法），一旦 `instanceId` 過期，明明 `objectPath` 可解析仍會失敗。  

### [P2] `EndsWith` 命名空間匹配有誤匹配風險
- **位置**: `Editor/Utils/ComponentTypeResolver.cs:71`, `Editor/Utils/ComponentTypeResolver.cs:86`  
- **問題**: `t.FullName.EndsWith("." + componentName)` 在多組件同尾段名稱時會命中第一個掃描到的型別，結果依 Assembly 掃描順序而變。  
- **影響**: 大型專案中可能偶發綁錯型別，問題難以重現。  

### [P3] `maxDepth` 缺少整數/範圍約束
- **位置**: `Server~/src/tools/getGameObjectTool.ts:18`  
- **問題**: schema 使用 `z.number()`，允許浮點與 `< -1` 值；C# 端語義其實是整數且 `-1` 特例。  
- **影響**: 契約鬆散，可能導致跨端語義不一致（例如 `1.5`）。  

### 自我評估檢查補充

Request 已涵蓋 `EndsWith` 誤匹配與整數攔截風險，但**尚未覆蓋**上述兩個場景引用分支盲點（`UnityEngine.Object` 基底型別、雙鍵 fallback）。這兩點建議納入「已知脆弱點」。

---

## 4. 改善建議 (Improvement Suggestions)

### 建議 A: 結構化引用解析改為「先 ID、失敗再 Path」，並覆蓋 `UnityEngine.Object`

```csharp
// UpdateComponentTool.cs (ConvertJTokenToValue)
if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token.Type == JTokenType.Object)
{
    JObject refObj = (JObject)token;
    if (refObj.ContainsKey("instanceId") || refObj.ContainsKey("objectPath"))
    {
        UnityEngine.Object resolvedObj = null;

        // 1) Try instanceId first
        if (refObj["instanceId"] != null && refObj["instanceId"].Type != JTokenType.Null)
        {
            int id = refObj["instanceId"].ToObject<int>();
            resolvedObj = EditorUtility.InstanceIDToObject(id);
        }

        // 2) Fallback to objectPath if instanceId failed
        if (resolvedObj == null && refObj["objectPath"] != null && refObj["objectPath"].Type != JTokenType.Null)
        {
            string objPath = refObj["objectPath"].ToObject<string>();
            GameObject go = ResolveGameObjectByPathAcrossLoadedScenes(objPath);
            resolvedObj = go;
        }

        if (resolvedObj == null)
            return null;

        if (targetType == typeof(GameObject))
            return resolvedObj is GameObject g ? g : (resolvedObj as Component)?.gameObject;

        if (typeof(Component).IsAssignableFrom(targetType))
        {
            if (targetType.IsAssignableFrom(resolvedObj.GetType())) return resolvedObj;
            var host = resolvedObj as GameObject ?? (resolvedObj as Component)?.gameObject;
            return host?.GetComponent(targetType);
        }

        // Cover UnityEngine.Object base and other assignable types
        if (targetType.IsAssignableFrom(resolvedObj.GetType()))
            return resolvedObj;

        return null;
    }
}
```

### 建議 B: `ComponentTypeResolver` 對 partial namespace match 做「唯一命中」策略

```csharp
// ComponentTypeResolver.cs
var suffixMatches = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => SafeGetTypes(a))
    .Where(t => t != null
        && typeof(Component).IsAssignableFrom(t)
        && t.FullName != null
        && t.FullName.EndsWith("." + componentName, StringComparison.Ordinal))
    .Distinct()
    .ToList();

if (suffixMatches.Count == 1) return suffixMatches[0];
if (suffixMatches.Count > 1)
{
    // 建議在此記錄警告，要求呼叫方改傳完整名稱
    return null;
}
```

### 建議 C: Node 端收斂 `maxDepth` 契約

```ts
// Server~/src/tools/getGameObjectTool.ts
maxDepth: z
  .number()
  .int()
  .gte(-1)
  .optional()
  .describe("Maximum depth for traversing children. 0 = target only, 1 = direct children, -1 = unlimited (default)")
```

---

## Refactor Prompt

根據 `doc/codeReview/Response_20260213_SixBugFixes.md` 的審查意見，請執行以下修正：

1. 修正 `UpdateComponentTool` 的結構化場景引用邏輯：當 `instanceId` 解析失敗時 fallback 到 `objectPath`，並補上 `UnityEngine.Object` 基底型別回傳分支。  
2. 調整 `ComponentTypeResolver` 的 partial namespace 匹配策略為唯一命中才接受，避免 Assembly 掃描順序造成誤綁。  
3. 在 `getGameObjectTool.ts` 對 `maxDepth` 加上整數與下限驗證（`.int().gte(-1)`），讓 TS/C# 契約一致。  
4. 新增最小測試覆蓋：  
   - `update_component` 傳 `{ instanceId, objectPath }` 且 ID 無效時可 fallback 成功  
   - `UnityEngine.Object` 欄位可接受結構化場景引用  
   - `maxDepth` 非整數輸入被 schema 擋下  

### 涉及檔案
- `Editor/Tools/UpdateComponentTool.cs`
- `Editor/Utils/ComponentTypeResolver.cs`
- `Server~/src/tools/getGameObjectTool.ts`
- `Server~/src/__tests__/`（新增或調整對應測試）

---

⚠️ **完成後請更新 Implementation Tracker**：

請在 `doc/requirement/feature_{name}_tracker.md` 對應 Phase 的「關鍵決策」區塊中，  
新增 `[Review Fix]` 標籤記錄本次修改內容，並在「關聯審查」區塊連結本審查報告。
