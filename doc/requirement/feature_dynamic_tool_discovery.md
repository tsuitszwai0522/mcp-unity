# Feature Design: Dynamic External Tool Discovery

> 狀態：**已實作、已測試**
> 建立日期：2026-03-23
> 實作日期：2026-03-23
> 測試日期：2026-03-23
> 目標版本：v1.7.0

## 1. 需求概述

### 背景

目前 MCP Unity 的 tool 註冊流程是**雙邊硬編碼**：
- **C# 側**：`McpUnityServer.RegisterTools()` 手動 `new` 每個 tool class 並加到 `_tools` dictionary
- **Node.js 側**：每個 tool 有獨立的 `.ts` 檔案，用 Zod 定義參數 schema，在 `index.ts` 中逐一呼叫 `register*Tool()`

外部專案（如 ProjectT）若想新增 MCP tool，必須 fork mcp-unity 或修改 package 原始碼，**無法以 plugin 形式擴展**。

### 目標

讓外部專案能在**不修改 mcp-unity package** 的前提下，透過繼承 `McpToolBase` 自動註冊 MCP tool：

1. C# 側：assembly 掃描自動發現所有 `McpToolBase` 子類
2. C# 側：tool 自描述參數 schema（JSON Schema 格式）
3. Node.js 側：啟動時從 Unity 動態拉取外部 tool 清單並註冊

### 設計原則

- **零耦合**：外部專案只需引用 mcp-unity package，寫一個繼承 `McpToolBase` 的 class 即可
- **內建優先**：內建 tools 保持現有 hardcode（精確 Zod 驗證 + 獨立 handler），外部 tools 用 passthrough 模式
- **向後相容**：現有 tools 完全不受影響

---

## 2. 現有架構

### Tool 生命週期

```
C# 側                                    Node.js 側
─────────────────────                    ─────────────────────
RegisterTools()                          index.ts
  new XxxTool()                            registerXxxTool(server, mcpUnity)
  _tools[name] = tool                        server.tool(name, desc, zodSchema.shape, handler)

OnMessage(method, params)                sendRequest({ method, params })
  _tools[method].Execute(params)           → WebSocket → Unity
  → response                               ← response
```

### McpToolBase 現有介面

```csharp
public abstract class McpToolBase
{
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    public bool IsAsync { get; protected set; } = false;

    public virtual JObject Execute(JObject parameters) { ... }
    public virtual void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs) { ... }
}
```

### McpUnitySocketHandler 訊息路由

```csharp
// McpUnitySocketHandler.cs:84-95
if (_server.TryGetTool(method, out var tool))
    EditorCoroutineUtility.StartCoroutineOwnerless(ExecuteTool(tool, parameters, tcs));
else if (_server.TryGetResource(method, out var resource))
    EditorCoroutineUtility.StartCoroutineOwnerless(FetchResourceCoroutine(resource, parameters, tcs));
else
    tcs.SetResult(CreateErrorResponse($"Unknown method: {method}", "unknown_method"));
```

### MCP SDK `server.tool()` 簽名

```typescript
server.tool(
  name: string,
  description: string,
  paramsSchema: ZodRawShape,  // ← Zod 物件，不是 JSON Schema
  handler: (params) => Promise<CallToolResult>
): void;
```

**關鍵限制**：MCP SDK 的第三個參數是 `ZodRawShape`，不接受原始 JSON Schema。動態註冊需要 JSON Schema → Zod 轉換。

---

## 3. 設計方案

### 3.1 C# 側改動

#### 改動 1：`McpToolBase` 新增 `ParameterSchema` 虛擬屬性

```csharp
// McpToolBase.cs
public abstract class McpToolBase
{
    public string Name { get; protected set; }
    public string Description { get; protected set; }
    public bool IsAsync { get; protected set; } = false;

    /// <summary>
    /// JSON Schema describing the tool's parameters.
    /// Override this to provide parameter definitions for dynamic tool discovery.
    /// Built-in tools can ignore this (Node.js side uses Zod schemas).
    /// </summary>
    public virtual JObject ParameterSchema => new JObject
    {
        ["type"] = "object",
        ["properties"] = new JObject()
    };

    public virtual JObject Execute(JObject parameters) { ... }
    public virtual void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs) { ... }
}
```

