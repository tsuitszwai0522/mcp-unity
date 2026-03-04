# Feature Design: 競品功能移植

> 來源分析：[IvanMurzak/Unity-MCP](https://github.com/IvanMurzak/Unity-MCP) 競品比較
> 狀態：待批准
> 建立日期：2026-03-05

## 1. 需求概述

從 IvanMurzak/Unity-MCP 識別出 5 個我們缺少但對 AI agent workflow 有高價值嘅功能，移植到 mcp-unity。

### 功能清單

| # | 功能 | 優先級 | 新增 Tool 數量 |
|---|------|--------|---------------|
| 1 | Screenshot Tools | 高 | 3 |
| 2 | Script Execute (Roslyn) | 高 | 3 |
| 3 | Play Mode Control | 中 | 2 |
| 4 | Asset Move/Copy/Rename | 中 | 1 |
| 5 | Editor Selection Get | 低 | 1 |
| 6 | Play Mode Server 持續連線 | 高 | 0（基礎設施改動） |
| | **合計** | | **10 個新 tool + 1 個架構改進** |

---

## 2. 功能一：Screenshot Tools

### 2.1 目的

令 AI 可以「睇到」Unity Editor 嘅畫面，大幅提升 AI 理解場景、UI 佈局、視覺效果嘅能力。呢個係目前最大嘅功能缺口 —— AI 而家只能透過文字描述去理解場景，無法視覺化驗證。

### 2.2 Tool 定義

#### `screenshot_game_view`
從 Game View 截圖，反映玩家會睇到嘅畫面。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `width` | int | 否 | 960 | 截圖寬度（px） |
| `height` | int | 否 | 540 | 截圖高度（px） |

#### `screenshot_scene_view`
從 Scene View 截圖，反映編輯器視角。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `width` | int | 否 | 960 | 截圖寬度（px） |
| `height` | int | 否 | 540 | 截圖高度（px） |

#### `screenshot_camera`
從指定 Camera 截圖。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `cameraPath` | string | 否 | null | Camera 所在 GameObject 路徑，null 則用 Main Camera |
| `cameraInstanceId` | int | 否 | null | Camera 所在 GameObject 的 instance ID |
| `width` | int | 否 | 960 | 截圖寬度（px） |
| `height` | int | 否 | 540 | 截圖高度（px） |

### 2.3 回傳格式

所有 screenshot tool 以 **base64 嵌入** MCP response：

```jsonc
// Unity C# → Node.js 回傳
{
  "success": true,
  "type": "image",
  "mimeType": "image/png",
  "data": "<base64-encoded-png>"
}
```

```jsonc
// Node.js → MCP Client 回傳
{
  "content": [{
    "type": "image",
    "mimeType": "image/png",
    "data": "<base64-encoded-png>"
  }]
}
```

### 2.4 C# 實作方案

#### Game View 截圖
```csharp
// 方案：使用 ScreenCapture.CaptureScreenshotAsTexture()
// 需要 Game View 可見；若 Game View 未開啟則先開啟
var texture = ScreenCapture.CaptureScreenshotAsTexture();
// 縮放到目標解析度
var resized = ResizeTexture(texture, width, height);
byte[] pngBytes = resized.EncodeToPNG();
string base64 = Convert.ToBase64String(pngBytes);
```

#### Scene View 截圖
```csharp
// 方案：從 SceneView.lastActiveSceneView 取得 camera 再 render
SceneView sceneView = SceneView.lastActiveSceneView;
Camera sceneCamera = sceneView.camera;
RenderTexture rt = new RenderTexture(width, height, 24);
sceneCamera.targetTexture = rt;
sceneCamera.Render();
// 讀取 RenderTexture 到 Texture2D
RenderTexture.active = rt;
Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
tex.Apply();
byte[] pngBytes = tex.EncodeToPNG();
// 清理
sceneCamera.targetTexture = null;
RenderTexture.active = null;
```

#### Camera 截圖
```csharp
// 方案：找到指定 Camera，建立 RenderTexture 渲染
Camera cam = /* 透過 path 或 instanceId 找到 */;
RenderTexture rt = new RenderTexture(width, height, 24);
cam.targetTexture = rt;
cam.Render();
// 同 Scene View 方案讀取
```

### 2.5 注意事項

- **Game View 截圖唔需要 Play Mode**。只需要 Game View **window tab 存在**（Edit Mode 亦可截圖）
- 截圖前自動確保 Game View 已開啟（唔搶 focus）：
  ```csharp
  var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
  EditorWindow.GetWindow(gameViewType, false, null, false); // false = 唔搶 focus
  ```
- Scene View camera 係 Editor 內部 camera，render 結果可能包含 gizmos
- 大圖片 base64 會增加 token 用量，預設 960x540 係合理平衡
- 所有 tool 應該係 **synchronous**（IsAsync = false），因為截圖操作本身好快
- **Play Mode 期間**（搭配功能六 server 持續連線）：Game View 截圖會反映實際遊戲畫面，呢個係最有價值嘅使用場景

---

## 3. 功能二：Script Execute (Roslyn)

### 3.1 目的

令 AI 可以直接編譯同執行 C# code snippet，唔使建立 .cs 檔案再 recompile。呢個功能可以大幅加速迭代：

- 快速原型驗證
- 執行一次性 editor 腳本（例如批量重命名、資料遷移）
- 查詢 runtime 資料（例如 `FindObjectsOfType` 結果）
- 動態生成/修改 assets

### 3.2 Tool 定義

#### `execute_code`
編譯同執行 C# code snippet。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `code` | string | 是 | - | C# code snippet |
| `timeoutMs` | int | 否 | 10000 | 執行超時（毫秒） |

#### `read_script`
讀取一個 .cs 檔案嘅內容。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `assetPath` | string | 是 | - | 腳本路徑（例如 `Assets/Scripts/Player.cs`） |

#### `write_script`
建立或覆寫一個 .cs 檔案，然後觸發 recompile。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `assetPath` | string | 是 | - | 腳本路徑（例如 `Assets/Scripts/Player.cs`） |
| `content` | string | 是 | - | 完整嘅 C# 檔案內容 |
| `recompile` | bool | 否 | true | 寫入後是否自動 recompile |

### 3.3 execute_code 方案比較

#### 方案 A：Roslyn 動態編譯（推薦）

**做法**：使用 `Microsoft.CodeAnalysis.CSharp` (Roslyn) 在 Editor 內動態編譯 C# code 到 in-memory assembly，然後反射執行。

```csharp
// 概念代碼
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

string wrappedCode = $@"
using UnityEngine;
using UnityEditor;
using System.Linq;

public static class DynamicScript {{
    public static object Execute() {{
        {userCode}
    }}
}}";

var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);
var compilation = CSharpCompilation.Create("DynamicAssembly")
    .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
    .AddReferences(/* Unity assemblies + System assemblies */)
    .AddSyntaxTrees(syntaxTree);

using var ms = new MemoryStream();
var result = compilation.Emit(ms);
if (result.Success) {
    var assembly = Assembly.Load(ms.ToArray());
    var type = assembly.GetType("DynamicScript");
    var method = type.GetMethod("Execute");
    var output = method.Invoke(null, null);
    return output?.ToString();
}
```

**優點**：
- 即時執行，唔使 domain reload
- 可以 access 所有 Unity API 同專案程式碼
- 支援完整 C# 語法

**缺點**：
- 需要引入 Roslyn DLLs（~15-20MB，已確認可接受）
- 需要正確配置 assembly references
- 有安全風險（可以執行任意代碼，以 Level 1 warning log + 設定開關緩解）

#### 方案 B：EditorApplication.ExecuteMenuItem + 臨時腳本

**做法**：將 code snippet 寫入臨時 .cs 檔案（帶 `[InitializeOnLoad]` 或 `[MenuItem]`），觸發 recompile 後執行。

**優點**：
- 唔使額外依賴
- 100% Unity 原生

**缺點**：
- 每次執行都需要 domain reload（3-10 秒）
- 需要管理臨時檔案清理
- 用戶體驗差

#### 決策：選擇方案 A（Roslyn）

### 3.4 Roslyn 依賴管理

> **兼容性驗證結果**（Unity 6.3 / 6000.3）：
> - Roslyn DLLs 係 **managed .NET assemblies**（純 IL code），Mac/Win/Linux 跨平台完全兼容
> - 必須從 NuGet `.nupkg` 入面嘅 **`lib/netstandard2.0/`** 資料夾取得 DLL（唔好用 `net8.0` 版本）
> - 使用 **Compilation API**（`CSharpCompilation`），唔用 Scripting API（`CSharpScript.EvaluateAsync`），因為後者喺 Unity 有[已知兼容問題](https://discussions.unity.com/t/csharp-scripting-dynamic-compilation-notimplementedexception-on-csharpscript-evaluateasync/301550)
> - 參考：[Roslyn in Unity 6 Working Setup](https://itch.io/blog/937280/code-generation-with-roslyn-in-unity-6-my-working-setup)

**安裝方式**：從 NuGet 手動提取 DLL，直接 commit 入 git repo。**唔使 NuGetForUnity**，避免平台差異同 package restore 問題。

需要嘅 DLLs（建議版本 4.13.0）：

| DLL | NuGet Package | 說明 |
|-----|---------------|------|
| `Microsoft.CodeAnalysis.dll` | `Microsoft.CodeAnalysis.Common` 4.13.0 | Roslyn 核心 |
| `Microsoft.CodeAnalysis.CSharp.dll` | `Microsoft.CodeAnalysis.CSharp` 4.13.0 | C# 編譯器 |
| `System.Collections.Immutable.dll` | `System.Collections.Immutable` | Roslyn 依賴 |
| `System.Reflection.Metadata.dll` | `System.Reflection.Metadata` 9.0.x | Roslyn 依賴 |

放置位置：`Editor/Plugins/Roslyn/`
- 僅 Editor 使用，唔會打包入 build
- 純 managed DLL，Mac/Win 共用同一份，唔使分平台
- 若出現 "Unable to resolve reference" 錯誤，喺 Inspector 關閉 **"Validate References"**

### 3.5 安全考量

> **決策**：採用 **Level 1（Warning Log + 設定開關）**，唔做 sandbox。

**理由**：
- MCP Unity 本身已經係 editor tool，用家已經信任 AI agent 操作佢嘅 Unity project
- AI agent 已經可以透過 `write_script` 寫任意 .cs 檔案 + recompile，sandbox 咗 `execute_code` 但冇 sandbox `write_script` 冇意義
- IvanMurzak 嘅 `script-execute` 同 `reflection-method-call` 都冇做 sandbox
- Unity 唔支持 AppDomain isolation（Mono/IL2CPP 限制），真正嘅 sandbox 做唔到
- 過度限制會令功能變得雞肋

**實際安全措施**：

```csharp
// 1. McpUnitySettings.json 加新設定
"EnableDynamicCodeExecution": true   // 用家可以關閉

// 2. 每次執行前 warning log（令用家可以喺 Console 追蹤 AI 執行咗咩）
Debug.LogWarning($"[MCP Unity] Dynamic code execution requested. Code:\n{code}");

// 3. 執行超時（預設 10 秒）
var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

// 4. 捕捉所有 exception 並回傳錯誤訊息（唔會 crash editor）
```

### 3.6 execute_code 回傳格式

```jsonc
{
  "success": true,
  "type": "text",
  "message": "Script executed successfully",
  "output": "/* Execute() 返回值的 ToString() */",
  "compilationErrors": [],  // 編譯錯誤（如有）
  "logs": []                // 執行期間產生的 Debug.Log 訊息
}
```

### 3.7 注意事項

- `execute_code` 應該係 **async**（IsAsync = true），因為 Roslyn 編譯可能需要時間
- `read_script` 同 `write_script` 可以係 **sync**
- Code snippet 會被包裝喺 `Execute()` method 入面，用戶唔使寫 class 結構
- 自動 `using` 常用 namespace：UnityEngine, UnityEditor, System, System.Linq, System.Collections.Generic

---

## 4. 功能三：Play Mode Control

### 4.1 目的

令 AI 可以控制 Unity Editor 嘅播放狀態，用於：
- 啟動 Play Mode 測試遊戲行為
- 暫停 Play Mode 檢查狀態
- 停止 Play Mode 返回編輯

### 4.2 Tool 定義

#### `get_editor_state`
查詢 Unity Editor 當前狀態。

| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| （無參數） | | | |

回傳：

```jsonc
{
  "success": true,
  "type": "text",
  "message": "Editor state retrieved",
  "state": {
    "isPlaying": false,
    "isPaused": false,
    "isCompiling": false,
    "currentScene": "Assets/Scenes/SampleScene.unity",
    "platform": "StandaloneWindows64"
  }
}
```

#### `set_editor_state`
設定 Unity Editor 播放狀態。

| 參數 | 類型 | 必填 | 預設值 | 說明 |
|------|------|------|--------|------|
| `action` | string | 是 | - | `"play"`, `"pause"`, `"unpause"`, `"stop"` |

### 4.3 實作方案

```csharp
// get_editor_state
return new JObject
{
    ["success"] = true,
    ["type"] = "text",
    ["message"] = "Editor state retrieved",
    ["state"] = new JObject
    {
        ["isPlaying"] = EditorApplication.isPlaying,
        ["isPaused"] = EditorApplication.isPaused,
        ["isCompiling"] = EditorApplication.isCompiling,
        ["currentScene"] = SceneManager.GetActiveScene().path,
        ["platform"] = EditorUserBuildSettings.activeBuildTarget.ToString()
    }
};

// set_editor_state
switch (action)
{
    case "play":
        EditorApplication.isPlaying = true;
        break;
    case "pause":
        EditorApplication.isPaused = true;
        break;
    case "unpause":
        EditorApplication.isPaused = false;
        break;
    case "stop":
        EditorApplication.isPlaying = false;
        break;
}
```

### 4.4 注意事項

- `get_editor_state` 係 sync，`set_editor_state` 都係 sync（狀態變更本身係即時嘅）
- `set_editor_state("play")` 嘅行為取決於功能六（Play Mode Server 持續連線）：
  - **Domain Reload 關閉**：server 唔會停，連線零中斷
  - **Domain Reload 開啟**：server 短暫停止（2-5 秒），`EnteredPlayMode` 後自動重啟，Node.js fast polling 自動重連
- Node.js 側 response 應包含 warning 提示 client 可能有短暫斷線

---

## 5. 功能四：Asset Move/Copy/Rename

### 5.1 目的

補齊基本 asset 檔案操作。現有工具可以建立同刪除場景/prefab，但無法搬移、複製、重命名任意 asset。

### 5.2 Tool 定義

#### `manage_asset`
統一嘅 asset 管理工具，支持 move/copy/rename/delete 操作。

| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `action` | string | 是 | `"move"`, `"copy"`, `"rename"`, `"delete"`, `"create_folder"` |
| `assetPath` | string | 是 | 來源 asset 路徑（例如 `Assets/Textures/old.png`） |
| `destinationPath` | string | 條件 | 目標路徑（move/copy 必填） |
| `newName` | string | 條件 | 新名稱（rename 必填，不含路徑） |

### 5.3 實作方案

```csharp
switch (action)
{
    case "move":
        string moveError = AssetDatabase.MoveAsset(assetPath, destinationPath);
        if (!string.IsNullOrEmpty(moveError))
            return CreateErrorResponse(moveError, "tool_execution_error");
        break;

    case "copy":
        bool copied = AssetDatabase.CopyAsset(assetPath, destinationPath);
        if (!copied)
            return CreateErrorResponse($"Failed to copy {assetPath}", "tool_execution_error");
        break;

    case "rename":
        string renameError = AssetDatabase.RenameAsset(assetPath, newName);
        if (!string.IsNullOrEmpty(renameError))
            return CreateErrorResponse(renameError, "tool_execution_error");
        break;

    case "delete":
        bool deleted = AssetDatabase.DeleteAsset(assetPath);
        if (!deleted)
            return CreateErrorResponse($"Failed to delete {assetPath}", "tool_execution_error");
        break;

    case "create_folder":
        string parent = Path.GetDirectoryName(assetPath).Replace("\\", "/");
        string folder = Path.GetFileName(assetPath);
        string guid = AssetDatabase.CreateFolder(parent, folder);
        if (string.IsNullOrEmpty(guid))
            return CreateErrorResponse($"Failed to create folder {assetPath}", "tool_execution_error");
        break;
}
```

### 5.4 設計決策：單一 Tool vs 多個 Tool

| 方案 | 優點 | 缺點 |
|------|------|------|
| **單一 `manage_asset`**（推薦） | 減少 tool 數量，batch 友好 | 參數組合較複雜 |
| 多個 tool（move_asset, copy_asset...） | 每個 tool 參數簡單 | 增加 5 個 tool，膨脹 |

選擇單一 tool，因為已經有 `batch_execute` 可以組合操作，且減少 tool 總數對 LLM context 更友好。

---

## 6. 功能五：Editor Selection Get

### 6.1 目的

現有 `select_gameobject` 只能**設定**選擇，無法**讀取**當前選擇。AI 需要知道用戶當前選中咗咩嘢嚟做 context-aware 操作。

### 6.2 Tool 定義

#### `get_selection`
取得 Unity Editor 當前選中嘅物件。

| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| （無參數） | | | |

回傳：

```jsonc
{
  "success": true,
  "type": "text",
  "message": "Current selection retrieved",
  "selection": {
    "activeGameObject": {
      "name": "Player",
      "instanceId": 12345,
      "path": "Environment/Player"
    },
    "gameObjects": [
      { "name": "Player", "instanceId": 12345, "path": "Environment/Player" },
      { "name": "Enemy", "instanceId": 12346, "path": "Environment/Enemy" }
    ],
    "activeObject": {
      "name": "PlayerMaterial",
      "instanceId": 67890,
      "type": "Material",
      "assetPath": "Assets/Materials/PlayerMaterial.mat"
    },
    "count": 2
  }
}
```

### 6.3 實作方案

```csharp
var selection = new JObject();

// Active GameObject
if (Selection.activeGameObject != null)
{
    var go = Selection.activeGameObject;
    selection["activeGameObject"] = new JObject
    {
        ["name"] = go.name,
        ["instanceId"] = go.GetInstanceID(),
        ["path"] = GetGameObjectPath(go)
    };
}

// All selected GameObjects
var gameObjects = new JArray();
foreach (var go in Selection.gameObjects)
{
    gameObjects.Add(new JObject
    {
        ["name"] = go.name,
        ["instanceId"] = go.GetInstanceID(),
        ["path"] = GetGameObjectPath(go)
    });
}
selection["gameObjects"] = gameObjects;

// Active Object (could be Asset in Project window)
if (Selection.activeObject != null && !(Selection.activeObject is GameObject))
{
    var obj = Selection.activeObject;
    selection["activeObject"] = new JObject
    {
        ["name"] = obj.name,
        ["instanceId"] = obj.GetInstanceID(),
        ["type"] = obj.GetType().Name,
        ["assetPath"] = AssetDatabase.GetAssetPath(obj)
    };
}

selection["count"] = Selection.objects.Length;
```

---

## 7. 功能六：Play Mode Server 持續連線

### 7.1 目的

現有架構下，Unity 進入 Play Mode 時 MCP server 會**完全停止**，所有 tool（包括新增嘅 screenshot）都無法使用。呢個係一個根本性限制 —— AI 無法喺 Play Mode 期間截圖、查詢狀態、甚至讀取 console log。

呢個改動係所有新功能嘅**基礎設施前提**，特別係 screenshot 同 play mode control 嘅價值會因此大幅提升。

### 7.2 現狀分析

**現有 `McpUnityServer.cs` 嘅 Play Mode 處理（`OnPlayModeStateChanged`）：**

```
ExitingEditMode  → StopServer(4001)     ← server 完全停止
EnteredPlayMode  → （無操作）            ← server 仍然停止
ExitingPlayMode  → （無操作）            ← server 仍然停止
EnteredEditMode  → StartServer()        ← server 重啟
```

**問題**：Play Mode 期間有一個完整嘅 domain reload cycle，server 被銷毀後冇人重啟佢。

### 7.3 方案設計：智能持續連線（方案 C）

結合兩個互補策略，根據項目設定自動選擇最佳路徑：

#### 路徑 1：Domain Reload 被關閉 → 零斷線

如果項目啟用咗 **Enter Play Mode Settings** 並關閉 Domain Reload（Unity 2022+ 支持），server 可以完全唔停。

```csharp
case PlayModeStateChange.ExitingEditMode:
    if (IsDomainReloadDisabled())
    {
        // Domain reload 唔會發生，server 可以存活，唔使停
        McpLogger.LogInfo("Play Mode: Domain Reload disabled, keeping MCP server alive");
    }
    else
    {
        // Domain reload 會銷毀 server，必須先優雅關閉
        _instance.StopServer(UnityCloseCode.PlayMode, "Unity entering Play mode");
    }
    break;
```

**偵測方法：**

```csharp
private static bool IsDomainReloadDisabled()
{
    return EditorSettings.enterPlayModeOptionsEnabled &&
           (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
}
```

#### 路徑 2：Domain Reload 開啟 → 自動重啟

如果 domain reload 開啟（大多數項目嘅預設值），server 仍然會喺 `ExitingEditMode` 被銷毀。但喺 `EnteredPlayMode` 階段（domain reload 完成後）立即重啟。

```csharp
case PlayModeStateChange.EnteredPlayMode:
    // Domain reload 完成，重啟 server 令 Play Mode 期間都可以用 MCP tools
    if (!_instance.IsListening && McpUnitySettings.Instance.AutoStartServer)
    {
        McpLogger.LogInfo("Play Mode: Restarting MCP server after domain reload");
        _instance.StartServer();
    }
    break;
```

#### 完整修改後嘅 `OnPlayModeStateChanged`：

```csharp
private static void OnPlayModeStateChanged(PlayModeStateChange state)
{
    switch (state)
    {
        case PlayModeStateChange.ExitingEditMode:
            if (IsDomainReloadDisabled())
            {
                // Domain reload 關閉 → server 繼續跑，零斷線
            }
            else if (_instance.IsListening)
            {
                // Domain reload 開啟 → 必須先停 server
                _instance.StopServer(UnityCloseCode.PlayMode, "Unity entering Play mode");
            }
            break;

        case PlayModeStateChange.EnteredPlayMode:
            // Domain reload 完成後重啟（路徑 2）
            // 若 domain reload 關閉，server 本身仍在跑，IsListening 為 true，唔會重複啟動
            if (!_instance.IsListening && McpUnitySettings.Instance.AutoStartServer)
            {
                _instance.StartServer();
            }
            break;

        case PlayModeStateChange.ExitingPlayMode:
            // 即將離開 Play Mode，若 domain reload 開啟會再次觸發 reload
            // 唔使特別處理 — OnBeforeAssemblyReload 會處理
            break;

        case PlayModeStateChange.EnteredEditMode:
            // 返回 Edit Mode，確保 server 運行
            if (!_instance.IsListening && McpUnitySettings.Instance.AutoStartServer)
            {
                _instance.StartServer();
            }
            break;
    }
}
```

### 7.4 Node.js 側影響

Node.js 側**唔使改任何代碼**。現有嘅 play mode reconnection 機制已經可以處理：

- **路徑 1（零斷線）**：連線從未中斷，Node.js 完全無感
- **路徑 2（短暫斷線）**：收到 close code 4001 → 進入 fast polling（3 秒） → server 喺 `EnteredPlayMode` 重啟 → 自動重連

**斷線窗口估算（路徑 2）：**

| 階段 | 耗時 |
|------|------|
| ExitingEditMode → Domain Reload 開始 | ~0.5s |
| Domain Reload 進行中 | ~1-5s（取決於項目大小） |
| EnteredPlayMode → StartServer() | ~0.5s |
| Node.js fast polling 偵測到 server | 0-3s（最差情況） |
| **總斷線時間** | **~2-9s** |

### 7.5 風險評估

| 風險 | 嚴重性 | 緩解措施 |
|------|--------|----------|
| Play Mode 期間 editor tool 行為異常 | 低 | Unity Editor API 喺 Play Mode 仍然可用，只有少數 API 有限制 |
| ExitingPlayMode 再次 domain reload 導致二次斷線 | 低 | 同樣由 `OnAfterAssemblyReload` 處理，已有機制 |
| 用戶預期 Play Mode 唔會有 MCP 操作 | 無 | 呢個係新功能，唔會破壞現有行為 |
| `IsDomainReloadDisabled()` 檢測失敗 | 極低 | Fallback 到路徑 2（有 domain reload），最差情況同現有行為一樣 |

### 7.6 測試計劃

1. **Domain Reload 開啟（預設）**：
   - 進入 Play Mode → 確認 server 喺 `EnteredPlayMode` 後重啟
   - Play Mode 期間執行 `get_editor_state` → 確認回傳 `isPlaying: true`
   - Play Mode 期間執行 `screenshot_game_view` → 確認截圖正常
   - 離開 Play Mode → 確認 server 正常恢復

2. **Domain Reload 關閉**：
   - 開啟 Enter Play Mode Settings，關閉 Reload Domain
   - 進入 Play Mode → 確認 server 從未停止（無 close code 4001）
   - Play Mode 期間執行所有 tool → 確認正常

3. **Edge Cases**：
   - 快速 Play/Stop 切換 → 確認 server 唔會 crash
   - Play Mode 期間 recompile scripts → 確認 server 重啟正常

---

## 8. 實作依賴關係

```
功能六 (Play Mode Server 持續連線)
  ↓ 解鎖 Play Mode 期間所有 tool
  ├── 功能一 (Screenshot) ← Play Mode 截圖需要 server 存活
  ├── 功能三 (Play Mode Control) ← set_editor_state("play") 後仍需保持連線
  └── 所有現有 tool ← Play Mode 期間亦可使用

功能二 (Script Execute) ← 獨立，唔依賴其他功能
功能四 (Asset Manage) ← 獨立
功能五 (Get Selection) ← 獨立
```

**建議實作順序**：功能六 → 功能一 → 功能三 → 功能五 → 功能四 → 功能二

---

## 9. 檔案結構

新增檔案清單：

### Unity C# 側 (`Editor/Tools/`)

| 檔案 | Tool Name | 說明 |
|------|-----------|------|
| `ScreenshotTools.cs` | `screenshot_game_view`, `screenshot_scene_view`, `screenshot_camera` | 3 個截圖 tool 合併一個檔案（類似現有 TransformTools.cs 模式） |
| `ScriptExecuteTool.cs` | `execute_code` | Roslyn 動態執行 |
| `ScriptFileTool.cs` | `read_script`, `write_script` | 腳本檔案讀寫 |
| `EditorStateTool.cs` | `get_editor_state`, `set_editor_state` | 編輯器狀態控制 |
| `ManageAssetTool.cs` | `manage_asset` | Asset 管理操作 |
| `GetSelectionTool.cs` | `get_selection` | 讀取編輯器選擇 |

### Unity C# 側 (`Editor/Plugins/Roslyn/`)

> 從 NuGet `.nupkg` 手動提取 `lib/netstandard2.0/` 版本，commit 入 git。Mac/Win 共用同一份。

| 檔案 | NuGet 來源 | 版本 |
|------|-----------|------|
| `Microsoft.CodeAnalysis.dll` | `Microsoft.CodeAnalysis.Common` | 4.13.0 |
| `Microsoft.CodeAnalysis.CSharp.dll` | `Microsoft.CodeAnalysis.CSharp` | 4.13.0 |
| `System.Collections.Immutable.dll` | `System.Collections.Immutable` | latest |
| `System.Reflection.Metadata.dll` | `System.Reflection.Metadata` | 9.0.x |

### Node.js TypeScript 側 (`Server~/src/tools/`)

| 檔案 | Tool Name |
|------|-----------|
| `screenshotTools.ts` | `screenshot_game_view`, `screenshot_scene_view`, `screenshot_camera` |
| `executeCodeTool.ts` | `execute_code` |
| `readScriptTool.ts` | `read_script` |
| `writeScriptTool.ts` | `write_script` |
| `editorStateTool.ts` | `get_editor_state`, `set_editor_state` |
| `manageAssetTool.ts` | `manage_asset` |
| `getSelectionTool.ts` | `get_selection` |

---

## 10. 註冊點修改

### McpUnityServer.cs `RegisterTools()`

在 `BatchExecuteTool` 之前加入（BatchExecuteTool 必須最後註冊）：

```csharp
// Register Screenshot Tools
ScreenshotGameViewTool screenshotGameViewTool = new ScreenshotGameViewTool();
_tools.Add(screenshotGameViewTool.Name, screenshotGameViewTool);
ScreenshotSceneViewTool screenshotSceneViewTool = new ScreenshotSceneViewTool();
_tools.Add(screenshotSceneViewTool.Name, screenshotSceneViewTool);
ScreenshotCameraTool screenshotCameraTool = new ScreenshotCameraTool();
_tools.Add(screenshotCameraTool.Name, screenshotCameraTool);

// Register Script Tools
ExecuteCodeTool executeCodeTool = new ExecuteCodeTool();
_tools.Add(executeCodeTool.Name, executeCodeTool);
ReadScriptTool readScriptTool = new ReadScriptTool();
_tools.Add(readScriptTool.Name, readScriptTool);
WriteScriptTool writeScriptTool = new WriteScriptTool();
_tools.Add(writeScriptTool.Name, writeScriptTool);

// Register Editor State Tools
GetEditorStateTool getEditorStateTool = new GetEditorStateTool();
_tools.Add(getEditorStateTool.Name, getEditorStateTool);
SetEditorStateTool setEditorStateTool = new SetEditorStateTool();
_tools.Add(setEditorStateTool.Name, setEditorStateTool);

// Register Asset Management Tool
ManageAssetTool manageAssetTool = new ManageAssetTool();
_tools.Add(manageAssetTool.Name, manageAssetTool);

// Register Get Selection Tool
GetSelectionTool getSelectionTool = new GetSelectionTool();
_tools.Add(getSelectionTool.Name, getSelectionTool);
```

### Server~/src/index.ts

```typescript
// Register Screenshot Tools
registerScreenshotTools(server, mcpUnity, toolLogger);

// Register Script Tools
registerExecuteCodeTool(server, mcpUnity, toolLogger);
registerReadScriptTool(server, mcpUnity, toolLogger);
registerWriteScriptTool(server, mcpUnity, toolLogger);

// Register Editor State Tools
registerEditorStateTools(server, mcpUnity, toolLogger);

// Register Asset Management Tool
registerManageAssetTool(server, mcpUnity, toolLogger);

// Register Get Selection Tool
registerGetSelectionTool(server, mcpUnity, toolLogger);
```

---

## 11. TypeScript 側特殊處理

### Screenshot Tools — Image Content Type

MCP SDK 支持 `image` content type，screenshot tool 嘅 TypeScript handler 需要特別處理：

```typescript
// screenshotTools.ts
async function toolHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(ErrorType.TOOL_EXECUTION, response.message);
  }

  return {
    content: [{
      type: 'image',
      mimeType: response.mimeType || 'image/png',
      data: response.data  // base64 encoded PNG
    }]
  };
}
```

### execute_code — 較長 Timeout

Roslyn 編譯可能需要較長時間，Node.js 側需要增加 timeout：

```typescript
// executeCodeTool.ts
const response = await mcpUnity.sendRequest({
  method: toolName,
  params
}, 30000);  // 30 秒 timeout（預設 10 秒唔夠）
```

---

## 12. 實作階段建議

### Phase 1：基礎設施 + 核心工具（最高價值，最低風險）
- [ ] **Play Mode Server 持續連線**（功能六） ← 所有後續功能嘅前提
  - [ ] 修改 `McpUnityServer.cs` 嘅 `OnPlayModeStateChanged`
  - [ ] 新增 `IsDomainReloadDisabled()` helper
  - [ ] 測試 domain reload 開啟/關閉兩個路徑
- [ ] `screenshot_game_view`
- [ ] `screenshot_scene_view`
- [ ] `screenshot_camera`
- [ ] `get_editor_state`
- [ ] `set_editor_state`
- [ ] `get_selection`

### Phase 2：資產與腳本管理（中等價值，低風險）
- [ ] `manage_asset`
- [ ] `read_script`
- [ ] `write_script`

### Phase 3：動態代碼執行（高價值，高風險/複雜度）
- [ ] `execute_code`（Roslyn 依賴管理需要額外測試）

---

## 13. 待解問題

1. ~~**Roslyn DLL 大小**~~ → ✅ 已確認 ~15-20MB 可接受
2. ~~**Roslyn Unity 兼容性**~~ → ✅ 已驗證：使用 `netstandard2.0` 版本 DLL，Compilation API（唔用 Scripting API），版本 4.13.0，Mac/Win 跨平台兼容（managed DLL）
3. ~~**Screenshot Game View**~~ → ✅ 已澄清：唔需要 Play Mode，只需 Game View window tab 存在，截圖前自動開啟
4. ~~**Play Mode 同 MCP 連線**~~ → ✅ 已由功能六（Play Mode Server 持續連線，方案 C）解決
5. ~~**execute_code 安全性**~~ → ✅ 決策：Level 1（Warning Log + `EnableDynamicCodeExecution` 設定開關），唔做 sandbox
6. ~~**Play Mode 期間 Editor API 限制**~~ → ✅ 已確認：Play Mode 主要用途係 screenshot、console log、狀態查詢、runtime 資料檢查，全部唔涉及 `AssetDatabase`。Asset 相關操作（manage_asset、write_script、create_prefab 等）本身就係 Edit Mode 工作，唔會喺 Play Mode 使用

---

## 14. 變更摘要

| 項目 | 數量 |
|------|------|
| 新增 C# 工具檔案 | 6 |
| 新增 TypeScript 工具檔案 | 7 |
| 新增 MCP Tools | 10 |
| 修改現有檔案 | 3（McpUnityServer.cs, index.ts, McpUnityServer.cs play mode 邏輯） |
| 架構改進 | Play Mode Server 持續連線 |
| 新增依賴 | Roslyn DLLs（僅 execute_code） |
| 總 Tool 數量 | 42 → 52 |
