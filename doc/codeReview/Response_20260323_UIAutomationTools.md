# Code Review Response: UI Automation Testing Primitives

**日期**: 2026-03-23
**審查者**: Claude Opus 4.6
**對應 Request**: `doc/codeReview/Request_20260323_UIAutomationTools.md`
**需求文件**: `doc/requirement/feature_ui_automation_tools.md`

---

## 1. 代碼品質（7.5 / 10）

整體結構清晰，6 個工具類 + 1 個共用工具類的分層合理。命名一致（`UIAutomationUtils` 靜態方法 + 各工具類獨立 region）。C# 與 TypeScript 的參數對應正確。

扣分項：
- `ScanInteractableElements` 中的 O(n*m) 去重迴圈（見 §3.1）
- `wait_for_condition` 的 `elapsed` 累加精度問題（見 §3.3）
- `simulate_input_field` 缺少 `interactable` 前置檢查（見 §3.4）
- `FindObjectsOfType` 無 `includeInactive` 參數，與 `GetComponentsInChildren(includeNonInteractable)` 行為不對稱（見 §3.5）
- `GetScreenPosition` 與 `GetScreenCenter` 功能重複（見 §3.6）

---

## 2. 優點

1. **Play Mode Guard 設計**：`RequirePlayMode()` / `RequireEventSystem()` 作為共用前置檢查，錯誤訊息明確且包含操作提示（`use set_editor_state to enter Play Mode`），agent 友善度高。

2. **GetDisplayText Bug 修復**：在測試中發現 InputField Placeholder 問題並修復，優先檢查 `InputField`/`TMP_InputField` 組件的 `.text` 屬性，避免 `GetComponentInChildren<Text>()` 取到 Placeholder 子物件。這是實際踩坑經驗的正確處理。

3. **事件序列分幀**：`simulate_pointer_click` 的 3 幀事件序列（Enter→Down / Up→Click / Exit）符合 UGUI EventSystem 的真實行為模式，`simulate_drag` 的多幀插值設計也合理。

4. **TypeScript timeout 延長**：`wait_for_condition` 和 `simulate_drag` 在 TS 端計算額外的 WebSocket timeout（+5s / +10s），避免 Node 端先超時而丟失 Unity 端的正常回傳。

5. **TMP Reflection 策略統一**：所有 TMP 存取透過 `Type.GetType` + `GetProperty`，不依賴條件編譯，消除了 asmdef 依賴問題。

6. **`wait_for_condition` 超時設計**：超時回傳 `success: false` 而非 throw，附帶 `finalState` 讓 agent 可做後續決策，這比 hard error 更適合自動化場景。

---

## 3. 缺點與風險

### 3.1 [中] `ScanInteractableElements` 去重用 O(n*m) 線性搜索

**位置**: `UIAutomationTools.cs:371-382`, `:421-432`, `:471-480`

每個 TMP_InputField / TMP_Dropdown / ScrollRect 都遍歷整個 `results` List 做 instanceId 比對。若場景有 100+ UI 元素，三次掃描的去重總成本為 O(3 * n * m)。

**建議修復**：

```csharp
// 在 ScanInteractableElements 方法開頭，Selectable 迴圈結束後：
var capturedInstanceIds = new HashSet<int>();
foreach (var element in results)
    capturedInstanceIds.Add((int)element["instanceId"]);

// 然後在 TMP / ScrollRect 去重時替換為：
if (capturedInstanceIds.Contains(tmpInput.gameObject.GetInstanceID()))
    continue;
// ... 新增元素後：
capturedInstanceIds.Add(tmpInput.gameObject.GetInstanceID());
```

### 3.2 [中] `FindObjectsOfType` 與 `includeNonInteractable` 行為不對稱

**位置**: `UIAutomationTools.cs:329`, `:366`, `:417`, `:467`

當 `root == null`（全場景掃描）時使用 `FindObjectsOfType<T>()`，此 API **不會找到 inactive 的 GameObject**。但當 `root != null` 時使用 `GetComponentsInChildren<T>(includeNonInteractable)`，`includeNonInteractable=true` **會包含 inactive 物件**。

