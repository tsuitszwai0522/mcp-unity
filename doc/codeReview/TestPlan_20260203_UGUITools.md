# UGUI Tools 完整測試計畫

**日期**: 2026-02-03
**測試範圍**: UGUI Tools (`create_canvas`, `create_ui_element`, `set_rect_transform`, `add_layout_component`, `get_ui_element_info`)

---

## 1. 環境準備

### 1.1 前置需求

- Unity 2022.3+ (建議 Unity 6)
- Node.js 18+
- npm 9+

### 1.2 Unity 專案設定

1. **開啟 Unity 專案**
   ```
   開啟包含 MCP Unity package 的 Unity 專案
   ```

2. **確認 MCP Unity Server 啟動**
   - 選單：`Tools > MCP Unity > Server Window`
   - 確認 WebSocket Server 狀態為 **Running** (預設 port 8090)
   - 若未啟動，點擊 `Start Server`

3. **建立測試 Scene**
   - `File > New Scene` (選擇 Basic 或 Empty)
   - `File > Save As...` → `Assets/Scenes/UGUITest.unity`

### 1.3 Node Server 設定

```bash
# 進入 Server 目錄
cd /Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~

# 安裝依賴 (若尚未安裝)
npm install

# 編譯 TypeScript
npm run build
```

---

## 2. 測試方式

### 方式 A：MCP Inspector (推薦用於開發測試)

```bash
cd /Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~
npm run inspector
```

這會開啟 MCP Inspector 網頁介面，可直接呼叫工具並查看結果。

### 方式 B：直接執行 Server + MCP Client

```bash
cd /Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~
npm start
```

然後使用支援 MCP 的客戶端（如 Claude Code、Cursor）連接。

---

## 3. 測試案例

### 3.1 create_canvas

#### Test 3.1.1：建立基本 Canvas (ScreenSpaceOverlay)

**輸入**:
```json
{
  "objectPath": "TestCanvas"
}
```

**預期結果**:
- ✅ 場景中建立 `TestCanvas` GameObject
- ✅ 包含 Canvas 組件 (renderMode = ScreenSpaceOverlay)
- ✅ 包含 CanvasScaler 組件
- ✅ 包含 GraphicRaycaster 組件
- ✅ 自動建立 EventSystem (若不存在)
- ✅ 回傳 success = true, instanceId, path

**Unity 驗證**:
```
Hierarchy 應顯示：
├── TestCanvas (Canvas, CanvasScaler, GraphicRaycaster)
└── EventSystem (EventSystem, StandaloneInputModule 或 InputSystemUIInputModule)
```

---

#### Test 3.1.2：建立 ScreenSpaceCamera Canvas

**輸入**:
```json
{
  "objectPath": "CameraCanvas",
  "renderMode": "ScreenSpaceCamera",
  "sortingOrder": 10,
  "scaler": {
    "uiScaleMode": "ScaleWithScreenSize",
    "referenceResolution": { "x": 1920, "y": 1080 },
    "screenMatchMode": "MatchWidthOrHeight",
    "matchWidthOrHeight": 0.5
  }
}
```

**預期結果**:
- ✅ Canvas.renderMode = ScreenSpaceCamera
- ✅ Canvas.worldCamera = Main Camera (自動尋找)
- ✅ Canvas.sortingOrder = 10
- ✅ CanvasScaler 設定正確

---

#### Test 3.1.3：重複建立 Canvas (錯誤處理)

**輸入**:
```json
{
  "objectPath": "TestCanvas"
}
```
(在已有 TestCanvas 的情況下再次執行)

**預期結果**:
- ❌ 回傳 error，類型為 `validation_error`
- ❌ 訊息包含 "Canvas already exists"

---

### 3.2 create_ui_element

#### Test 3.2.1：建立 Button

**輸入**:
```json
{
  "objectPath": "TestCanvas/MyButton",
  "elementType": "Button",
  "rectTransform": {
    "anchorPreset": "middleCenter",
    "sizeDelta": { "x": 200, "y": 50 }
  },
  "elementData": {
    "text": "Click Me",
    "fontSize": 18
  }
}
```

