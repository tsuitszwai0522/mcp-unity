# Code Review Response: UGUI Tools Implementation

**日期**: 2026-02-03
**審查對象**: `Request_20260203_UGUITools.md`
**審查者**: Antigravity

---

## 1. 代碼質素 (Code Quality)

*   **可讀性**: 代碼結構清晰，工具類別 (`CreateCanvasTool`, `CreateUIElementTool` 等) 職責劃分明確。共用邏輯被正確提取到 `UGUIToolUtils`。
*   **參數驗證**: TypeScript (Zod) 與 C# 端的參數驗證高度一致，錯誤訊息具體且有幫助 (例如明確指出無效的 `anchorPreset`)。
*   **Undo 支援**: 這一點做得非常好。幾乎所有的 GameObject 建立和組件添加都正確使用了 `Undo.RegisterCreatedObjectUndo` 和 `Undo.AddComponent`，這對於 Editor 工具至關重要。
*   **相容性**: 對於 `TextMeshPro` 的處理使用了 Reflection (`Type.GetType`)，避免了硬性依賴 Package，這是一個很好的設計決策。

## 2. 優點 (Pros)

1.  **強大的 Undo/Redo 系統整合**: 確保用戶的操作可以被安全撤銷，不會破壞場景狀態。
2.  **詳盡的 RectTransform 支援**: `SetRectTransformTool` 覆蓋了錨點 (Anchors)、軸心 (Pivot)、位置、旋轉、縮放等多個維度，並提供了實用的 Presets。
3.  **優雅的降級處理 (Graceful Degradation)**: 當 TMPro 未安裝時，自動降級為 Legacy Text/InputField，保證工具在任何環境下都可用。
4.  **清晰的錯誤處理分類**: 定義了 `validation_error`, `component_error`, `canvas_error` 等不同類型的錯誤，有助於客戶端判斷錯誤原因。

## 3. 缺點與風險 (Cons & Risks)

1.  **EventSystem 對新輸入系統的相容性 (Legacy vs New Input System)**:
    *   **問題**: `UGUIToolUtils.EnsureEventSystem` 目前預設添加 `StandaloneInputModule`。
    *   **風險**: 如果專案使用了 Unity 的 **New Input System** (且禁用了 Legacy Input Manager)，`StandaloneInputModule` 會報錯或無法運作。應該檢測環境並優先使用 `InputSystemUIInputModule`。

2.  **Dropdown 缺乏 Template (無法運作)**:
    *   **問題**: 正如 Request 中自我評估所述，`CreateDropdown` 只有建立了 Header (Label, Arrow)，但沒有建立關鍵的 `Template` 子物件結構。
    *   **風險**: 這樣的 Dropdown 在 Runtime 點擊時無法展開選項，屬於「半成品」。雖然實作 Template 很繁瑣，但對於「建立 UI 元素」的工具來說是必要的。

3.  **TMPro 複雜組件的潛在破壞**:
    *   **問題**: `TMP_InputField` 和 `TMP_Dropdown` 對子物件結構有特定要求 (例如 `TextViewport` 等)。目前的 `CreateInputFieldTMP` 註解提到是 "simplified version"。
    *   **風險**: 簡化的結構可能導致 TMPro 組件報錯或顯示異常。

4.  **硬編碼的樣式數值**:
    *   許多預設值 (Color, SizeDelta) 是硬編碼的 (Hardcoded)。雖然這在初期是可以接受的，但未來難以維護一致的 Design System。

## 4. 改善建議 (Improvement Suggestions)

### 建議 1: 增強 EventSystem 的 Input System 偵測

使用 Reflection 檢查是否支援 New Input System，動態決定添加哪個 Input Module。

### 建議 2: 補全 Dropdown Template 結構

雖然繁瑣，但應該為 Dropdown 產生一個最小可用的 `Template` 結構，否則該工具產生的 Dropdown 毫無用處。

---

## Refactor Prompt

根據 `doc/codeReview/Response_20260203_UGUITools.md` 的審查意見，請執行以下修正：

1.  **修正 `EnsureEventSystem`**: 修改 `UGUIToolUtils.cs`，使用 Reflection 偵測並支援 `InputSystemUIInputModule`。
2.  **實作 `CreateDropdownTemplate`**: 在 `CreateUIElementTool.cs` 中增加建立 Dropdown Template 的邏輯，使其在 Runtime 可運作。

### 涉及檔案
- `Editor/Tools/UGUITools.cs`

### 具體修改片段 (Code Snippets)

#### 1. UGUIToolUtils.EnsureEventSystem (支援 New Input System)

