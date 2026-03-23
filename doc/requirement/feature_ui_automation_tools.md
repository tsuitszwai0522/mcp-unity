# Feature Design: UGUI Automation Testing Primitives

> 狀態：**已實作、已測試** (Phase 1 + Phase 2 全部完成，整合測試通過)
> 建立日期：2026-03-23
> 實作日期：2026-03-23
> 測試日期：2026-03-23
> 目標版本：v1.6.0

## 1. 需求概述

### 背景

MCP Unity 目前的 UGUI 工具（`UGUITools.cs`）專注於 **Edit Mode 下的 UI 建構**（建立 Canvas、建立 UI 元素、修改 RectTransform 等）。但在 agent 自動化測試場景中，AI 需要在 **Play Mode** 下與 UI 互動——點擊按鈕、填寫表單、拖拽元素、等待畫面切換——這些能力目前完全缺失。

### 目標

為 MCP Unity 添加一組通用的 UGUI 自動化 primitives，讓 AI agent 能在 Play Mode 下：
1. 發現場景中可互動的 UI 元素
2. 模擬使用者操作（點擊、輸入、拖拽）
3. 等待 UI 狀態變化（元素出現、啟用、內容匹配）
4. 查詢單一 UI 元素的即時狀態

### 設計原則

- **通用性**：僅依賴 UGUI 標準組件，不涉及任何遊戲特定邏輯
- **事件完整性**：模擬操作必須走完整的 EventSystem 事件序列，確保 listener 正確觸發
- **可觀測性**：每個操作回傳執行後的狀態，讓 agent 能驗證操作結果
- **Play Mode 限制**：所有工具在非 Play Mode 下回傳明確錯誤（`get_ui_element_state` 除外）

---

## 2. 功能清單

| # | Tool Name | 說明 | 同步/非同步 |
|---|-----------|------|-------------|
| 1 | `get_interactable_elements` | 掃描場景中所有可互動 UI 元素 | 同步 |
| 2 | `simulate_pointer_click` | 對指定 GameObject 發送完整點擊事件序列 | 非同步（3 幀） |
| 3 | `simulate_input_field` | 對 InputField / TMP_InputField 填入文字 | 同步 |
| 4 | `get_ui_element_state` | 查詢單一 UI 元素的即時狀態 | 同步 |
| 5 | `wait_for_condition` | 等待指定條件成立（泛用等待機制） | 非同步（polling） |
| 6 | `simulate_drag` | 模擬拖拽手勢（from → to） | 非同步（多幀） |

---

## 3. Tool 詳細定義

### 3.1 `get_interactable_elements`

掃描場景中所有可互動 UI 元素，給 agent「視覺」能力。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `rootPath` | string | 否 | null | 限定掃描範圍的 GameObject 路徑（null = 全場景） |
| `filter` | string[] | 否 | null | 篩選組件類型，可選值：`Button`, `Toggle`, `InputField`, `TMP_InputField`, `Slider`, `Dropdown`, `TMP_Dropdown`, `ScrollRect`, `Scrollbar`（null = 全部） |
| `includeNonInteractable` | bool | 否 | false | 是否包含 `interactable=false` 或 `activeInHierarchy=false` 的元素 |

#### 掃描邏輯

1. 透過 `Selectable` 基類取得所有 Button, Toggle, InputField, Slider, Dropdown, Scrollbar
   - `root != null` → `GetComponentsInChildren<Selectable>(includeNonInteractable)`
   - `root == null` → `FindObjectsByType<Selectable>(includeNonInteractable ? Include : Exclude, None)`
2. 建立 `HashSet<int> capturedInstanceIds` 記錄已捕獲的 instanceId（O(1) 去重）
3. 獨立掃描 `TMP_InputField`（部分版本非 Selectable 子類），透過 reflection 存取，以 `capturedInstanceIds` 去重
4. 獨立掃描 `TMP_Dropdown`，同上去重機制
5. 獨立掃描 `ScrollRect`（非 Selectable），同上去重機制

