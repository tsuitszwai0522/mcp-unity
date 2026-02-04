# Code Review Request: UGUI Tools Implementation

**日期**: 2026-02-03
**功能**: Unity UGUI 工具實作
**作者**: Claude Opus 4.5

---

## 1. 背景與目標

### 背景
MCP Unity 目前缺乏對 Unity UI 系統（UGUI）的支援，AI 客戶端無法直接建立或修改 UI 元素。

### 目標
為 MCP Unity 添加 5 個工具來支援 Unity UI 系統操作：
- 建立 Canvas 和 UI 元素
- 修改 RectTransform 屬性
- 添加 Layout 組件
- 查詢 UI 元素資訊

---

## 2. 變更摘要

### 新增檔案

| 檔案 | 行數 | 說明 |
|------|------|------|
| `Editor/Tools/UGUITools.cs` | ~1400 | C# 實作，包含 5 個工具類和共用工具類 |
| `Server~/src/tools/uguiTools.ts` | ~380 | TypeScript 實作，Zod schemas 和註冊函數 |

### 修改檔案

| 檔案 | 變更 |
|------|------|
| `Editor/UnityBridge/McpUnityServer.cs` | +15 行：在 `RegisterTools()` 中新增 5 個 UGUI 工具註冊 |
| `Server~/src/index.ts` | +3 行：新增 import 和 `registerUGUITools()` 調用 |

### 新增工具

| Tool Name | 用途 |
|-----------|------|
| `create_canvas` | 建立 Canvas（含 CanvasScaler、GraphicRaycaster、EventSystem） |
| `create_ui_element` | 建立 14 種 UI 元素（Button, Text, TextMeshPro, Image, Panel 等） |
| `set_rect_transform` | 修改 RectTransform 屬性（anchors, pivot, position, size, rotation, scale） |
| `add_layout_component` | 添加 6 種 Layout 組件（LayoutGroup, ContentSizeFitter 等） |
| `get_ui_element_info` | 獲取 UI 元素詳細資訊，支援遞迴查詢子元素 |

---

## 3. 關鍵代碼

### 3.1 Anchor Presets 定義
**檔案**: `Editor/Tools/UGUITools.cs:24-50`

```csharp
public static readonly Dictionary<string, (Vector2 min, Vector2 max, Vector2 pivot)> AnchorPresets =
    new Dictionary<string, (Vector2, Vector2, Vector2)>
    {
        { "topLeft", (new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1)) },
        { "middleCenter", (new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f)) },
        { "stretch", (new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f)) },
        // ... 16 種預設
    };
```

### 3.2 TextMeshPro 檢測與 Fallback
**檔案**: `Editor/Tools/UGUITools.cs:107-111`

```csharp
public static bool IsTMProAvailable()
{
    return Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro") != null;
}
```

### 3.3 UI 元素建立（以 Button 為例）
**檔案**: `Editor/Tools/UGUITools.cs:480-530`

```csharp
private void CreateButton(GameObject go, JObject data)
{
    Image image = go.GetComponent<Image>();
    if (image == null)
    {
        image = Undo.AddComponent<Image>(go);
        image.color = new Color(1, 1, 1, 1);
    }

    Button button = go.GetComponent<Button>();
    if (button == null)
    {
        button = Undo.AddComponent<Button>(go);
    }

    // 建立子文字元素
    string buttonText = data?["text"]?.ToObject<string>() ?? "Button";
    GameObject textGO = new GameObject("Text");
    Undo.RegisterCreatedObjectUndo(textGO, "Create Button Text");
    // ...
}
```

### 3.4 TypeScript Zod Schema
**檔案**: `Server~/src/tools/uguiTools.ts:17-30`

```typescript
const anchorPresetEnum = z.enum([
  "topLeft", "topCenter", "topRight", "topStretch",
  "middleLeft", "middleCenter", "middleRight", "middleStretch",
  "bottomLeft", "bottomCenter", "bottomRight", "bottomStretch",
  "stretchLeft", "stretchCenter", "stretchRight", "stretch"
]).describe("Anchor preset name");
```

---

## 4. 自我評估

### 4.1 已知脆弱點