#### 改動 2：`McpUnityServer.RegisterTools()` 加入 Assembly 掃描

在所有內建 tools 註冊後、`BatchExecuteTool` 註冊前，掃描所有 assembly：

```csharp
private void RegisterTools()
{
    // === 現有內建 tools 照常註冊 ===
    // MenuItemTool, SelectGameObjectTool, ... (不變)

    // === 新增：自動發現外部 tools ===
    DiscoverExternalTools();

    // === BatchExecuteTool 最後註冊 ===
    BatchExecuteTool batchExecuteTool = new BatchExecuteTool(this);
    _tools.Add(batchExecuteTool.Name, batchExecuteTool);
}

private void DiscoverExternalTools()
{
    int discoveredCount = 0;

    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // 部分 type 可能因缺失依賴而無法載入
            types = ex.Types.Where(t => t != null).ToArray();
        }

        foreach (var type in types)
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(McpToolBase)))
                continue;

            // 跳過已由內建註冊的 tool
            // 透過 type 所屬 assembly 判斷：mcp-unity package 自身的 assembly 跳過
            if (type.Assembly == typeof(McpToolBase).Assembly)
                continue;

            try
            {
                var tool = (McpToolBase)Activator.CreateInstance(type);

                if (string.IsNullOrEmpty(tool.Name))
                {
                    McpLogger.LogWarning($"External tool {type.FullName} has empty Name, skipping");
                    continue;
                }

                if (_tools.ContainsKey(tool.Name))
                {
                    McpLogger.LogWarning($"External tool '{tool.Name}' ({type.FullName}) conflicts with existing tool, skipping");
                    continue;
                }

                _tools[tool.Name] = tool;
                discoveredCount++;
                McpLogger.LogInfo($"Discovered external tool: '{tool.Name}' from {type.Assembly.GetName().Name}");
            }
            catch (Exception ex)
            {
                McpLogger.LogWarning($"Failed to instantiate external tool {type.FullName}: {ex.Message}");
            }
        }
    }

    if (discoveredCount > 0)
    {
        McpLogger.LogInfo($"Total external tools discovered: {discoveredCount}");
    }
}
```

**設計決策**：

- 用 `type.Assembly == typeof(McpToolBase).Assembly` 判斷是否為 mcp-unity 自身的 tool，而非 `_tools.ContainsKey()`。原因：內建 tool 名稱和外部 tool 名稱衝突時應報警告，而非靜默跳過。
- `ReflectionTypeLoadException` 用 `ex.Types.Where(t => t != null)` 取得可載入的 types，避免整個 assembly 被跳過。
- `Activator.CreateInstance` 需要 public parameterless constructor，失敗時 log warning 並跳過。

#### 改動 3：新增 `list_tools` 內建方法

在 `McpUnitySocketHandler.OnMessage` 的路由邏輯中，加入對 `list_tools` 的處理：

```csharp
// McpUnitySocketHandler.cs — OnMessage 路由新增
if (method == "list_tools")
{
    tcs.SetResult(HandleListTools());
}
else if (_server.TryGetTool(method, out var tool))
{
    // ... 現有邏輯
}
```

```csharp
private JObject HandleListTools()
{
    var tools = new JArray();

    foreach (var kvp in _server.Tools)
    {
        // 只回傳外部 tools（內建 tools 已在 Node.js 側 hardcode）
        if (kvp.Value.GetType().Assembly == typeof(McpToolBase).Assembly)
            continue;

        tools.Add(new JObject
        {
            ["name"] = kvp.Value.Name,
            ["description"] = kvp.Value.Description,
            ["parameterSchema"] = kvp.Value.ParameterSchema,
            ["isAsync"] = kvp.Value.IsAsync
        });
    }

    return new JObject
    {
        ["success"] = true,
        ["tools"] = tools,
        ["count"] = tools.Count
    };
}
```

**注意**：`_server.Tools` 需要公開 `_tools` dictionary（或提供 accessor）。目前 `McpUnityServer` 有 `TryGetTool()`，需新增 `Tools` property 或 `GetAllTools()` 方法。

---