> **v1.6.0 Refactor**: 去重從 O(n*m) 線性搜索改為 HashSet O(1)；`FindObjectsOfType` 改為 `FindObjectsByType` 以正確支援 `includeNonInteractable=true` 時包含 inactive 物件。

#### 回傳

```jsonc
{
  "success": true,
  "message": "Found 4 interactable element(s)",
  "elements": [
    {
      "path": "Canvas/Panel/LoginButton",
      "instanceId": 12345,
      "componentType": "Button",
      "interactable": true,
      "active": true,
      "state": {
        "interactable": true,
        "componentType": "Button",
        "text": "Login"          // 子層 Text/TMP_Text 內容
      }
    },
    {
      "path": "Canvas/Panel/UsernameField",
      "instanceId": 12346,
      "componentType": "TMP_InputField",
      "interactable": true,
      "active": true,
      "state": {
        "componentType": "TMP_InputField",
        "interactable": true,
        "text": "",
        "placeholder": "Enter username..."
      }
    },
    {
      "path": "Canvas/Panel/RememberToggle",
      "instanceId": 12347,
      "componentType": "Toggle",
      "interactable": true,
      "active": true,
      "state": {
        "interactable": true,
        "componentType": "Toggle",
        "isOn": false,
        "label": "Remember me"   // 子層 Text/TMP_Text 內容
      }
    },
    {
      "path": "Canvas/Panel/VolumeSlider",
      "instanceId": 12348,
      "componentType": "Slider",
      "interactable": true,
      "active": true,
      "state": {
        "interactable": true,
        "componentType": "Slider",
        "value": 0.75,
        "minValue": 0,
        "maxValue": 1,
        "wholeNumbers": false
      }
    }
  ],
  "count": 4
}
```

#### 各組件 state 欄位

| componentType | state 欄位 |
|---------------|-----------|
| `Button` | `interactable`, `text`（子層 Text） |
| `Toggle` | `interactable`, `isOn`, `label`（子層 Text） |
| `InputField` | `interactable`, `text`, `placeholder` |
| `TMP_InputField` | `interactable`, `text`, `placeholder`（via reflection） |
| `Slider` | `interactable`, `value`, `minValue`, `maxValue`, `wholeNumbers` |
| `Dropdown` | `interactable`, `value`, `selectedText`, `options[]` |
| `TMP_Dropdown` | `interactable`, `value`, `selectedText`（via reflection） |
| `Scrollbar` | `interactable`, `value`, `size` |
| `ScrollRect` | `horizontal`, `vertical` |

---

### 3.2 `simulate_pointer_click`

對指定 GameObject 發送完整的 UGUI 點擊事件序列。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `objectPath` | string | 二選一 | - | 目標 GameObject 的 hierarchy path |
| `instanceId` | int | 二選一 | - | 目標 GameObject 的 instance ID |

#### 前置驗證

依序檢查，任一不滿足則回傳對應錯誤：
1. `Application.isPlaying == true` → `play_mode_required`
2. `EventSystem.current != null` → `no_event_system`
3. 目標 GameObject 存在 → `not_found_error`
4. `activeInHierarchy == true` → `validation_error`
5. 若目標或父層有 `Selectable`，檢查 `interactable == true` → `validation_error`

> **實作備註**：未檢查 `Graphic.raycastTarget`，因為 `ExecuteEvents.Execute` 直接對目標發送事件，不經過 raycast。

#### 事件序列（3 幀）

```
Frame 1: PointerEnter → PointerDown
Frame 2: PointerUp → PointerClick
Frame 3: PointerExit
```

使用 `EditorCoroutineUtility.StartCoroutineOwnerless` + `yield return null` 分幀。

#### PointerEventData 初始化

```csharp
new PointerEventData(EventSystem.current)
{
    position = UIAutomationUtils.GetScreenCenter(target),  // RectTransform 中心的 screen space 座標
    button = PointerEventData.InputButton.Left,
    pointerPress = target,
    pointerEnter = target
};
```