這造成行為不對稱：
- `rootPath=null` + `includeNonInteractable=true` → **不會回傳** inactive 元素（`FindObjectsOfType` 限制）
- `rootPath="Canvas"` + `includeNonInteractable=true` → **會回傳** inactive 元素

**建議修復**：

```csharp
// Unity 2020.3+（本專案要求 2022.3+）支援 FindObjectsByType：
if (root != null)
    selectables = root.GetComponentsInChildren<Selectable>(includeNonInteractable);
else if (includeNonInteractable)
    selectables = Resources.FindObjectsOfTypeAll<Selectable>()
        .Where(s => s.gameObject.scene.isLoaded)  // 排除 Asset 中的 prefab
        .ToArray();
else
    selectables = UnityEngine.Object.FindObjectsOfType<Selectable>();
```

或者，如果認為全場景掃描不需要 inactive 元素，應在文件中明確記載此行為差異，並在 `includeNonInteractable=true` 且 `rootPath=null` 時加上 warning 到回傳的 `message` 中。

### 3.3 [中] `wait_for_condition` elapsed 精度問題

**位置**: `UIAutomationTools.cs:1104-1128`

```csharp
elapsed += pollInterval;  // 每次固定加 pollInterval
```

但實際等待時間受 frame timing 影響，`yield return null` 的 while 迴圈可能在 `pollInterval` 之後額外等了幾毫秒才回來。隨著迭代次數增加，誤差累積。

**建議修復**：

```csharp
float startTime = Time.realtimeSinceStartup;
float elapsed = 0f;

while (elapsed < timeout)
{
    if (!Application.isPlaying) { /* ... */ }

    conditionMet = CheckCondition(objectPath, condition, value);
    if (conditionMet) break;

    float waitStart = Time.realtimeSinceStartup;
    while (Time.realtimeSinceStartup - waitStart < pollInterval)
        yield return null;

    elapsed = Time.realtimeSinceStartup - startTime;  // 直接計算真實經過時間
}
```

### 3.4 [中] `simulate_input_field` 缺少 `interactable` 前置檢查

**位置**: `UIAutomationTools.cs:743-871`

`simulate_pointer_click` 會檢查 `Selectable.interactable`，但 `simulate_input_field` 直接操作 `InputField.text` 屬性，即使 `interactable=false` 也能成功設值並觸發事件。這與真實使用者行為不一致。

**建議修復**：

```csharp
// 在找到 InputField 之後、設值之前加入：
InputField inputField = target.GetComponent<InputField>();
if (inputField != null)
{
    if (!inputField.interactable)
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"InputField at {identifierInfo} is not interactable.",
            "validation_error"
        );
    }
    // ... 原有邏輯
}

// TMP_InputField 同理：
var interactableProp = tmpInputType.GetProperty("interactable");
if (interactableProp != null && !(bool)interactableProp.GetValue(tmpInputField))
{
    return McpUnitySocketHandler.CreateErrorResponse(
        $"TMP_InputField at {identifierInfo} is not interactable.",
        "validation_error"
    );
}
```

### 3.5 [低] `GetScreenPosition` 與 `GetScreenCenter` 功能完全重複

**位置**: `UIAutomationTools.cs:705-725`（`SimulatePointerClickTool.GetScreenPosition`）
**位置**: `UIAutomationTools.cs:1393-1412`（`SimulateDragTool.GetScreenCenter`）

兩個方法邏輯完全相同，僅方法名不同。

**建議修復**：

```csharp
// 移到 UIAutomationUtils 中：
public static Vector2 GetScreenCenter(GameObject go)
{
    RectTransform rect = go.GetComponent<RectTransform>();
    if (rect != null)
    {
        Canvas canvas = go.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Camera cam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 center = (corners[0] + corners[2]) / 2f;
            if (cam != null)
                return RectTransformUtility.WorldToScreenPoint(cam, center);
            else
                return center;
        }
    }
    return Vector2.zero;
}
```

然後 `SimulatePointerClickTool` 和 `SimulateDragTool` 都呼叫 `UIAutomationUtils.GetScreenCenter()`。

