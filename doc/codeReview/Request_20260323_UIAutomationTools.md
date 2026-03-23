# Code Review Request: UI Automation Testing Primitives

**日期**: 2026-03-23
**功能**: UGUI 自動化測試工具（Play Mode UI 互動）
**作者**: Claude Opus 4.6
**Feature Doc**: `doc/requirement/feature_ui_automation_tools.md`

---

## 1. 背景與目標

### 背景
MCP Unity 的 UGUI 工具（`UGUITools.cs`）專注於 Edit Mode 下的 UI 建構。在 agent 自動化測試場景中，AI 需要在 **Play Mode** 下與 UI 互動——點擊按鈕、填寫表單、拖拽元素、等待畫面切換——這些能力完全缺失。

### 目標
添加 6 個通用 UGUI 自動化工具，讓 AI agent 能在 Play Mode 下操控和觀測 UI：
1. 掃描可互動 UI 元素
2. 模擬點擊、輸入、拖拽
3. 等待 UI 狀態變化
4. 查詢單一元素即時狀態

---

## 2. 變更摘要

### 新增檔案

| 檔案 | 行數 | 說明 |
|------|------|------|
| `Editor/Tools/UIAutomationTools.cs` | 1416 | C# 實作：6 個工具類 + `UIAutomationUtils` 共用工具類 |
| `Server~/src/tools/uiAutomationTools.ts` | 667 | TypeScript 實作：Zod schemas、handlers、aggregate 註冊函數 |
| `doc/requirement/feature_ui_automation_tools.md` | ~640 | Feature 設計文檔 + 測試結果 |

### 修改檔案

| 檔案 | 變更 |
|------|------|
| `Editor/UnityBridge/McpUnityServer.cs` | +19 行：在 `RegisterTools()` 中、`BatchExecuteTool` 之前新增 6 個工具註冊 |
| `Server~/src/index.ts` | +4 行：import `registerUIAutomationTools` + 呼叫 |

### 新增工具

| Tool Name | 同步/非同步 | Play Mode | 用途 |
|-----------|-------------|-----------|------|
| `get_interactable_elements` | 同步 | 必要 | 掃描場景中所有可互動 UI 元素 |
| `simulate_pointer_click` | 非同步（3 幀） | 必要 | 完整點擊事件序列 |
| `simulate_input_field` | 同步 | 必要 | InputField / TMP_InputField 文字填入 |
| `get_ui_element_state` | 同步 | **不要求** | 單一 UI 元素狀態查詢 |
| `wait_for_condition` | 非同步（polling） | 必要 | 泛用等待機制（8 種條件） |
| `simulate_drag` | 非同步（多幀） | 必要 | 拖拽手勢模擬 |

---

## 3. 關鍵代碼

### 3.1 Play Mode Guard
**檔案**: `Editor/Tools/UIAutomationTools.cs:26-36`

```csharp
public static JObject RequirePlayMode()
{
    if (!Application.isPlaying)
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            "This tool requires Play Mode. Enter Play Mode first (use set_editor_state to enter Play Mode).",
            "play_mode_required"
        );
    }
    return null;
}
```

所有工具（`get_ui_element_state` 除外）在 `Execute` / `ExecuteCoroutine` 開頭呼叫。

### 3.2 GetDisplayText — InputField 特殊處理
**檔案**: `Editor/Tools/UIAutomationTools.cs:271-296`

```csharp
public static string GetDisplayText(GameObject go)
{
    // Check for InputField first — GetComponentInChildren<Text>() would return
    // the Placeholder child, not the actual input text.
    InputField inputField = go.GetComponent<InputField>();
    if (inputField != null)
        return inputField.text;

    // Check for TMP_InputField via reflection
    if (UGUIToolUtils.IsTMProAvailable())
    {
        Type tmpInputType = Type.GetType("TMPro.TMP_InputField, Unity.TextMeshPro");
        if (tmpInputType != null)
        {
            Component tmpInput = go.GetComponent(tmpInputType);
            if (tmpInput != null)
            {
                var textProp = tmpInputType.GetProperty("text");
                if (textProp != null)
                    return textProp.GetValue(tmpInput) as string;
            }
        }
    }

    return GetTMPText(go) ?? GetLegacyText(go);
}
```

> **測試中發現 Bug**: 原始實作直接 `GetComponentInChildren<Text>()`，對 InputField 會取到 Placeholder 子物件的 Text。修復後優先檢查 InputField 組件。

### 3.3 事件序列分幀（simulate_pointer_click）
**檔案**: `Editor/Tools/UIAutomationTools.cs:638-656`

```csharp
// Frame 1: PointerEnter → PointerDown
ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerEnterHandler);
ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerDownHandler);
yield return null;

// Frame 2: PointerUp → PointerClick
ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerClickHandler);
yield return null;

// Frame 3: PointerExit
ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerExitHandler);
```