#### 回傳

```jsonc
{
  "success": true,
  "message": "Successfully clicked Canvas/Panel/LoginButton",
  "targetPath": "Canvas/Panel/LoginButton",
  "eventsDispatched": ["PointerEnter", "PointerDown", "PointerUp", "PointerClick", "PointerExit"],
  "stateAfter": {
    "componentType": "Toggle",
    "interactable": true,
    "isOn": true
  }
}
```

`stateAfter` 僅在目標有 `Selectable` 組件時回傳，使用 `ExtractSelectableState()` 提取。

---

### 3.3 `simulate_input_field`

對 InputField 或 TMP_InputField 填入文字並觸發相關事件。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `objectPath` | string | 二選一 | - | 目標 GameObject 的 hierarchy path |
| `instanceId` | int | 二選一 | - | 目標 GameObject 的 instance ID |
| `text` | string | 是 | - | 要填入的文字 |
| `mode` | string | 否 | `"replace"` | `"replace"` 替換全部 / `"append"` 追加到現有文字後 |
| `submitAfter` | bool | 否 | true | 填入後是否觸發 submit 事件 |

#### 行為

1. 偵測目標上的 InputField 類型（優先 legacy `InputField`，其次 `TMP_InputField`）
2. 檢查 `interactable` 屬性，若為 `false` 則回傳 `validation_error`（v1.6.0 新增）
3. 根據 `mode` 設定 `.text` 屬性
4. 手動觸發 `onValueChanged.Invoke(newText)`
4. 若 `submitAfter == true`：
   - Legacy: 觸發 `onEndEdit.Invoke(newText)`
   - TMP: 觸發 `onEndEdit.Invoke(newText)` + `onSubmit.Invoke(newText)`（皆透過 reflection）

#### TMP 支援

透過 reflection 存取 `TMPro.TMP_InputField`：
- `text` property: get/set
- `onValueChanged` property → `.Invoke(string)`
- `onEndEdit` property → `.Invoke(string)`
- `onSubmit` property → `.Invoke(string)`

使用 `UGUIToolUtils.IsTMProAvailable()` 做前置檢查。

#### 回傳

```jsonc
{
  "success": true,
  "message": "Successfully set text on TMP_InputField at Canvas/Panel/UsernameField",
  "targetPath": "Canvas/Panel/UsernameField",
  "inputFieldType": "TMP_InputField",
  "previousText": "",
  "currentText": "testuser@example.com",
  "submitted": true
}
```

---

### 3.4 `get_ui_element_state`

輕量查詢單一 UI 元素的即時狀態。**不要求 Play Mode**，Edit Mode 也可使用。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `objectPath` | string | 二選一 | - | 目標 GameObject 的 hierarchy path |
| `instanceId` | int | 二選一 | - | 目標 GameObject 的 instance ID |

#### 回傳結構

```jsonc
{
  "success": true,
  "message": "UI element state for Canvas/Panel/RememberToggle",
  "path": "Canvas/Panel/RememberToggle",
  "instanceId": 12347,
  "active": true,
  "activeInHierarchy": true,
  "components": {
    "Toggle": {
      "interactable": true,
      "componentType": "Toggle",
      "isOn": true
    },
    "Image": {
      "color": "(1.00, 1.00, 1.00, 1.00)",
      "raycastTarget": true,
      "sprite": "Checkmark"
    },
    "CanvasGroup": {             // 僅在存在時回傳
      "alpha": 1,
      "interactable": true,
      "blocksRaycasts": true
    },
    "ScrollRect": {              // 僅在存在時回傳
      "horizontal": false,
      "vertical": true,
      "normalizedPosition": { "x": 0, "y": 1 }
    }
  },
  "rectTransform": {
    "anchoredPosition": { "x": 0, "y": -30 },
    "sizeDelta": { "x": 200, "y": 40 },
    "anchorMin": { "x": 0.5, "y": 0.5 },
    "anchorMax": { "x": 0.5, "y": 0.5 },
    "pivot": { "x": 0.5, "y": 0.5 }
  },
  "displayText": "Remember me"
}
```