### 3.6 [低] `simulate_drag` 缺少 PointerEnter / PointerExit

**位置**: `UIAutomationTools.cs:1335-1363`

`simulate_pointer_click` 發送完整的 Enter→Down / Up→Click / Exit 序列，但 `simulate_drag` 只有 Down→InitializePotentialDrag / BeginDrag / Drag×N / EndDrag→Up，缺少 `PointerEnter`（開始前）和 `PointerExit`（結束後）。

某些自定義 UI（如 hover highlight）依賴 PointerEnter/Exit 事件。

**建議修復**：

```csharp
// Frame 0 開頭加：
ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerEnterHandler);
ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerDownHandler);

// EndDrag + PointerUp 之後加：
ExecuteEvents.Execute(source, pointerData, ExecuteEvents.pointerExitHandler);
```

### 3.7 [低] `simulate_pointer_click` 中 `GetComponentInParent` 的意外行為

**位置**: `UIAutomationTools.cs:637-641`

```csharp
Selectable selectable = target.GetComponentInParent<Selectable>();
if (selectable == null)
{
    selectable = target.GetComponent<Selectable>();
}
```

`GetComponentInParent<T>()` 在 Unity 中**已包含自身**（Unity 2022.3 行為），所以後續的 `GetComponent<Selectable>()` fallback 永遠不會執行。這段 fallback 是多餘代碼。

**建議修復**：

```csharp
// 直接用一行即可：
Selectable selectable = target.GetComponentInParent<Selectable>();
```

---

## 4. 改善建議

### 4.1 TS 端 `get_interactable_elements` 缺少 `instanceId`/`objectPath` 參數驗證

**位置**: `uiAutomationTools.ts:80-110`

其他工具（`simulate_pointer_click`, `simulate_input_field` 等）在 handler 中都有 `instanceId`/`objectPath` 二選一的驗證，但 `get_interactable_elements` 和 `get_ui_element_state` 略過。

`get_interactable_elements` 不需要（它的參數都是 optional），但 `get_ui_element_state` 理論上需要。目前 `getUIElementStateHandler` 已有驗證（`:362-367`），正確。✅

### 4.2 TS 端 boilerplate 代碼重複

**觀察**: 6 個工具的註冊函數結構幾乎相同（logger.info → try/catch → handler → logger.info/error）。這是現有專案模式（`materialTools.ts` 等也是如此），所以**不建議在此 PR 改動**，但可記錄為未來技術債。

### 4.3 建議 `CheckCondition` 加入 `inactive` 條件的邊界情境說明

**位置**: `UIAutomationTools.cs:1193-1194`

```csharp
case "inactive":
    return obj != null && !obj.activeInHierarchy;
```

`GameObject.Find()` **只會找到 active 的 GameObject**，所以 `inactive` 條件實際上永遠回傳 `false`（`obj` 如果是 inactive 就找不到，找到的一定是 active）。

唯一能回傳 `true` 的情境是：`activeInHierarchy == false` 但 `activeSelf == true`（父層 inactive）。但 `GameObject.Find()` 在父層 inactive 時**也找不到**子物件。

**結論**：`inactive` 條件在當前實作下**只有 "先 active 再變 inactive" 的過渡期**才可能被 `wait_for_condition` 的 polling 捕捉到（第一次 poll 找到，下一次 poll 前物件被 deactivated，但此時 `GameObject.Find` 已經找不到了）。

**建議**：
1. 在 `wait_for_condition` 的 description 或回傳 message 中加註：`inactive` / `not_exists` 條件依賴 `GameObject.Find()`，只能找到 active 物件
2. 或改用 `Resources.FindObjectsOfTypeAll` + path 比對（但效能成本高，可能不值得）
3. 最低限度：在 Feature Doc 的 Section 7（已知限制）中記載此行為

---

## 5. 需求覆蓋率

### 5.1 需求 vs 實作對照