**預期結果**:
- ✅ 建立 `MyButton` 作為 TestCanvas 的子物件
- ✅ 包含 Image、Button 組件
- ✅ 子物件 `Text` 顯示 "Click Me"
- ✅ RectTransform 置中，大小 200x50

**Unity 驗證**:
```
Hierarchy:
└── TestCanvas
    └── MyButton (Image, Button)
        └── Text (Text)

在 Game View 中應能看到按鈕
```

---

#### Test 3.2.2：建立 Text

**輸入**:
```json
{
  "objectPath": "TestCanvas/Title",
  "elementType": "Text",
  "rectTransform": {
    "anchorPreset": "topCenter",
    "anchoredPosition": { "x": 0, "y": -50 },
    "sizeDelta": { "x": 300, "y": 60 }
  },
  "elementData": {
    "text": "UGUI Test",
    "fontSize": 32,
    "color": { "r": 0.2, "g": 0.4, "b": 0.8, "a": 1 },
    "alignment": "MiddleCenter"
  }
}
```

**預期結果**:
- ✅ 建立 Text 元素
- ✅ 文字為 "UGUI Test"，藍色，32px，置中

---

#### Test 3.2.3：建立 Panel + 子元素

**輸入** (依序執行):

**Step 1 - 建立 Panel**:
```json
{
  "objectPath": "TestCanvas/MainPanel",
  "elementType": "Panel",
  "rectTransform": {
    "anchorPreset": "middleCenter",
    "sizeDelta": { "x": 400, "y": 300 }
  },
  "elementData": {
    "color": { "r": 0.1, "g": 0.1, "b": 0.1, "a": 0.8 }
  }
}
```

**Step 2 - 在 Panel 內建立 Button**:
```json
{
  "objectPath": "TestCanvas/MainPanel/SubmitBtn",
  "elementType": "Button",
  "rectTransform": {
    "anchorPreset": "bottomCenter",
    "anchoredPosition": { "x": 0, "y": 30 },
    "sizeDelta": { "x": 120, "y": 40 }
  },
  "elementData": {
    "text": "Submit"
  }
}
```

**預期結果**:
- ✅ Panel 和 Button 正確建立為父子關係
- ✅ Button 相對於 Panel 定位

---

#### Test 3.2.4：建立 InputField

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel/NameInput",
  "elementType": "InputField",
  "rectTransform": {
    "anchorPreset": "topCenter",
    "anchoredPosition": { "x": 0, "y": -50 },
    "sizeDelta": { "x": 300, "y": 40 }
  },
  "elementData": {
    "placeholder": "Enter your name..."
  }
}
```

**預期結果**:
- ✅ InputField 包含 Text Area、Placeholder、Text 子物件
- ✅ 在 Play Mode 可輸入文字

---

#### Test 3.2.5：建立 Dropdown

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel/LanguageDropdown",
  "elementType": "Dropdown",
  "rectTransform": {
    "anchorPreset": "topCenter",
    "anchoredPosition": { "x": 0, "y": -100 },
    "sizeDelta": { "x": 200, "y": 35 }
  },
  "elementData": {
    "options": ["English", "繁體中文", "日本語", "한국어"],
    "value": 1
  }
}
```

**預期結果**:
- ✅ Dropdown 顯示 "繁體中文" (index 1)
- ✅ **重要**: 在 Play Mode 點擊可展開選項列表
- ✅ 可選擇其他選項

**Unity 驗證**:
```
Hierarchy:
└── LanguageDropdown
    ├── Label
    ├── Arrow
    └── Template (inactive)
        ├── Viewport
        │   └── Content
        │       └── Item
        └── Scrollbar
```

---