```csharp
public static EventSystem EnsureEventSystem()
{
    EventSystem eventSystem = UnityEngine.Object.FindObjectOfType<EventSystem>();
    if (eventSystem == null)
    {
        GameObject eventSystemGO = new GameObject("EventSystem");
        Undo.RegisterCreatedObjectUndo(eventSystemGO, "Create EventSystem");
        eventSystem = eventSystemGO.AddComponent<EventSystem>();

        // 嘗試偵測並添加 InputSystemUIInputModule (New Input System)
        Type inputModuleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
        if (inputModuleType != null)
        {
            Undo.AddComponent(eventSystemGO, inputModuleType);
        }
        else
        {
            // Fallback to Legacy Input System
            eventSystemGO.AddComponent<StandaloneInputModule>();
        }
    }
    return eventSystem;
}
```

#### 2. CreateUIElementTool.CreateDropdown (增加 Template 和 Scrollbar)

請在 `CreateDropdown` 方法末尾呼叫一個新的 `CreateDropdownTemplate` 方法：

```csharp
private void CreateDropdown(GameObject go, JObject data)
{
    // ... (原有代碼保持不變) ...

    dropdown.RefreshShownValue();

    // [新增] 建立 Template 結構
    CreateDropdownTemplate(go, dropdown);

    // ... (原有代碼: 設定 SizeDelta) ...
}

// [新增方法] 建立 Dropdown 的 Template 結構
private void CreateDropdownTemplate(GameObject dropdownGO, Dropdown dropdown)
{
    // 1. Template Object
    GameObject template = new GameObject("Template");
    Undo.RegisterCreatedObjectUndo(template, "Create Dropdown Template");
    template.transform.SetParent(dropdownGO.transform, false);
    template.SetActive(false); // 默認隱藏

    // Template RectTransform
    RectTransform templateRect = template.AddComponent<RectTransform>();
    templateRect.anchorMin = new Vector2(0, 0);
    templateRect.anchorMax = new Vector2(1, 0);
    templateRect.pivot = new Vector2(0.5f, 1);
    templateRect.anchoredPosition = new Vector2(0, 2);
    templateRect.sizeDelta = new Vector2(0, 150);

    // Template Image
    Image templateImage = template.AddComponent<Image>();
    templateImage.color = Color.white;

    // Template ScrollRect
    ScrollRect scrollRect = template.AddComponent<ScrollRect>();
    scrollRect.content = null; // 稍後設置
    scrollRect.viewport = null; // 稍後設置
    scrollRect.horizontal = false;
    scrollRect.movementType = ScrollRect.MovementType.Clamped;
    scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
    scrollRect.verticalScrollbarSpacing = -3;

    // 2. Viewport
    GameObject viewport = new GameObject("Viewport");
    Undo.RegisterCreatedObjectUndo(viewport, "Create Dropdown Viewport");
    viewport.transform.SetParent(template.transform, false);
    
    RectTransform viewportRect = viewport.AddComponent<RectTransform>();
    viewportRect.anchorMin = Vector2.zero;
    viewportRect.anchorMax = Vector2.one;
    viewportRect.sizeDelta = new Vector2(-18, 0); // 留出 Scrollbar 空間
    viewportRect.pivot = new Vector2(0, 1);

    viewport.AddComponent<Image>().type = Image.Type.Sliced; // Mask 需要
    viewport.AddComponent<Mask>().showMaskGraphic = false;

    // 3. Content
    GameObject content = new GameObject("Content");
    Undo.RegisterCreatedObjectUndo(content, "Create Dropdown Content");
    content.transform.SetParent(viewport.transform, false);

    RectTransform contentRect = content.AddComponent<RectTransform>();
    contentRect.anchorMin = new Vector2(0, 1);
    contentRect.anchorMax = new Vector2(1, 1);
    contentRect.pivot = new Vector2(0.5f, 1);
    contentRect.sizeDelta = new Vector2(0, 28);
    contentRect.anchoredPosition = Vector2.zero;

    // 4. Item
    GameObject item = new GameObject("Item");
    Undo.RegisterCreatedObjectUndo(item, "Create Dropdown Item");
    item.transform.SetParent(content.transform, false);

    RectTransform itemRect = item.AddComponent<RectTransform>();
    itemRect.anchorMin = new Vector2(0, 0.5f);
    itemRect.anchorMax = new Vector2(1, 0.5f);
    itemRect.sizeDelta = new Vector2(0, 20);

    Toggle itemToggle = item.AddComponent<Toggle>();
    
    // Item Background
    GameObject itemBg = new GameObject("Item Background");
    Undo.RegisterCreatedObjectUndo(itemBg, "Create Item Background");
    itemBg.transform.SetParent(item.transform, false);
    RectTransform itemBgRect = itemBg.AddComponent<RectTransform>();
    itemBgRect.anchorMin = Vector2.zero;
    itemBgRect.anchorMax = Vector2.one;
    itemBgRect.sizeDelta = Vector2.zero;
    Image itemBgImage = itemBg.AddComponent<Image>();
    itemBgImage.color = new Color(0.96f, 0.96f, 0.96f);

    // Item Checkmark
    GameObject itemCheck = new GameObject("Item Checkmark");
    Undo.RegisterCreatedObjectUndo(itemCheck, "Create Item Checkmark");
    itemCheck.transform.SetParent(item.transform, false);
    RectTransform itemCheckRect = itemCheck.AddComponent<RectTransform>();
    itemCheckRect.anchorMin = new Vector2(0, 0.5f);
    itemCheckRect.anchorMax = new Vector2(0, 0.5f);
    itemCheckRect.sizeDelta = new Vector2(20, 20);
    itemCheckRect.anchoredPosition = new Vector2(10, 0);
    Image itemCheckImage = itemCheck.AddComponent<Image>();
    itemCheckImage.color = Color.black;

    // Item Label
    GameObject itemLabel = new GameObject("Item Label");
    Undo.RegisterCreatedObjectUndo(itemLabel, "Create Item Label");
    itemLabel.transform.SetParent(item.transform, false);
    RectTransform itemLabelRect = itemLabel.AddComponent<RectTransform>();
    itemLabelRect.anchorMin = Vector2.zero;
    itemLabelRect.anchorMax = Vector2.one;
    itemLabelRect.offsetMin = new Vector2(20, 0); 
    itemLabelRect.offsetMax = Vector2.zero;
    Text itemLabelText = itemLabel.AddComponent<Text>();
    itemLabelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    itemLabelText.color = Color.black;
    itemLabelText.alignment = TextAnchor.MiddleLeft;

    // Config Item Toggle
    itemToggle.targetGraphic = itemBgImage;
    itemToggle.graphic = itemCheckImage;
    itemToggle.isOn = true;

    // 5. Scrollbar
    GameObject scrollbar = new GameObject("Scrollbar");
    Undo.RegisterCreatedObjectUndo(scrollbar, "Create Dropdown Scrollbar");
    scrollbar.transform.SetParent(template.transform, false);
    
    RectTransform scrollbarRect = scrollbar.AddComponent<RectTransform>();
    scrollbarRect.anchorMin = new Vector2(1, 0);
    scrollbarRect.anchorMax = new Vector2(1, 1);
    scrollbarRect.pivot = Vector2.one;
    scrollbarRect.sizeDelta = new Vector2(20, 0);

    Scrollbar scrollbarComp = scrollbar.AddComponent<Scrollbar>();
    scrollbarComp.direction = Scrollbar.Direction.BottomToTop;
    
    // Scrollbar Sliding Area
    GameObject slidingArea = new GameObject("Sliding Area");
    Undo.RegisterCreatedObjectUndo(slidingArea, "Create Sliding Area");
    slidingArea.transform.SetParent(scrollbar.transform, false);
    RectTransform slidingRect = slidingArea.AddComponent<RectTransform>();
    slidingRect.anchorMin = Vector2.zero;
    slidingRect.anchorMax = Vector2.one;
    slidingRect.sizeDelta = new Vector2(-20, -20);
    
    // Scrollbar Handle
    GameObject handle = new GameObject("Handle");
    Undo.RegisterCreatedObjectUndo(handle, "Create Handle");
    handle.transform.SetParent(slidingArea.transform, false);
    RectTransform handleRect = handle.AddComponent<RectTransform>();
    handleRect.sizeDelta = new Vector2(20, 20);
    Image handleImage = handle.AddComponent<Image>();
    handleImage.color = new Color(0.5f, 0.5f, 0.5f);
    
    scrollbarComp.handleRect = handleRect;
    scrollbarComp.targetGraphic = handleImage;

    // Link everything to Dropdown
    dropdown.template = templateRect;
    dropdown.itemText = itemLabelText;
    
    scrollRect.content = contentRect;
    scrollRect.viewport = viewportRect;
    scrollRect.verticalScrollbar = scrollbarComp;
}
```

---

⚠️ **完成後請更新 Implementation Tracker**：

請在 `doc/requirement/feature_ugui_tools_tracker.md` (若不存在請建立或請示使用者) 對應 Phase 的「關鍵決策」區塊中，
新增 `[Review Fix]` 標籤記錄本次修改內容，並在「關聯審查」區塊連結本審查報告。