### 3.2 Node.js 側改動

#### 改動 1：JSON Schema → Zod 轉換器

MCP SDK 的 `server.tool()` 要求 `ZodRawShape`，而 C# 側回傳 JSON Schema。需要一個基礎轉換器：

```typescript
// Server~/src/utils/schemaConverter.ts

import * as z from 'zod';

/**
 * Convert a JSON Schema object to a Zod raw shape for MCP SDK registration.
 * Supports basic types (string, number, integer, boolean, array, object).
 * Complex/nested schemas fall back to z.any() — Unity C# does the real validation.
 */
export function jsonSchemaToZodShape(schema: any): z.ZodRawShape {
  const shape: z.ZodRawShape = {};

  if (!schema?.properties || typeof schema.properties !== 'object') {
    return shape;
  }

  const required = new Set<string>(Array.isArray(schema.required) ? schema.required : []);

  for (const [key, prop] of Object.entries<any>(schema.properties)) {
    let zodType: z.ZodTypeAny;

    switch (prop.type) {
      case 'string':
        zodType = z.string();
        if (prop.enum) {
          zodType = z.enum(prop.enum as [string, ...string[]]);
        }
        break;
      case 'integer':
        zodType = z.number().int();
        break;
      case 'number':
        zodType = z.number();
        break;
      case 'boolean':
        zodType = z.boolean();
        break;
      case 'array':
        zodType = z.array(z.any());
        break;
      case 'object':
        zodType = z.record(z.any());
        break;
      default:
        zodType = z.any();
        break;
    }

    if (prop.description) {
      zodType = zodType.describe(prop.description);
    }

    if (!required.has(key)) {
      zodType = zodType.optional();
    }

    shape[key] = zodType;
  }

  return shape;
}
```

**覆蓋範圍**：`string`（含 enum）、`number`、`integer`、`boolean`、`array`、`object`。涵蓋 >95% 的 tool 參數使用場景。複雜巢狀結構用 `z.any()` fallback，由 C# 側做實際驗證。

#### 改動 2：`index.ts` 新增動態工具註冊

```typescript
// Server~/src/tools/dynamicTools.ts

import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { jsonSchemaToZodShape } from '../utils/schemaConverter.js';

/**
 * Query Unity for external tools and register them dynamically.
 * Called after WebSocket connection is established.
 * Only registers tools not already registered by built-in modules.
 */
export async function registerDynamicTools(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
): Promise<number> {
  try {
    const response = await mcpUnity.sendRequest({
      method: 'list_tools',
      params: {}
    });

    if (!response.success || !response.tools) {
      logger.info('No external tools found in Unity');
      return 0;
    }

    let registered = 0;

    for (const tool of response.tools) {
      try {
        const zodShape = jsonSchemaToZodShape(tool.parameterSchema);

        server.tool(
          tool.name,
          tool.description || `External tool: ${tool.name}`,
          zodShape,
          async (params: any) => {
            logger.info(`Executing external tool: ${tool.name}`);

            try {
              const result = await mcpUnity.sendRequest({
                method: tool.name,
                params
              });

              // 統一格式化回傳
              const text = result.message || JSON.stringify(result, null, 2);
              return {
                content: [{ type: 'text' as const, text }],
                ...(result.success !== undefined ? { data: result } : {})
              };
            } catch (error) {
              logger.error(`External tool ${tool.name} failed`, error);
              throw error;
            }
          }
        );

        registered++;
        logger.info(`Registered external tool: ${tool.name}`);
      } catch (error) {
        logger.error(`Failed to register external tool ${tool.name}`, error);
      }
    }

    if (registered > 0) {
      logger.info(`Total external tools registered: ${registered}`);
      // 通知 MCP client 工具清單已更新
      server.sendToolListChanged();
    }

    return registered;
  } catch (error) {
    logger.error('Failed to query external tools from Unity', error);
    return 0;
  }
}
```

#### 改動 3：`index.ts` 在 `server.connect()` 前完成動態註冊

> **重要**：動態工具必須在 `server.connect()` 之前註冊，確保 MCP client 第一次查詢 `tools/list` 時所有工具已就緒。