#### Test 3.2.6：建立 Toggle

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel/RememberMe",
  "elementType": "Toggle",
  "rectTransform": {
    "anchorPreset": "middleLeft",
    "anchoredPosition": { "x": 50, "y": 0 },
    "sizeDelta": { "x": 160, "y": 25 }
  },
  "elementData": {
    "text": "Remember Me",
    "isOn": true
  }
}
```

---

#### Test 3.2.7：建立 Slider

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel/VolumeSlider",
  "elementType": "Slider",
  "rectTransform": {
    "anchorPreset": "bottomCenter",
    "anchoredPosition": { "x": 0, "y": 80 },
    "sizeDelta": { "x": 250, "y": 25 }
  },
  "elementData": {
    "value": 0.7,
    "minValue": 0,
    "maxValue": 1
  }
}
```

---

#### Test 3.2.8：建立 TextMeshPro (若有安裝)

**輸入**:
```json
{
  "objectPath": "TestCanvas/TMPText",
  "elementType": "TextMeshPro",
  "rectTransform": {
    "anchorPreset": "bottomCenter",
    "anchoredPosition": { "x": 0, "y": 50 },
    "sizeDelta": { "x": 300, "y": 50 }
  },
  "elementData": {
    "text": "TextMeshPro Test",
    "fontSize": 24
  }
}
```

**預期結果 (TMPro 已安裝)**:
- ✅ 建立 TextMeshProUGUI 組件
- ✅ usedFallback = false

**預期結果 (TMPro 未安裝)**:
- ✅ Fallback 至 legacy Text
- ✅ usedFallback = true
- ✅ 訊息提示 TMPro 未安裝

---

### 3.3 set_rect_transform

#### Test 3.3.1：使用 Anchor Preset

**輸入**:
```json
{
  "objectPath": "TestCanvas/MyButton",
  "anchorPreset": "bottomRight",
  "anchoredPosition": { "x": -20, "y": 20 }
}
```

**預期結果**:
- ✅ Button 移動到右下角
- ✅ anchorMin = (1, 0), anchorMax = (1, 0), pivot = (1, 0)

---

#### Test 3.3.2：手動設定 Anchors

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel",
  "anchorMin": { "x": 0.1, "y": 0.1 },
  "anchorMax": { "x": 0.9, "y": 0.9 },
  "offsetMin": { "x": 0, "y": 0 },
  "offsetMax": { "x": 0, "y": 0 }
}
```

**預期結果**:
- ✅ Panel 延展至父物件 10%~90% 的區域

---

#### Test 3.3.3：設定旋轉和縮放

**輸入**:
```json
{
  "objectPath": "TestCanvas/MyButton",
  "rotation": { "x": 0, "y": 0, "z": 15 },
  "localScale": { "x": 1.2, "y": 1.2, "z": 1 }
}
```

---

### 3.4 add_layout_component

#### Test 3.4.1：HorizontalLayoutGroup

**Step 1 - 建立容器**:
```json
{
  "objectPath": "TestCanvas/ButtonRow",
  "elementType": "Panel",
  "rectTransform": {
    "anchorPreset": "bottomStretch",
    "sizeDelta": { "x": 0, "y": 60 }
  }
}
```

**Step 2 - 添加 Layout**:
```json
{
  "objectPath": "TestCanvas/ButtonRow",
  "layoutType": "HorizontalLayoutGroup",
  "layoutData": {
    "padding": { "left": 10, "right": 10, "top": 5, "bottom": 5 },
    "spacing": 15,
    "childAlignment": "MiddleCenter",
    "childControlWidth": false,
    "childForceExpandWidth": false
  }
}
```

**Step 3 - 添加子 Button** (重複 3 次):
```json
{
  "objectPath": "TestCanvas/ButtonRow/Btn1",
  "elementType": "Button",
  "rectTransform": { "sizeDelta": { "x": 80, "y": 40 } },
  "elementData": { "text": "Btn 1" }
}
```

**預期結果**:
- ✅ 3 個按鈕水平排列，間距 15px
- ✅ 邊距正確

---

#### Test 3.4.2：VerticalLayoutGroup

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel",
  "layoutType": "VerticalLayoutGroup",
  "layoutData": {
    "padding": { "left": 20, "right": 20, "top": 20, "bottom": 20 },
    "spacing": 10,
    "childAlignment": "UpperCenter",
    "childControlHeight": false,
    "childForceExpandHeight": false
  }
}
```