| 風險等級 | 問題 | 說明 | 建議處理 |
|----------|------|------|----------|
| 🟡 中 | TMPro InputField 實作不完整 | `CreateInputFieldTMP()` 僅添加組件，未建立完整子結構 | 建議使用 prefab 實例化或完善子元素建立 |
| 🟡 中 | TMPro Dropdown 實作不完整 | `CreateDropdownTMP()` 同上問題 | 同上 |
| 🟢 低 | Dropdown Template 未建立 | 建立的 Dropdown 沒有 Template，運行時無法展開選項 | 未來可加入 Template 建立邏輯 |
| 🟢 低 | ScrollView 無 Scrollbar | 建立的 ScrollView 未附帶 Scrollbar | 可選參數添加 Scrollbar |

### 4.2 Edge Cases

| 情境 | 處理方式 | 測試狀態 |
|------|----------|----------|
| 在非 Canvas 下建立 UI 元素 | 自動在根物件添加 Canvas 組件 | ⚠️ 待測試 |
| 重複建立 Canvas | 返回錯誤 "Canvas already exists" | ⚠️ 待測試 |
| 無效的 anchor preset 名稱 | 返回 validation_error | ⚠️ 待測試 |
| TMPro 未安裝時建立 TextMeshPro | Fallback 至 legacy Text，返回 usedFallback=true | ⚠️ 待測試 |
| instanceId 和 objectPath 都未提供 | 返回 validation_error | ✅ 已實作 |

### 4.3 效能考量

- **大量 UI 建立**：每個工具呼叫都會觸發 `EditorUtility.SetDirty()`，批量建立時建議使用 `batch_execute`
- **遞迴查詢**：`get_ui_element_info` 的 `includeChildren=true` 在深層階層時可能產生大量資料

---

## 5. 審查重點

### 請重點審查以下區域：

1. **Undo 支援完整性** (`Editor/Tools/UGUITools.cs`)
   - 所有建立的 GameObject 是否都有 `Undo.RegisterCreatedObjectUndo()`？
   - 所有修改是否都有 `Undo.RecordObject()`？

2. **錯誤處理一致性**
   - 錯誤類型是否正確（validation_error, not_found_error, component_error, canvas_error）？
   - 錯誤訊息是否足夠描述問題？

3. **TypeScript/C# 參數對應**
   - Zod schema 是否與 C# 參數提取一致？
   - 可選參數的預設值是否兩端一致？

4. **UI 元素建立邏輯**
   - 元件添加順序是否正確？
   - RectTransform 預設值是否合理？

---

## 6. 文檔一致性檢查

| 項目 | 狀態 | 說明 |
|------|------|------|
| CLAUDE.md | ✅ | 已描述 Tool/Resource 添加流程 |
| README.md | ⚠️ | 可能需要更新工具列表 |
| CHANGELOG.md | ❌ | 需要添加此功能的變更記錄 |

---

## 7. 測試清單

- [ ] 建立 Canvas（ScreenSpaceOverlay）
- [ ] 建立 Canvas（ScreenSpaceCamera + 指定相機）
- [ ] 建立 Canvas（WorldSpace）
- [ ] 建立 Button 並驗證子 Text 元素
- [ ] 建立 Text 並設定 fontSize、color
- [ ] 建立 TextMeshPro（若有安裝 TMPro）
- [ ] 建立 Image、RawImage、Panel
- [ ] 建立 InputField 並驗證 placeholder
- [ ] 建立 Toggle、Slider、Dropdown
- [ ] 建立 ScrollView、Scrollbar
- [ ] 套用各種 anchor presets
- [ ] 使用 set_rect_transform 修改位置和大小
- [ ] 添加 HorizontalLayoutGroup 並設定 padding/spacing
- [ ] 添加 VerticalLayoutGroup
- [ ] 添加 GridLayoutGroup 並設定 cellSize
- [ ] 添加 ContentSizeFitter
- [ ] 添加 LayoutElement
- [ ] 使用 get_ui_element_info 查詢單一元素
- [ ] 使用 get_ui_element_info 遞迴查詢子元素
- [ ] 使用 batch_execute 建立完整 UI hierarchy
- [ ] 驗證 Undo 功能（Ctrl+Z 撤銷所有操作）
- [ ] TMPro 未安裝時的 fallback 行為

---

## 8. 相關資源

- **Plan 文件**: 原始實作計畫
- **參考實作**: `Editor/Tools/UpdateGameObjectTool.cs`、`Editor/Tools/UpdateComponentTool.cs`