```typescript
// index.ts — startServer() 中

async function startServer() {
  try {
    // 1. 先連接 Unity WebSocket（在 MCP client 連接前）
    //    start() 連接失敗時不會 crash，只會 warn 並繼續
    await mcpUnity.start();

    // 2. 如果 Unity 已連接，查詢並註冊外部 tools
    if (mcpUnity.isConnected) {
      const dynamicCount = await registerDynamicTools(server, mcpUnity, toolLogger);
      if (dynamicCount > 0) {
        serverLogger.info(`Registered ${dynamicCount} external tools from Unity`);
      }
    }

    // 3. 最後才連接 MCP transport — 此時 tools/list 已包含所有工具
    const stdioTransport = new StdioServerTransport();
    await server.connect(stdioTransport);

    serverLogger.info('MCP Server started');

    const clientName = server.server.getClientVersion()?.name || 'Unknown MCP Client';
    serverLogger.info(`Connected MCP client: ${clientName}`);
  } catch (error) {
    serverLogger.error('Failed to start server', error);
    process.exit(1);
  }
}
```

---

## 4. 外部 Tool 範例

### 4.1 最簡範例（無參數）

```csharp
// Assets/MyProject/Editor/MCP/PingTool.cs
using McpUnity.Tools;
using Newtonsoft.Json.Linq;

public class PingTool : McpToolBase
{
    public PingTool()
    {
        Name = "ping";
        Description = "Simple ping tool that returns pong";
    }

    public override JObject Execute(JObject parameters)
    {
        return new JObject
        {
            ["success"] = true,
            ["message"] = "pong"
        };
    }
}
```

### 4.2 帶參數範例

```csharp
// Assets/ProjectT/Editor/AIQAMCP/CBStartBattleTool.cs
using McpUnity.Tools;
using Newtonsoft.Json.Linq;

public class CBStartBattleTool : McpToolBase
{
    public CBStartBattleTool()
    {
        Name = "cb_start_battle";
        Description = "Start a new CardBattle with specified config";
    }

    public override JObject ParameterSchema => JObject.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""config_name"": { ""type"": ""string"", ""description"": ""CBBattleConfigSO asset name"" },
            ""rng_seed"":    { ""type"": ""integer"", ""description"": ""Optional RNG seed for deterministic replay"" }
        },
        ""required"": [""config_name""]
    }");

    public override JObject Execute(JObject parameters)
    {
        string configName = parameters["config_name"]?.ToString();
        int? rngSeed = parameters["rng_seed"]?.ToObject<int?>();

        // ProjectT 專屬邏輯...

        return new JObject
        {
            ["success"] = true,
            ["message"] = $"Battle started with config '{configName}'" + (rngSeed.HasValue ? $", seed={rngSeed}" : "")
        };
    }
}
```

### 4.3 非同步範例

```csharp
// Assets/ProjectT/Editor/AIQAMCP/WaitForBattleEndTool.cs
using System.Collections;
using System.Threading.Tasks;
using McpUnity.Tools;
using Newtonsoft.Json.Linq;
using Unity.EditorCoroutines.Editor;

public class WaitForBattleEndTool : McpToolBase
{
    public WaitForBattleEndTool()
    {
        Name = "cb_wait_for_battle_end";
        Description = "Wait for the current battle to finish (async polling)";
        IsAsync = true;
    }

    public override JObject ParameterSchema => JObject.Parse(@"{
        ""type"": ""object"",
        ""properties"": {
            ""timeout"": { ""type"": ""number"", ""description"": ""Max wait time in seconds (default: 60)"" }
        }
    }");

    public override void ExecuteAsync(JObject parameters, TaskCompletionSource<JObject> tcs)
    {
        float timeout = parameters["timeout"]?.ToObject<float>() ?? 60f;
        EditorCoroutineUtility.StartCoroutineOwnerless(WaitCoroutine(timeout, tcs));
    }

    private IEnumerator WaitCoroutine(float timeout, TaskCompletionSource<JObject> tcs)
    {
        // ... polling 邏輯
        yield return null;
    }
}
```

---

## 5. 時序圖