#### 回傳的組件類型

| 組件 | 條件 | 回傳欄位 |
|------|------|---------|
| Selectable 子類 | `GetComponent<Selectable>()` | 透過 `ExtractSelectableState()` |
| TMP_InputField | Selectable 未捕獲時 | `text`, `interactable`, `placeholder` |
| TMP_Dropdown | Selectable 未捕獲時 | `value`, `interactable`, `selectedText` |
| ScrollRect | 存在時 | `horizontal`, `vertical`, `normalizedPosition` |
| Image | 存在時 | `color`, `raycastTarget`, `sprite` |
| CanvasGroup | 存在時 | `alpha`, `interactable`, `blocksRaycasts` |

`displayText` 遍歷子層尋找 `TMP_Text`（優先）或 `Text`。

---

### 3.5 `wait_for_condition`

泛用等待機制，使用 EditorCoroutine polling 等待指定條件成立後回傳。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `objectPath` | string | 是 | - | 目標 GameObject 的 hierarchy path |
| `condition` | string | 是 | - | 等待條件類型（見下表） |
| `value` | string | 視條件 | - | 條件參數（部分條件需要） |
| `timeout` | float | 否 | 10.0 | 超時秒數（上限 30 秒，自動 clamp） |
| `pollInterval` | float | 否 | 0.1 | 輪詢間隔秒數（下限 0.05 秒） |

#### 支援的條件類型

| condition | value 參數 | 說明 |
|-----------|-----------|------|
| `"active"` | 不需要 | 等待 `GameObject.Find(path)` 找到且 `activeInHierarchy == true` |
| `"inactive"` | 不需要 | 等待找到且 `activeInHierarchy == false` |
| `"exists"` | 不需要 | 等待 `GameObject.Find(path) != null` |
| `"not_exists"` | 不需要 | 等待 `GameObject.Find(path) == null` |
| `"interactable"` | 不需要 | 等待 `Selectable.interactable == true` |
| `"text_equals"` | 完全匹配文字 | 等待 `GetDisplayText()` 完全匹配 value |
| `"text_contains"` | 包含文字 | 等待 `GetDisplayText()` 包含 value 子字串 |
| `"component_enabled"` | 組件類型名 | 等待 `GetComponent(value)` 的 `Behaviour.enabled == true` |

#### Polling 實作

```csharp
float startTime = Time.realtimeSinceStartup;
while (elapsed < timeout)
{
    if (!Application.isPlaying) → 回傳 play_mode_required 錯誤
    conditionMet = CheckCondition(objectPath, condition, value);
    if (conditionMet) break;
    // yield return null loop 直到 pollInterval 時間到
    elapsed = Time.realtimeSinceStartup - startTime;  // v1.6.0: 使用真實經過時間
}
```

使用 `Time.realtimeSinceStartup` 計時，不受 `Time.timeScale` 影響。

#### TypeScript 端 timeout 處理

TS handler 將 WebSocket request timeout 設為 `(params.timeout + 5) * 1000` ms，確保 Unity 端有時間完成 polling + 回傳。

#### 回傳

```jsonc
// 成功
{
  "success": true,
  "message": "Condition 'active' met on 'Canvas/LoadingPanel' after 2.35s",
  "condition": "active",
  "objectPath": "Canvas/LoadingPanel",
  "elapsed": 2.35,
  "finalState": {
    "active": true,
    "activeInHierarchy": true,
    "displayText": "Welcome!"
  }
}

// 超時
{
  "success": false,
  "error": {
    "type": "timeout_error",
    "message": "Timeout after 10.0s waiting for 'active' on 'Canvas/LoadingPanel'"
  },
  "condition": "active",
  "objectPath": "Canvas/LoadingPanel",
  "elapsed": 10.0,
  "finalState": {
    "active": false,
    "activeInHierarchy": false
  }
}
```