| 需求項目 | 需求文件位置 | 實作狀態 | 說明 |
|----------|-------------|----------|------|
| `get_interactable_elements` 基本掃描 | §3.1 | ✅ 完整 | |
| `get_interactable_elements` rootPath 過濾 | §3.1 參數表 | ✅ 完整 | |
| `get_interactable_elements` filter 過濾 | §3.1 參數表 | ✅ 完整 | |
| `get_interactable_elements` includeNonInteractable | §3.1 參數表 | ⚠️ 部分 | `FindObjectsOfType` 在 root=null 時不含 inactive（§3.2） |
| `get_interactable_elements` 9 種組件 state 欄位 | §3.1 各組件 state 欄位表 | ✅ 完整 | Button/Toggle/InputField/TMP_InputField/Slider/Dropdown/TMP_Dropdown/Scrollbar/ScrollRect |
| `simulate_pointer_click` 事件序列 | §3.2 事件序列 | ✅ 完整 | 3 幀 5 事件 |
| `simulate_pointer_click` 前置驗證 | §3.2 前置驗證 | ✅ 完整 | PlayMode/EventSystem/exists/active/interactable |
| `simulate_pointer_click` stateAfter | §3.2 回傳 | ✅ 完整 | |
| `simulate_input_field` replace/append | §3.3 行為 | ✅ 完整 | |
| `simulate_input_field` submitAfter | §3.3 行為 | ✅ 完整 | Legacy: onEndEdit / TMP: onEndEdit + onSubmit |
| `simulate_input_field` TMP reflection | §3.3 TMP 支援 | ✅ 完整 | |
| `get_ui_element_state` 無 Play Mode 限制 | §3.4 | ✅ 完整 | |
| `get_ui_element_state` 6 種組件回傳 | §3.4 回傳的組件類型表 | ✅ 完整 | Selectable/TMP_InputField/TMP_Dropdown/ScrollRect/Image/CanvasGroup |
| `get_ui_element_state` rectTransform | §3.4 回傳結構 | ✅ 完整 | |
| `get_ui_element_state` displayText | §3.4 回傳結構 | ✅ 完整 | |
| `wait_for_condition` 8 種條件 | §3.5 條件表 | ✅ 完整 | active/inactive/exists/not_exists/interactable/text_equals/text_contains/component_enabled |
| `wait_for_condition` timeout clamp | §3.5 參數表 | ✅ 完整 | max 30s, min poll 0.05s |
| `wait_for_condition` 超時回傳 success:false | §3.5 回傳 | ✅ 完整 | |
| `wait_for_condition` finalState | §3.5 回傳 | ✅ 完整 | |
| `wait_for_condition` Play Mode 退出檢測 | §3.5 Polling 實作 | ✅ 完整 | |
| `wait_for_condition` TS timeout 延長 | §3.5 TypeScript 端 | ✅ 完整 | +5s |
| `simulate_drag` delta 模式 | §3.6 參數表 | ✅ 完整 | |
| `simulate_drag` targetPath 模式 | §3.6 參數表 | ✅ 完整 | |
| `simulate_drag` steps/duration clamp | §3.6 參數表 | ✅ 完整 | 1~60 / 0.05~5s |
| `simulate_drag` 事件序列 | §3.6 事件序列 | ⚠️ 缺少 | 缺 PointerEnter/PointerExit（§3.6） |
| `simulate_drag` Drop 事件 | §3.6 事件序列 | ✅ 完整 | ExecuteHierarchy(dropHandler) |
| `simulate_drag` TS timeout 延長 | §3.6 TypeScript 端 | ✅ 完整 | +10s |
| 共用工具類 UIAutomationUtils | §4 | ✅ 完整 | 11 個方法全部實作 |
| 錯誤類型 6 種 | §6 | ✅ 完整 | |
| C# 註冊 | §5 修改檔案 | ✅ 完整 | BatchExecuteTool 之前 |
| TS 註冊 | §5 修改檔案 | ✅ 完整 | |

### 5.2 需求遺漏清單

| # | 遺漏項目 | 嚴重度 | 說明 |
|---|---------|--------|------|
| 1 | `simulate_drag` PointerEnter/Exit | 低 | 需求 §3.6 事件序列未明確要求，但與 `simulate_pointer_click` 不一致 |
| 2 | `inactive` 條件語義問題 | 中 | `GameObject.Find` 找不到 inactive 物件，導致 `inactive`/`not_exists` 條件幾乎等價（§4.3） |