使用 `EditorCoroutineUtility.StartCoroutineOwnerless` + `yield return null` 分幀，確保 UGUI 事件系統正確處理每步。

### 3.4 TMP_InputField 事件觸發（reflection）
**檔案**: `Editor/Tools/UIAutomationTools.cs:789-824`

```csharp
// Trigger onValueChanged
var onValueChangedField = tmpInputType.GetProperty("onValueChanged");
if (onValueChangedField != null)
{
    var onValueChanged = onValueChangedField.GetValue(tmpInputField);
    if (onValueChanged != null)
    {
        var invokeMethod = onValueChanged.GetType().GetMethod("Invoke", new Type[] { typeof(string) });
        invokeMethod?.Invoke(onValueChanged, new object[] { newText });
    }
}
```

所有 TMP 存取透過 `Type.GetType` + `GetProperty` + `GetMethod`，不使用 `#if` 條件編譯。

### 3.5 wait_for_condition polling 機制
**檔案**: `Editor/Tools/UIAutomationTools.cs:1098-1115`

```csharp
while (elapsed < timeout)
{
    if (!Application.isPlaying)
    {
        tcs.TrySetResult(McpUnitySocketHandler.CreateErrorResponse(
            "Play Mode exited during wait.", "play_mode_required"));
        yield break;
    }
    conditionMet = CheckCondition(objectPath, condition, value);
    if (conditionMet) break;
    // yield return null loop until pollInterval elapsed
    float waitStart = Time.realtimeSinceStartup;
    while (Time.realtimeSinceStartup - waitStart < pollInterval)
        yield return null;
    elapsed += pollInterval;
}
```

使用 `Time.realtimeSinceStartup` 計時（不受 `Time.timeScale` 影響）。超時回傳 `success: false` 而非 exception。

### 3.6 TypeScript timeout 延長
**檔案**: `Server~/src/tools/uiAutomationTools.ts:484-492`

```typescript
// wait_for_condition: extend WebSocket timeout to cover Unity-side polling
const requestTimeout = ((params.timeout ?? 10) + 5) * 1000;
const response = await mcpUnity.sendRequest(
  { method: waitForConditionToolName, params },
  { timeout: requestTimeout }
);
```

`simulate_drag` 同理：`(params.duration + 10) * 1000` ms。

---

## 4. 自我評估

### 4.1 已知脆弱點

| 風險等級 | 問題 | 說明 | 建議處理 |
|----------|------|------|----------|
| 🟡 中 | `simulate_pointer_click` 無 raycast 驗證 | `ExecuteEvents.Execute` 直接發送事件，不經過 raycast。若 UI 被其他元素遮擋，事件仍會送達（與真實使用者行為不同） | 可選：加入 `GraphicRaycaster.Raycast` 前置檢查，但會增加複雜度 |
| 🟡 中 | `ScanInteractableElements` 去重用 O(n*m) 迴圈 | TMP_InputField / TMP_Dropdown / ScrollRect 各自與已有結果做 instanceId 比對 | 可改用 `HashSet<int>` 降至 O(1) 查找 |
| 🟡 中 | `simulate_input_field` 未驗證 `interactable` 狀態 | 即使 InputField 的 `interactable=false`，仍可設定 text 並觸發事件 | 應加 interactable 前置檢查 |
| 🟡 中 | `wait_for_condition` 的 `elapsed` 精度 | 使用 `elapsed += pollInterval` 累加，實際等待時間可能因 frame timing 偏差而不精確 | 可改用 `Time.realtimeSinceStartup - startTime` 直接計算 |
| 🟢 低 | `simulate_drag` PointerEnter/PointerExit 缺失 | 事件序列中未包含 `PointerEnter`（起始）和 `PointerExit`（結束），與 `simulate_pointer_click` 不一致 | 可在 Frame 0 前加 PointerEnter，EndDrag 後加 PointerExit |
| 🟢 低 | `FindGameObject` 未復用 `GameObjectToolUtils` | 自行實作了簡化版 Find，缺少 `FindGameObjectByPath` fallback | Play Mode 下 `GameObject.Find` 足夠，但行為與 Edit Mode 工具不完全一致 |

### 4.2 Edge Cases

| 情境 | 處理方式 | 測試狀態 |
|------|----------|----------|
| Edit Mode 呼叫 Play Mode 工具 | `RequirePlayMode()` 回傳 `play_mode_required` | ✅ 已測試 |
| 無 EventSystem 的場景 | `RequireEventSystem()` 回傳 `no_event_system` | ⚠️ 未測試（場景已有 EventSystem） |
| InputField 上 `GetDisplayText` 取到 Placeholder | 優先檢查 `InputField` 組件回傳 `.text` | ✅ 已測試（測試中發現並修復） |
| `wait_for_condition` 超時 | 回傳 `success: false` + `timeout_error`，非 exception | ✅ 已測試 |
| `wait_for_condition` 期間退出 Play Mode | 回傳 `play_mode_required` 錯誤 | ⚠️ 未測試 |
| `simulate_drag` 對 inactive 元素 | `GameObject.Find` 返回 null → `not_found_error` | ✅ 已測試 |
| TMPro 未安裝時操作 TMP_InputField | `UGUIToolUtils.IsTMProAvailable()` 前置檢查，graceful fallback | ⚠️ 未測試 |
| `get_interactable_elements` 場景有 >100 元素 | `rootPath` + `filter` 限制範圍 | ⚠️ 未測試（效能） |