> **設計決策**：超時回傳 `success: false` 而非 throw exception，讓 agent 可以根據 `finalState` 決定下一步。TS 端以 `isError: true` 標記但不 throw。

---

### 3.6 `simulate_drag`

模擬拖拽手勢，從起點拖到終點。支援 `delta`（像素偏移）和 `targetPath`（拖到另一元素中心）兩種模式。

#### 參數

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `objectPath` | string | 二選一 | - | 要拖拽的 GameObject 的 hierarchy path |
| `instanceId` | int | 二選一 | - | 要拖拽的 GameObject 的 instance ID |
| `delta` | `{x, y}` | 二選一 | - | 拖拽位移量（screen pixels），相對於元素中心 |
| `targetPath` | string | 二選一 | - | 拖拽目標 GameObject 的 hierarchy path（自動計算 delta） |
| `steps` | int | 否 | 5 | 拖拽中間幀數（clamp 1~60） |
| `duration` | float | 否 | 0.3 | 拖拽持續時間秒（clamp 0.05~5） |

#### 事件序列

```
Frame 0:     PointerEnter → PointerDown → InitializePotentialDrag
Frame 1:     BeginDrag
Frame 2..N:  Drag (每步更新 position，線性插值，間隔 = duration / steps)
Frame N+1:   EndDrag → PointerUp → PointerExit
             → 若有 targetPath 且目標有 IDropHandler：ExecuteHierarchy(dropHandler)
```

> **v1.6.0 Refactor**: 加入 PointerEnter/PointerExit 事件，與 `simulate_pointer_click` 保持一致。

#### 座標系統

- 起始位置：source 元素 `RectTransform` 中心的 screen space 座標
- `delta` 模式：終點 = 起始位置 + delta 向量
- `targetPath` 模式：終點 = target 元素 `RectTransform` 中心的 screen space 座標

Screen space 轉換：
- ScreenSpaceOverlay Canvas → `RectTransform.GetWorldCorners()` 中心即 screen space
- 其他 Canvas → `RectTransformUtility.WorldToScreenPoint(camera, center)`

#### Drag 步進

每步使用 `Vector2.Lerp(startPos, endPos, t)` 計算位置，`pointerData.delta` 設為相鄰步之差。每步之間等待 `duration / steps` 秒（透過 `Time.realtimeSinceStartup` loop）。

#### TypeScript 端 timeout 處理

TS handler 將 WebSocket request timeout 設為 `(params.duration + 10) * 1000` ms。

#### 回傳

```jsonc
{
  "success": true,
  "message": "Successfully dragged Canvas/Inventory/Item_Sword to Canvas/Inventory/Slot_3",
  "sourcePath": "Canvas/Inventory/Item_Sword",
  "startPosition": { "x": 200, "y": 300 },
  "endPosition": { "x": 500, "y": 300 },
  "totalDelta": { "x": 300, "y": 0 },
  "steps": 5,
  "dropReceiver": "Canvas/Inventory/Slot_3"  // 有 IDropHandler 接收時，否則 null
}
```

---

## 4. 共用基礎設施

### 4.1 UIAutomationUtils 靜態工具類

**檔案**: `Editor/Tools/UIAutomationTools.cs`

| 方法 | 說明 |
|------|------|
| `RequirePlayMode()` | Play Mode 檢查，回傳 `play_mode_required` 錯誤或 null |
| `RequireEventSystem()` | EventSystem 檢查，回傳 `no_event_system` 錯誤或 null |
| `FindGameObject(instanceId, objectPath, out go, out info)` | GameObject 查找，支援 instanceId 和 path |
| `ExtractSelectableState(Selectable)` | 提取 Selectable 子類的完整狀態 |
| `ExtractTMPInputFieldState(Component)` | 透過 reflection 提取 TMP_InputField 狀態 |
| `ExtractTMPDropdownState(Component)` | 透過 reflection 提取 TMP_Dropdown 狀態 |
| `GetTMPText(GameObject)` | 透過 reflection 取得 TMP_Text 內容 |
| `GetLegacyText(GameObject)` | 取得 legacy Text 內容 |
| `GetDisplayText(GameObject)` | InputField/TMP_InputField 優先回傳 `.text`，其次 TMP_Text，最後 legacy Text |
| `GetGameObjectPath(GameObject)` | 取得完整 hierarchy path |
| `GetScreenCenter(GameObject)` | 取得 UI 元素 RectTransform 中心的 screen space 座標（v1.6.0 從各工具合併） |
| `ScanInteractableElements(root, filter, includeNonInteractable)` | 完整掃描邏輯，含 HashSet 去重 |