### 5.3 未測試清單

| # | 項目 | 風險 |
|---|------|------|
| 1 | TMP_InputField / TMP_Dropdown（reflection 路徑） | 中 — reflection 的 property name 若 TMP 版本變更可能斷裂 |
| 2 | `includeNonInteractable=true` 行為 | 低 |
| 3 | `interactable=false` 元素的 `simulate_input_field` | 中 — 當前可繞過 interactable 限制 |
| 4 | `no_event_system` 錯誤路徑 | 低 |
| 5 | `IDropHandler` drop 事件 | 低 |
| 6 | `wait_for_condition` 異步等待（延遲觸發） | 中 — 核心場景未測試 |
| 7 | `batch_execute` 串聯 | 低 |
| 8 | 高元素數場景效能（>100 元素） | 低 |

---

## 6. 總結評分

| 維度 | 分數 | 說明 |
|------|------|------|
| 代碼品質 | 7.5/10 | 結構清晰但有去重效能、重複方法、精度問題 |
| 需求覆蓋率 | 9/10 | 僅 `simulate_drag` Enter/Exit 和 `inactive` 語義為小缺口 |
| 測試覆蓋率 | 7/10 | 25 項通過但 TMP、interactable 邊界、異步等待未測 |
| 可維護性 | 8/10 | 共用工具類設計好，但 GetScreenPosition/GetScreenCenter 重複 |
| **綜合** | **8/10** | 可合併，建議先修 §3.1（去重效能）、§3.3（elapsed 精度）、§3.4（interactable 檢查） |

---

## 7. Refactor Prompt

> 以下為可直接執行的修改指令，按優先級排序。

### Priority 1：修復 `elapsed` 累加精度（§3.3）
- **檔案**: `Editor/Tools/UIAutomationTools.cs` — `WaitForConditionTool.ExecuteCoroutine`
- **動作**: 將 `elapsed += pollInterval` 改為 `elapsed = Time.realtimeSinceStartup - startTime`
- **範圍**: ~5 行

### Priority 2：`ScanInteractableElements` 去重改用 HashSet（§3.1）
- **檔案**: `Editor/Tools/UIAutomationTools.cs` — `UIAutomationUtils.ScanInteractableElements`
- **動作**: Selectable 迴圈後建立 `HashSet<int> capturedInstanceIds`，三處去重改為 `capturedInstanceIds.Contains()`
- **範圍**: ~15 行

### Priority 3：`simulate_input_field` 加入 interactable 檢查（§3.4）
- **檔案**: `Editor/Tools/UIAutomationTools.cs` — `SimulateInputFieldTool.Execute`
- **動作**: 在操作 InputField / TMP_InputField 之前檢查 `interactable` 屬性
- **範圍**: ~10 行

### Priority 4：合併重複的 `GetScreenPosition` / `GetScreenCenter`（§3.5）
- **檔案**: `Editor/Tools/UIAutomationTools.cs`
- **動作**: 移到 `UIAutomationUtils.GetScreenCenter()`，刪除兩個 private 方法
- **範圍**: ~20 行

### Priority 5：移除 `GetComponentInParent` 後多餘的 fallback（§3.7）
- **檔案**: `Editor/Tools/UIAutomationTools.cs:637-641`
- **動作**: 移除 `if (selectable == null) { selectable = target.GetComponent<Selectable>(); }`
- **範圍**: 3 行

### Priority 6（Optional）：`simulate_drag` 加入 PointerEnter/Exit（§3.6）
- **檔案**: `Editor/Tools/UIAutomationTools.cs` — `SimulateDragTool.ExecuteCoroutine`
- **動作**: Frame 0 前加 `pointerEnterHandler`，EndDrag 後加 `pointerExitHandler`
- **範圍**: 2 行

### 完成後
- 執行 `/verification-loop` 確認 Build 通過
- 更新 `implementation-tracker`（若有）