### 4.3 效能考量

- **`get_interactable_elements`**: 全場景掃描呼叫 `FindObjectsOfType` 多次（Selectable + TMP_InputField + TMP_Dropdown + ScrollRect），大場景可能有延遲
- **`ScanInteractableElements` 去重**: 對每個 TMP/ScrollRect 結果遍歷已有 List 做 instanceId 比對（O(n*m)）
- **`wait_for_condition` polling**: 每次 poll 呼叫 `GameObject.Find()`，高頻使用時有 GC 壓力

---

## 5. 審查重點

### 請重點審查以下區域：

1. **事件序列完整性** (`UIAutomationTools.cs:638-656`, `:1260-1340`)
   - `simulate_pointer_click` 的 3 幀事件序列是否正確模擬真實 UGUI 行為？
   - `simulate_drag` 的多幀事件序列是否遺漏了必要的 handler？

2. **TMP Reflection 安全性** (`UIAutomationTools.cs:772-840`)
   - reflection 存取 `onValueChanged.Invoke` 的方式是否有潛在的 `NullReferenceException` 風險？
   - `GetMethod("Invoke", new Type[] { typeof(string) })` 是否能正確解析 `UnityEvent<string>.Invoke`？

3. **`wait_for_condition` 的 timeout 精度** (`UIAutomationTools.cs:1098-1130`)
   - `elapsed += pollInterval` 累加 vs `realtimeSinceStartup` 差值，哪個更準確？
   - 超時回傳 `success: false`（非 error JObject 結構）是否與其他工具的錯誤格式一致？

4. **TypeScript/C# 參數對應** (`uiAutomationTools.ts` 全檔)
   - Zod schema 與 C# 參數提取是否一致？
   - `wait_for_condition` 和 `simulate_drag` 的自訂 timeout 計算是否合理？

5. **`simulate_input_field` 缺少 interactable 檢查** (`UIAutomationTools.cs:719-847`)
   - 是否應像 `simulate_pointer_click` 一樣，在操作前驗證 `interactable` 狀態？

---

## 6. 文檔一致性檢查

| 項目 | 狀態 | 說明 |
|------|------|------|
| Feature Doc | ✅ | `doc/requirement/feature_ui_automation_tools.md` 已更新至實作 + 測試結果 |
| CLAUDE.md | ⚠️ | 工具列表區未更新（現有格式僅描述 Adding a New Tool 流程，非列舉所有工具） |
| README.md | ⚠️ | 可能需更新工具總數或功能列表 |
| CHANGELOG.md | ❌ | 需添加 v1.6.0 的 UI Automation Tools 變更記錄 |
| Code Map | ⚠️ | `UIAutomationTools.cs` 未加入 Code Map（若有自動生成 script 則下次掃描會補上） |

---

## 7. 測試摘要

> 完整測試記錄見 `doc/requirement/feature_ui_automation_tools.md` Section 8

### 已通過（25/25）

| 類別 | 測試數 |
|------|--------|
| Play Mode Guard | 2 |
| `get_interactable_elements`（掃描 + filter + rootPath） | 3 |
| `simulate_pointer_click`（click Toggle + 驗證狀態） | 2 |
| `simulate_input_field`（replace + append） | 2 |
| `get_ui_element_state`（Edit + Play Mode） | 3 |
| `wait_for_condition`（active + text_contains + timeout） | 3 |
| `simulate_drag`（delta + targetPath + steps + duration + 錯誤） | 8 |
| 錯誤處理（not_found + component_error + timeout） | 3 |

### 測試中修復的 Bug

- **`GetDisplayText` 對 InputField 回傳 Placeholder**：`GetComponentInChildren<Text>()` 先找到 Placeholder 子物件。修復為優先檢查 `InputField` / `TMP_InputField` 組件。

### 未測試項目

- TMP_InputField / TMP_Dropdown（需 TextMeshPro）
- `interactable=false` 元素點擊
- `no_event_system` 錯誤
- `batch_execute` 串聯
- 高元素數場景效能

---

## 8. 相關資源

- **Feature Doc**: `doc/requirement/feature_ui_automation_tools.md`
- **參考實作**: `Editor/Tools/UGUITools.cs`（Edit Mode UI 工具）、`Editor/Tools/RunTestsTool.cs`（async 模式參考）
- **競品參考**: `doc/requirement/feature_competitive_tools.md`（Play Mode Control）