### 4.2 Play Mode Guard

除 `get_ui_element_state` 外，所有工具在 `Execute` / `ExecuteCoroutine` 開頭呼叫 `RequirePlayMode()`。

錯誤訊息提示使用 `set_editor_state` 進入 Play Mode：
```
"This tool requires Play Mode. Enter Play Mode first (use set_editor_state to enter Play Mode)."
```

### 4.3 GameObject 查找

獨立於 `GameObjectToolUtils.FindGameObject`，因為：
- Play Mode 下不需要 `PrefabEditingService` 檢查
- 簡化為 `instanceId` → `EditorUtility.InstanceIDToObject` / `objectPath` → `GameObject.Find`

### 4.4 TMP Reflection 策略

所有 TMP 存取統一透過 `Type.GetType("TMPro.XXX, Unity.TextMeshPro")` + `GetProperty` / `GetMethod`。
不使用 `#if` 條件編譯，確保不需要額外 asmdef 依賴。

---

## 5. 檔案結構

### 新增檔案

| 檔案 | 行數（約） | 說明 |
|------|-----------|------|
| `Editor/Tools/UIAutomationTools.cs` | ~950 | C# 實作：6 個工具類 + `UIAutomationUtils` 靜態工具類 |
| `Server~/src/tools/uiAutomationTools.ts` | ~500 | TypeScript 實作：Zod schemas、handler、aggregate 註冊函數 |

### 修改檔案

| 檔案 | 變更 |
|------|------|
| `Editor/UnityBridge/McpUnityServer.cs` | +12 行：在 `RegisterTools()` 中、`BatchExecuteTool` 之前新增 6 個工具註冊 |
| `Server~/src/index.ts` | +2 行：import `registerUIAutomationTools` + 呼叫 |

---

## 6. 錯誤類型一覽

| 錯誤類型 | 觸發條件 |
|----------|---------|
| `play_mode_required` | 非 Play Mode 下呼叫需要 Play Mode 的工具 |
| `no_event_system` | 場景中無 EventSystem（click / drag 需要） |
| `validation_error` | 參數缺失、目標不活動、不可互動、無效 condition |
| `not_found_error` | GameObject 找不到 |
| `component_error` | 目標上無所需組件（如非 InputField） |
| `timeout_error` | `wait_for_condition` 超時（回傳在 `error.type` 中，`success: false`） |

---

## 7. 已知風險與限制

| 風險等級 | 問題 | 說明 | 緩解措施 |
|----------|------|------|----------|
| 🔴 高 | Play Mode 下 WebSocket 連線 | Unity 進入 Play Mode 時會 domain reload，WebSocket server 需要重新啟動 | 依賴現有 `McpUnityServer` 的 Play Mode 持續連線機制（已在 v1.4.0 實作） |
| 🟡 中 | 事件序列時序 | 分幀發送事件依賴 EditorCoroutine 的 frame timing | 使用 `yield return null` 確保每幀步進 |
| 🟡 中 | TMP_InputField reflection | 若專案未安裝 TextMeshPro，reflection 會 graceful fallback | 使用 `UGUIToolUtils.IsTMProAvailable()` 前置檢查 |
| 🟡 中 | `simulate_drag` 座標精度 | Screen space 座標在不同解析度/Canvas 模式下行為不同 | 提供 `targetPath` 模式避免手動計算座標 |
| 🟡 中 | `simulate_pointer_click` 無 raycast 驗證 | 未檢查 `Graphic.raycastTarget`，因為 `ExecuteEvents.Execute` 直接對目標發送，不經過 raycast | 如果實際 UI 中 raycast 被遮擋，事件仍會送達（與真實使用者行為不同） |
| 🟢 低 | `get_interactable_elements` 效能 | 場景中有大量 UI 元素時掃描耗時 | 提供 `rootPath` 和 `filter` 參數限制範圍 |
| 🟢 低 | 所有 `objectPath` 查找依賴 `GameObject.Find` | `Find` 只能找到 active 的 GameObject，inactive 物件回傳 `not_found_error` 而非 `validation_error` | 適用於 `wait_for_condition` 的 `exists`/`not_exists`、`simulate_drag` 對 inactive 元素等場景 |