```
MCP Client          Node.js Server          Unity Editor
    │                    │                       │
    │                    │── mcpUnity.start() ──→│  (1) 先連 Unity
    │                    │←── connected ─────────│
    │                    │                       │
    │                    │── list_tools ────────→│  (2) 查詢外部工具
    │                    │                       │── DiscoverExternalTools()
    │                    │                       │   (assembly scan at startup)
    │                    │←── { tools: [...] } ──│
    │                    │                       │
    │                    │── registerDynamicTools()  (3) 註冊動態工具
    │                    │   server.tool(name, zodShape, handler)
    │                    │                       │
    │←── server.connect()│                       │  (4) 最後才接 MCP client
    │                    │                       │
    │── tools/list ─────→│                       │  (5) 第一次查詢已包含所有工具
    │←── (built-in + dynamic tools) ─────────────│
    │                    │                       │
    │── tools/call ─────→│                       │
    │   "cb_start_battle"│── sendRequest() ────→│
    │                    │                       │── CBStartBattleTool.Execute()
    │                    │←── result ────────────│
    │←── result ─────────│                       │
```

---

## 6. 檔案變更

### 新增檔案

| 檔案 | 說明 | 大小 |
|------|------|------|
| `Server~/src/utils/schemaConverter.ts` | JSON Schema → Zod 轉換器 | ~50 行 |
| `Server~/src/tools/dynamicTools.ts` | 動態工具註冊邏輯 | ~60 行 |

### 修改檔案

| 檔案 | 變更 | 大小 |
|------|------|------|
| `Editor/Tools/McpToolBase.cs` | 新增 `ParameterSchema` virtual property | ~8 行 |
| `Editor/UnityBridge/McpUnityServer.cs` | `RegisterTools()` 加 `DiscoverExternalTools()` + 公開 `Tools` accessor | ~40 行 |
| `Editor/UnityBridge/McpUnitySocketHandler.cs` | 加 `list_tools` 路由 + `HandleListTools()` | ~25 行 |
| `Server~/src/index.ts` | 連線後呼叫 `registerDynamicTools()` | ~5 行 |

---

## 7. 已知風險與限制

| 風險 | 嚴重度 | 說明 | 緩解 |
|------|--------|------|------|
| Zod 轉換不完整 | 中 | 複雜巢狀 JSON Schema 會 fallback 到 `z.any()` | AI client 仍能看到 description，C# 側做實際驗證 |
| Domain Reload 後工具變化 | 低 | 新增/刪除 C# tool 後 Node.js 側的註冊不同步 | Domain Reload 後 C# 側重新 scan；Node.js 側用方案 A（不重新註冊，失敗時回 error） |
| Assembly 掃描效能 | 低 | 大型專案可能有數百個 assembly | 只在 server 啟動時掃描一次，僅對非 mcp-unity assembly 的 types 做 SubclassOf 檢查 |
| `sendToolListChanged()` 支援 | 中 | 需確認 MCP SDK 版本是否支援此 notification | 若不支援，MCP client 需手動 refresh tools list |
| 外部 tool 缺少 parameterless constructor | 低 | `Activator.CreateInstance` 失敗 | try-catch + warning log，不影響其他 tools |

---

## 8. 未來擴展

### 8.1 外部 Resource 動態發現

同樣的模式可應用於 `McpResourceBase`：assembly 掃描 + `list_resources` + 動態註冊。

### 8.2 Hot Reload 支援

當 script recompilation 完成後（`OnAfterAssemblyReload`），自動重新掃描 tools 並通知 Node.js 更新。需要：
- `McpUnityServer.OnAfterAssemblyReload()` 中重新呼叫 `DiscoverExternalTools()`
- Node.js 側 reconnect callback 中重新呼叫 `registerDynamicTools()`
- MCP SDK 需支援 tool 的移除/更新（或 server restart）

### 8.3 Parameter Validation Attribute

取代手動 JSON string，用 C# attribute 定義參數：

```csharp
[McpParameter("config_name", type: "string", required: true, description: "Config asset name")]
[McpParameter("rng_seed", type: "integer", description: "Optional RNG seed")]
public class CBStartBattleTool : McpToolBase { ... }
```

自動從 attribute 生成 `ParameterSchema`。但增加了 mcp-unity package 的 API surface，需謹慎評估。
