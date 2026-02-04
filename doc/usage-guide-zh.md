# MCP Unity 完整使用指南

本指南涵蓋從安裝到測試 UGUI 功能的完整流程。

---

## 目錄

1. [環境需求](#1-環境需求)
2. [安裝 Unity Package](#2-安裝-unity-package)
3. [Build Node.js Server](#3-build-nodejs-server)
4. [啟動 Server](#4-啟動-server)
5. [配置 AI Client](#5-配置-ai-client)
6. [測試 UGUI Tools](#6-測試-ugui-tools)
7. [常見問題排解](#7-常見問題排解)

---

## 1. 環境需求

| 軟體 | 最低版本 | 建議版本 |
|------|----------|----------|
| Unity | 2022.3+ | Unity 6 |
| Node.js | 18+ | 20 LTS |
| npm | 9+ | 10+ |

### 檢查環境

```bash
# 檢查 Node.js 版本
node --version

# 檢查 npm 版本
npm --version
```

如未安裝 Node.js，請從 [nodejs.org](https://nodejs.org/) 下載安裝。

---

## 2. 安裝 Unity Package

### 方法 A：從 Git URL 安裝（推薦）

1. 在 Unity 中開啟 **Window > Package Manager**
2. 點擊左上角 **+** 按鈕
3. 選擇 **Add package from git URL...**
4. 輸入：
   ```
   https://github.com/CoderGamester/mcp-unity.git
   ```
5. 點擊 **Add**

### 方法 B：從本地資料夾安裝（開發用）

如果你已經 clone 了專案：

1. 在 Unity 中開啟 **Window > Package Manager**
2. 點擊左上角 **+** 按鈕
3. 選擇 **Add package from disk...**
4. 導航到你的 `mcp-unity` 資料夾
5. 選擇 `package.json` 檔案

### 驗證安裝

安裝成功後，你應該能在 Unity 選單中看到：
- **Tools > MCP Unity > Server Window**

---

## 3. Build Node.js Server

Node.js Server 位於 `Server~/` 目錄（`~` 表示 Unity 會忽略此資料夾）。

### 自動 Build（透過 Unity）

1. 在 Unity 中開啟 **Tools > MCP Unity > Server Window**
2. 如果 Server 尚未安裝，會自動執行 `npm install` 和 `npm run build`

### 手動 Build（命令列）

```bash
# 進入 Server 目錄
cd Server~

# 安裝依賴
npm install

# 編譯 TypeScript
npm run build

# （可選）監聽模式，自動重新編譯
npm run watch
```

### Build 輸出

編譯後的 JavaScript 檔案會在 `Server~/build/` 目錄：
- `build/index.js` - 主入口點
- `build/tools/` - 工具定義
- `build/resources/` - 資源定義

---

## 4. 啟動 Server

### 方法 A：從 Unity 啟動（推薦）

1. 開啟 **Tools > MCP Unity > Server Window**
2. 確認設定：
   - **Port**: `8090`（預設）
   - **Auto Start Server**: ✓（可選）
3. 點擊 **Start Server**

Server Window 會顯示連接狀態和日誌。

### 方法 B：從命令列啟動

```bash
cd Server~
npm start
```

或直接執行：
```bash
node Server~/build/index.js
```

### 驗證 Server 運行

Server 運行時會：
1. 在 Unity Console 顯示 `[MCP Unity] Server started on port 8090`
2. WebSocket 端點可用：`ws://localhost:8090/McpUnity`

---

## 5. 配置 AI Client

### Claude Code 配置（CLI）

Claude Code 使用 `.mcp.json` 檔案配置 MCP Server。

#### 方法 A：專案級配置（推薦）

在專案根目錄建立 `.mcp.json`：

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": ["/Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~/build/index.js"],
      "env": {
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

#### 方法 B：全域配置

在 `~/.claude.json` 加入 MCP Server 配置：

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": ["/Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~/build/index.js"],
      "env": {
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

#### 驗證連接

在 Claude Code 中執行：
```bash
/mcp
```
應該能看到 `mcp-unity` 已連接，並列出可用的 tools 和 resources。

---

### Google Antigravity 配置

Google Antigravity 支援 MCP 協議。配置步驟：

1. 開啟 [Google Antigravity](https://antigravity.google/)
2. 點擊 **Agent session**，選擇編輯器側邊面板頂部的 **...** 下拉選單
3. 選擇 **MCP Servers** 開啟 MCP Store
4. 點擊頂部的 **Manage MCP Servers**
5. 在主標籤頁點擊 **View raw config**
6. 在 `mcp_config.json` 中加入以下配置：

```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "node",
      "args": ["/Users/cyrus/Git/Personal/UnityMCP/mcp-unity/Server~/build/index.js"],
      "env": {
        "UNITY_PORT": "8090"
      }
    }
  }
}
```

7. 儲存配置並重新連接

> **注意**：確保 Unity Editor 已開啟且 MCP Server 正在運行，否則連接會失敗。

---

### 使用 MCP Inspector 除錯

MCP Inspector 是一個互動式介面，可直接測試工具和資源，非常適合除錯：

```bash
cd Server~
npm run inspector
```

Inspector 功能：
- 列出所有可用的 tools 和 resources
- 手動發送請求並查看回應
- 查看 WebSocket 連接狀態

---

## 6. 測試 UGUI Tools

根據 Code Review (`doc/codeReview/Response_20260203_UGUITools.md`) 中提到的功能，以下是測試步驟。

### 6.1 測試 Canvas 建立

**工具名稱**: `create_canvas`

透過 MCP Client 發送：
```json
{
  "method": "create_canvas",
  "params": {
    "name": "TestCanvas",
    "renderMode": "ScreenSpaceOverlay",
    "sortingOrder": 0
  }
}
```

**預期結果**:
- 場景中建立名為 "TestCanvas" 的 GameObject
- 包含 Canvas、CanvasScaler、GraphicRaycaster 組件
- 自動建立 EventSystem（如不存在）

### 6.2 測試 UI Element 建立

**工具名稱**: `create_ui_element`

#### 測試 Button
```json
{
  "method": "create_ui_element",
  "params": {
    "elementType": "Button",
    "name": "TestButton",
    "parentPath": "TestCanvas"
  }
}
```

#### 測試 Text（會自動使用 TextMeshPro 如已安裝）
```json
{
  "method": "create_ui_element",
  "params": {
    "elementType": "Text",
    "name": "TestText",
    "parentPath": "TestCanvas",
    "text": "Hello World"
  }
}
```

#### 測試 Dropdown（Code Review 重點）
```json
{
  "method": "create_ui_element",
  "params": {
    "elementType": "Dropdown",
    "name": "TestDropdown",
    "parentPath": "TestCanvas",
    "options": ["Option 1", "Option 2", "Option 3"]
  }
}
```

**驗證要點**（根據 Code Review）:
- 確認 Dropdown 包含完整的 Template 結構
- 進入 Play Mode 測試下拉功能是否正常

#### 測試 InputField
```json
{
  "method": "create_ui_element",
  "params": {
    "elementType": "InputField",
    "name": "TestInput",
    "parentPath": "TestCanvas",
    "placeholder": "Enter text..."
  }
}
```

### 6.3 測試 RectTransform 設定

**工具名稱**: `set_rect_transform`

```json
{
  "method": "set_rect_transform",
  "params": {
    "gameObjectPath": "TestCanvas/TestButton",
    "anchorPreset": "middleCenter",
    "sizeDelta": { "x": 200, "y": 50 },
    "anchoredPosition": { "x": 0, "y": 100 }
  }
}
```

**可用的 Anchor Presets**:
- `topLeft`, `topCenter`, `topRight`
- `middleLeft`, `middleCenter`, `middleRight`
- `bottomLeft`, `bottomCenter`, `bottomRight`
- `stretchTop`, `stretchMiddle`, `stretchBottom`
- `stretchLeft`, `stretchCenter`, `stretchRight`
- `stretchAll`

### 6.4 測試 Layout Component

**工具名稱**: `add_layout_component`

```json
{
  "method": "add_layout_component",
  "params": {
    "gameObjectPath": "TestCanvas/Panel",
    "layoutType": "VerticalLayoutGroup",
    "spacing": 10,
    "padding": { "left": 5, "right": 5, "top": 5, "bottom": 5 }
  }
}
```

### 6.5 測試 EventSystem（Code Review 重點）

根據 Code Review，需要驗證 EventSystem 對 New Input System 的支援：

1. **Legacy Input Manager 環境**:
   - 建立 Canvas 後，檢查 EventSystem 是否包含 `StandaloneInputModule`

2. **New Input System 環境**:
   - 確保已安裝 `com.unity.inputsystem` package
   - 建立 Canvas 後，檢查 EventSystem 是否包含 `InputSystemUIInputModule`

---

## 7. 常見問題排解

### Server 無法啟動

**症狀**: Unity 顯示 "Failed to start server"

**解決方案**:
1. 檢查 Port 8090 是否被佔用：
   ```bash
   lsof -i :8090
   ```
2. 如被佔用，在 Server Window 中更改 Port
3. 確認 Node.js 已正確安裝

### WebSocket 連接失敗

**症狀**: MCP Client 無法連接到 Unity

**解決方案**:
1. 確認 Unity Server 已啟動（查看 Server Window）
2. 確認 Port 配置一致
3. 檢查防火牆設定

### npm install 失敗

**症狀**: Server 安裝時報錯

**解決方案**:
1. 手動執行：
   ```bash
   cd Server~
   npm install --legacy-peer-deps
   ```
2. 清除 npm cache：
   ```bash
   npm cache clean --force
   ```

### 工具執行失敗

**症狀**: 工具返回錯誤

**解決方案**:
1. 查看 Unity Console 的詳細錯誤訊息
2. 確認參數名稱和類型正確
3. 使用 MCP Inspector 測試：
   ```bash
   cd Server~
   npm run inspector
   ```

### Play Mode 時 Server 停止

這是**預期行為**。Server 只在 Edit Mode 運行：
- 進入 Play Mode 時，Server 會自動停止
- 退出 Play Mode 後，Server 會自動重啟（如啟用 Auto Start）

---

## 附錄：專案結構

```
mcp-unity/
├── Editor/                     # Unity C# 程式碼
│   ├── Tools/                  # MCP 工具實作
│   │   ├── UGUITools.cs        # UI 工具（本次 Review 對象）
│   │   └── ...
│   ├── Resources/              # MCP 資源實作
│   ├── Services/               # 服務類別
│   └── UnityBridge/            # WebSocket Server
│       ├── McpUnityServer.cs   # 主要 Server 類別
│       └── McpUnitySettings.cs # 設定管理
│
├── Server~/                    # Node.js MCP Server
│   ├── src/                    # TypeScript 原始碼
│   │   ├── index.ts            # 入口點
│   │   ├── tools/              # 工具定義
│   │   │   ├── uguiTools.ts    # UI 工具（對應 C# 端）
│   │   │   └── ...
│   │   └── unity/              # Unity 通訊
│   ├── build/                  # 編譯輸出
│   └── package.json
│
├── package.json                # Unity Package 定義
└── ProjectSettings/
    └── McpUnitySettings.json   # Server 設定
```

---

## 附錄：環境變數

| 變數 | 說明 | 預設值 |
|------|------|--------|
| `UNITY_HOST` | Unity Server 主機 | `localhost` |
| `UNITY_PORT` | Unity Server 端口 | `8090` |
| `LOGGING` | 啟用 Console 日誌 | `false` |
| `LOGGING_FILE` | 寫入日誌到 log.txt | `false` |

---

## 下一步

完成基本測試後，你可以：

1. **執行 Code Review 建議的修改** - 參考 `doc/codeReview/Response_20260203_UGUITools.md` 中的 Refactor Prompt
2. **新增自定義工具** - 參考 `CLAUDE.md` 中的 "Adding a New Tool" 章節
3. **使用 MCP Inspector** 進行互動式除錯

如有問題，請在 [GitHub Issues](https://github.com/anthropics/claude-code/issues) 提出。