---

#### Test 3.4.3：GridLayoutGroup

**Step 1 - 建立 Grid 容器**:
```json
{
  "objectPath": "TestCanvas/IconGrid",
  "elementType": "Panel",
  "rectTransform": {
    "anchorPreset": "middleCenter",
    "sizeDelta": { "x": 340, "y": 340 }
  }
}
```

**Step 2 - 添加 GridLayout**:
```json
{
  "objectPath": "TestCanvas/IconGrid",
  "layoutType": "GridLayoutGroup",
  "layoutData": {
    "cellSize": { "x": 100, "y": 100 },
    "spacing": { "x": 10, "y": 10 },
    "startCorner": "UpperLeft",
    "startAxis": "Horizontal",
    "childAlignment": "MiddleCenter",
    "constraint": "FixedColumnCount",
    "constraintCount": 3
  }
}
```

**Step 3 - 添加 9 個 Image**:
```json
{
  "objectPath": "TestCanvas/IconGrid/Icon1",
  "elementType": "Image",
  "elementData": { "color": { "r": 1, "g": 0, "b": 0, "a": 1 } }
}
```
(重複建立 Icon2~Icon9，使用不同顏色)

**預期結果**:
- ✅ 9 個 100x100 的方塊排列成 3x3 網格

---

#### Test 3.4.4：ContentSizeFitter

**輸入**:
```json
{
  "objectPath": "TestCanvas/Title",
  "layoutType": "ContentSizeFitter",
  "layoutData": {
    "horizontalFit": "PreferredSize",
    "verticalFit": "PreferredSize"
  }
}
```

**預期結果**:
- ✅ Text 元素大小自動適應文字內容

---

### 3.5 get_ui_element_info

#### Test 3.5.1：查詢單一元素

**輸入**:
```json
{
  "objectPath": "TestCanvas/MainPanel"
}
```

**預期結果**:
- ✅ 回傳 RectTransform 資訊 (anchors, pivot, size 等)
- ✅ 回傳 UI 組件列表 (Image 等)
- ✅ 回傳 Layout 組件資訊 (若有)

---

#### Test 3.5.2：遞迴查詢子元素

**輸入**:
```json
{
  "objectPath": "TestCanvas",
  "includeChildren": true
}
```

**預期結果**:
- ✅ 回傳 TestCanvas 及所有子元素的完整資訊
- ✅ 結構為巢狀 JSON

---

### 3.6 batch_execute (綜合測試)

**輸入**:
```json
{
  "operations": [
    {
      "tool": "create_canvas",
      "params": { "objectPath": "BatchTestCanvas" }
    },
    {
      "tool": "create_ui_element",
      "params": {
        "objectPath": "BatchTestCanvas/Header",
        "elementType": "Panel",
        "rectTransform": { "anchorPreset": "topStretch", "sizeDelta": { "x": 0, "y": 80 } },
        "elementData": { "color": { "r": 0.2, "g": 0.2, "b": 0.3, "a": 1 } }
      }
    },
    {
      "tool": "add_layout_component",
      "params": {
        "objectPath": "BatchTestCanvas/Header",
        "layoutType": "HorizontalLayoutGroup",
        "layoutData": { "padding": { "left": 20, "right": 20 }, "spacing": 10, "childAlignment": "MiddleLeft" }
      }
    },
    {
      "tool": "create_ui_element",
      "params": {
        "objectPath": "BatchTestCanvas/Header/Logo",
        "elementType": "Image",
        "rectTransform": { "sizeDelta": { "x": 60, "y": 60 } }
      }
    },
    {
      "tool": "create_ui_element",
      "params": {
        "objectPath": "BatchTestCanvas/Header/Title",
        "elementType": "Text",
        "rectTransform": { "sizeDelta": { "x": 200, "y": 40 } },
        "elementData": { "text": "My App", "fontSize": 28 }
      }
    }
  ],
  "atomic": true
}
```