---

## 8. 測試結果

> 測試日期：2026-03-23
> 測試環境：SampleScene（TestCanvas + EventSystem），Unity 2022.3，macOS

### 8.1 整合測試結果摘要

| 工具 | 測試數 | 通過 | 失敗 | 備註 |
|------|--------|------|------|------|
| Play Mode Guard | 2 | 2 | 0 | |
| `get_interactable_elements` | 3 | 3 | 0 | |
| `simulate_pointer_click` | 2 | 2 | 0 | |
| `simulate_input_field` | 2 | 2 | 0 | |
| `get_ui_element_state` | 2 | 2 | 0 | |
| `wait_for_condition` | 3 | 3 | 0 | 首輪 `text_contains` 失敗 → 修復 bug 後通過 |
| `simulate_drag` | 8 | 8 | 0 | |
| 錯誤處理 | 3 | 3 | 0 | |
| **合計** | **25** | **25** | **0** | |

### 8.2 詳細測試記錄

#### Play Mode Guard
- [x] `get_ui_element_state` Edit Mode 下正常執行
- [x] `get_interactable_elements` Edit Mode 下回傳 `play_mode_required`

#### get_interactable_elements
- [x] 全場景掃描找到 4 個元素（Button, Toggle, InputField, Slider）
- [x] `filter: ["Button"]` 只回傳 1 個 Button
- [x] `rootPath: "TestCanvas/TestInputField"` 只回傳 1 個子樹元素

#### simulate_pointer_click
- [x] 點擊 Toggle，成功回傳
- [x] `get_ui_element_state` 驗證 Toggle 點擊後狀態

#### simulate_input_field
- [x] `mode: "replace"` 填入 "Hello World"，成功回傳
- [x] `mode: "append"` 追加 " - Appended"，成功回傳

#### get_ui_element_state
- [x] Edit Mode 查詢 Button 成功
- [x] Play Mode 查詢 Toggle 成功
- [x] Play Mode 查詢 InputField 成功

#### wait_for_condition
- [x] `active` 條件：已 active 物件立即回傳（elapsed = 0.00s）
- [x] `text_contains` 條件：InputField 含 "Hello" 立即回傳（修復 bug 後）
- [x] 超時測試：不存在物件 + `timeout: 1` → 1 秒後回傳 `timeout_error`

#### simulate_drag
- [x] `delta: {x:0, y:200}` 向下拖 ScrollView 成功
- [x] `delta: {x:0, y:-200}` 向上拖回成功
- [x] `steps: 1` 成功
- [x] `steps: 20` 成功
- [x] `duration: 0.1` 快速拖成功
- [x] `targetPath` 模式拖到另一個元素成功
- [x] 不存在路徑 → `not_found_error`
- [x] 缺少 delta + targetPath → `validation_error`

#### 錯誤處理
- [x] `simulate_pointer_click` 不存在路徑 → `not_found_error`
- [x] `simulate_input_field` 對 Button → `component_error`
- [x] `simulate_drag` inactive 元素 → `not_found_error`（`GameObject.Find` 限制）

### 8.3 測試中發現並修復的 Bug

#### Bug #1: `GetDisplayText` 對 InputField 回傳 Placeholder 文字