**預期結果**:
- ✅ 一次呼叫建立完整 Header UI 結構
- ✅ 所有元素正確建立和配置

---

## 4. Undo 測試

在執行完以上測試後：

1. 在 Unity 中按 `Ctrl+Z` (Windows) 或 `Cmd+Z` (Mac)
2. **預期**: 每次 Undo 應撤銷一個完整的工具操作
3. 重複 Undo 直到場景恢復初始狀態
4. 按 `Ctrl+Y` / `Cmd+Shift+Z` 測試 Redo

---

## 5. EventSystem Input System 測試

### 5.1 Legacy Input Manager 專案

**設定**: `Edit > Project Settings > Player > Active Input Handling = Input Manager (Old)`

**執行**: 建立 Canvas (會自動建立 EventSystem)

**預期結果**:
- ✅ EventSystem 包含 `StandaloneInputModule` 組件

### 5.2 New Input System 專案

**設定**:
1. 安裝 Input System package
2. `Edit > Project Settings > Player > Active Input Handling = Input System Package (New)`

**執行**: 建立 Canvas

**預期結果**:
- ✅ EventSystem 包含 `InputSystemUIInputModule` 組件

---

## 6. 錯誤處理測試

| 測試案例 | 輸入 | 預期錯誤 |
|----------|------|----------|
| 缺少必要參數 | `create_canvas` 無 objectPath | validation_error |
| 無效元素類型 | `elementType: "InvalidType"` | validation_error |
| 無效 anchor preset | `anchorPreset: "invalid"` | validation_error |
| 找不到 GameObject | `objectPath: "NonExistent"` for set_rect_transform | not_found_error |
| 非 UI 元素設定 RectTransform | 對 3D GameObject 呼叫 set_rect_transform | component_error |

---

## 7. 測試完成檢查清單

### 基本功能
- [ ] create_canvas - ScreenSpaceOverlay
- [ ] create_canvas - ScreenSpaceCamera
- [ ] create_canvas - WorldSpace
- [ ] create_canvas - 錯誤處理 (重複建立)

### UI 元素建立
- [ ] Button (含子 Text)
- [ ] Text
- [ ] TextMeshPro (或 fallback)
- [ ] Image
- [ ] RawImage
- [ ] Panel
- [ ] InputField (Play Mode 可輸入)
- [ ] Toggle (Play Mode 可切換)
- [ ] Slider (Play Mode 可拖動)
- [ ] Dropdown (Play Mode 可展開選擇)
- [ ] ScrollView
- [ ] Scrollbar

### RectTransform
- [ ] Anchor Presets (至少測試 5 種)
- [ ] 手動設定 anchors
- [ ] sizeDelta
- [ ] anchoredPosition
- [ ] rotation
- [ ] localScale

### Layout 組件
- [ ] HorizontalLayoutGroup
- [ ] VerticalLayoutGroup
- [ ] GridLayoutGroup
- [ ] ContentSizeFitter
- [ ] LayoutElement
- [ ] AspectRatioFitter

### 其他
- [ ] get_ui_element_info (單一)
- [ ] get_ui_element_info (含子元素)
- [ ] batch_execute
- [ ] Undo/Redo
- [ ] EventSystem (Legacy Input)
- [ ] EventSystem (New Input System)

---

## 8. 問題回報格式

如發現問題，請記錄：

```markdown
### 問題標題

**工具**: create_ui_element
**輸入參數**:
```json
{ ... }
```

**預期行為**: ...

**實際行為**: ...

**錯誤訊息** (若有):
```
...
```

**Unity Console 錯誤** (若有):
```
...
```

**截圖**: (若適用)
```