- **發現時機**: `wait_for_condition` + `text_contains` 測試
- **根因**: `GetDisplayText()` 呼叫 `GetComponentInChildren<Text>()` 時，對 InputField 找到的是 Placeholder 子物件的 Text（"Enter text here..."），而非 InputField 的 `.text` 屬性值
- **修復**: 在 `GetDisplayText()` 中優先檢查 `InputField` / `TMP_InputField` 組件，直接回傳 `.text` 屬性
- **影響範圍**: `wait_for_condition` 的 `text_equals` / `text_contains`、`get_ui_element_state` 的 `displayText` 欄位

### 8.4 v1.6.0 Refactor 驗證測試

> 測試日期：2026-03-23
> 測試環境：SampleScene（Canvas + TestPanel + 7 UI elements），Unity 2022.3，macOS

| # | 測試 | 驗證 Refactor | 結果 |
|---|------|---------------|------|
| T1.1 | 全場景掃描 (6 元素) | P2 HashSet + P2.5 FindObjectsByType | PASS |
| T1.2 | rootPath 過濾 (6 元素) | P2 HashSet | PASS |
| T1.3 | filter=Button,Toggle (2 元素) | P2 HashSet | PASS |
| T2.1 | 點擊 Button | P4 GetScreenCenter + P5 死代碼 | PASS |
| T2.2 | 點擊 Toggle | P5 GetComponentInParent | PASS |
| T3.1 | 填入可互動 InputField | baseline | PASS |
| T3.2 | 填入不可互動 InputField → 攔截 | P3 interactable 檢查 | PASS |
| T4.1 | 查詢 Button 狀態 | baseline | PASS |
| T5.1 | 等待已成立條件 → elapsed=0.00s | P1 elapsed 精度 | PASS |
| T5.2 | 等待超時 2s → elapsed=2.0s | P1 elapsed 精度 | PASS |
| T6.1 | 拖拽 Slider delta=(50,0) | P4 GetScreenCenter + P6 Enter/Exit | PASS |

**11/11 全部通過。**

### 8.5 未測試項目（需手動或特定環境）

| 項目 | 原因 |
|------|------|
| TMP_InputField / TMP_Dropdown | 測試場景未安裝 TextMeshPro |
| `includeNonInteractable: true` | 未建立 disabled 元素 |
| `interactable=false` 點擊驗證 | 未建立 disabled Button |
| `IDropHandler` drop 事件 | 需要自定義腳本實作 IDropHandler |
| `no_event_system` 錯誤 | 場景已有 EventSystem |
| `batch_execute` 串聯 | 未執行整合串聯測試 |
| `wait_for_condition` 非同步等待（延遲觸發） | 需要 coroutine 延遲啟用 GameObject |

---

## 9. 未來可擴展

以下工具不在本次範圍內，但 `UIAutomationUtils` 已預留共用基礎設施：

| Tool | 說明 |
|------|------|
| `simulate_scroll` | ScrollRect 滾動操作（設定 normalizedPosition） |
| `simulate_key_input` | 鍵盤輸入 / navigation |
| `simulate_slider_set` | 直接設定 Slider 值並觸發 onValueChanged |
| `simulate_dropdown_select` | 選擇 Dropdown 選項並觸發 onValueChanged |

---

## 10. 相關資源

- **實作檔案**: `Editor/Tools/UIAutomationTools.cs`, `Server~/src/tools/uiAutomationTools.ts`
- **註冊點**: `Editor/UnityBridge/McpUnityServer.cs:RegisterTools()`, `Server~/src/index.ts`
- **現有 UGUI 工具**: `Editor/Tools/UGUITools.cs` — Edit Mode UI 建構工具，共用 `UGUIToolUtils`
- **Play Mode 連線**: `Editor/UnityBridge/McpUnityServer.cs` — Play Mode 下的 WebSocket 持續連線
- **事件系統參考**: [Unity EventSystem Manual](https://docs.unity3d.com/2022.3/Documentation/Manual/EventSystem.html)
