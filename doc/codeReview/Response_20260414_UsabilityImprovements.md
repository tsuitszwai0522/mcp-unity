# Code Review Response — P0/P2/P4 Usability 改善

- **日期**: 2026-04-14
- **審查對象**: `doc/codeReview/Request_20260414_UsabilityImprovements.md`
- **審查結果**: **需修改後再合併**
- **Critical**: 0
- **Major**: 4
- **Minor**: 2
- **本機驗證**:
  - `cd Server~ && npm test -- --runInBand` → 7 suites / 96 tests passed；仍有 Jest open-handle 警告
  - `cd Server~ && npm run build` → pass
  - Unity Test Runner 未在本次審查重跑

---

## 維度 1. 代碼質素 (Code Quality)

整體方向正確：三個 friction point 都用低侵入方式補齊，沒有破壞 `get_gameobject` 單一結果合約，也沒有把 `include_values` 誤塞進 C# Localization 層。`screenshot_game_view` 改成 `IsAsync=true` 後，`McpUnitySocketHandler` 與 `BatchExecuteTool` 都會依 `IsAsync` 分流，這點我確認過，轉換本身可接受。

主要問題在邊界防線與 release 一致性：

- 🟡 **Major** — `GetGameObjectsByNameTool` 只在 TS schema 限制 `limit` / `maxDepth`，Unity 端沒有自己的驗證。由於 `batch_execute` 會在 Unity 端直接呼叫 inner tool，且 WebSocket bridge 也不是只能經過這個 TS wrapper，C# tool 不能假設所有 caller 都被 zod 擋過。`limit < 0` 會造成 `RemoveRange` 例外；`limit > 1000` 會繞過 advertised max；`maxDepth < -1` 目前會被 `GameObjectToJObject` 視為 unlimited traversal。
- 🟡 **Major** — `loc_get_entries include_values=true` 會把所有 entries 直接塞進 text content，且 value 中的換行沒有 escape。這會讓大型表格炸 token budget，也會讓 `key: value` 行格式在多行 value 時失真。
- 🟡 **Major** — 宣告 v1.12.0 release，但版本 metadata 未一致更新：`package.json` 已變成 1.12.0，`Server~/package.json` / `Server~/package-lock.json` 仍是 1.0.0，`server.json` 仍是 1.2.1。這與 repo 自己的 release/version bump checklist 衝突。
- 🟡 **Major** — 新增 TS wrapper / schema 行為缺少可重跑的 regression tests。live smoke-test 有價值，但不能防止下一次 wrapper/schema 改動把 `include_values`、`force_focus` 或 `get_gameobjects_by_name` 轉發合約弄壞。
- 🟢 **Minor** — `AGENTS.md` 的 current tool list 沒有加入 `get_gameobjects_by_name`，也沒有補 `loc_get_entries include_values` / `screenshot_game_view force_focus`。該檔自己的 update policy 明確要求 tool 增刪改名時更新。
- 🟢 **Minor** — `GlobToRegex` 目前可用，但手寫 metachar escape 較脆弱；改用 `Regex.Escape` 處理 literal chunk 會降低後續維護成本。

---

## 維度 2. 優點 (Pros)

- ✅ 新增 `get_gameobjects_by_name` 而不是改 `get_gameobject` 回傳 array，這個 API compatibility 判斷是對的。
- ✅ `get_gameobjects_by_name` 預設 `maxDepth=0` / `includeChildren=false`，能避免多 match 場景下的回傳暴增。
- ✅ `loc_get_entries` 把 `include_values` 留在 TS wrapper，不污染 Unity 端既有資料合約，符合「C# 已回 entries，問題在 MCP text content」的根因分析。
- ✅ `screenshot_game_view force_focus` 是顯式 opt-in，避免預設打斷使用者目前 focus。
- ✅ `ScreenshotGameViewTool` 非 `force_focus` 路徑仍同步完成 `tcs.TrySetResult(CaptureGameView(...))`，延遲成本只落在 opt-in 路徑。

---

## 維度 3. 缺點與風險 (Cons & Risks)

### 3.1 🟡 Major — Unity 端缺少 `limit` / `maxDepth` 驗證

位置：`Editor/Tools/GetGameObjectsByNameTool.cs`

風險：
- `limit` 只在 `Server~/src/tools/getGameObjectTool.ts` 透過 zod 限制，但 `batch_execute` 的 inner operation 可繞過該 schema。
- `limit < 0` 會讓 `matches.RemoveRange(limit, ...)` 直接丟例外。
- `maxDepth < -1` 會被現有 `GameObjectToJObject` 邏輯當成 unlimited，與 TS schema 的 `.gte(-1)` 不一致。
- self-review F1 提到的 early-exit 也應該順手處理。雖然 `FindObjectsByType` 仍會配置全量陣列，但至少可避免寬 pattern 下繼續收集 match 與後續處理。

### 3.2 🟡 Major — `include_values` 缺少輸出上限與換行 escaping

位置：`Server~/src/tools/localizationTools.ts`

風險：
- `include_values=true` 對 10k entries 表格會把所有值放進 MCP text content。
- value 若含 `\n`，目前 `${e.key}: ${e.value}` 會把一個 entry 拆成多行，讓 agent 或人類 reviewer 誤讀。
- self-review F11 / F13 屬實，建議修。

### 3.3 🟡 Major — Release metadata 與 agent guide 未對齊 v1.12.0

位置：`package.json`、`Server~/package.json`、`Server~/package-lock.json`、`server.json`、`AGENTS.md`

風險：
- repo 的 `AGENTS.md` release checklist 要求 Unity package、Node server、MCP registry 三者版本一致更新。
- 若這次準備真的發 v1.12.0，Node package 與 registry metadata 不更新，外部 MCP registry / npm consumer 可能看不到這次能力。
- 如果專案實際上決定讓 Unity package、Node package、registry 三條版本線解耦，也應該先修改 release checklist，否則後續 reviewer 會反覆卡在同一個矛盾。

### 3.4 🟡 Major — 缺少新 wrapper/schema 的 regression tests

位置：`Server~/src/__tests__/`、`Editor/Tests/`

風險：
- 現有 `npm test` 96/96 pass，但沒有直接覆蓋 `loc_get_entries include_values`、`get_gameobjects_by_name` wrapper 註冊/轉發、`screenshot_game_view force_focus` schema。
- C# 新工具沒有測 `limit`、`maxDepth` validation、glob escape、prefab root scope。
- live smoke-test 可證明當下可用，但不能防止 future regression。

### 3.5 🟢 Minor — `GlobToRegex` 可維護性

位置：`Editor/Tools/GetGameObjectsByNameTool.cs`

目前 `*` / `?` / regex metachar escape 足夠支撐需求；不建議引入 `System.Management.Automation.WildcardPattern`。但長期看，手動列舉 metachar 容易在未來加 glob 語法時漏掉規則。可用 `Regex.Escape` 處理 literal run，讓意圖更清楚。

### 3.6 自我評估驗證

- **F1**：成立，建議修，並與 Unity 端 validation 一起做。
- **F7**：現行 `delayCall` 可接受；不建議為此改成 coroutine。需求只是讓 focus/repaint settle，不需要保證精確 render frame。
- **F11**：成立，建議加 `max_entries` 或硬上限。
- **F13**：成立，至少 escape `\r` / `\n`。
- **GlobToRegex / F4**：不需換成 `System.Management.Automation`；改用 `Regex.Escape` 是較合理的小改善。
- **版本 bump 策略**：新增 tool 用 minor 是合理的；但所有 release metadata 需一致，或明確修正 checklist。
- **IsAsync 轉換安全**：可接受；`McpUnitySocketHandler` 與 `BatchExecuteTool` 都支援 async tool。

---

## 維度 4. 改善建議 (Improvement Suggestions)

### 4.1 修正 `GetGameObjectsByNameTool` 參數驗證與 early-exit

建議修改方向：

```csharp
const int DefaultLimit = 100;
const int MaxLimit = 1000;

int maxDepth = parameters?["maxDepth"]?.ToObject<int?>() ?? 0;
if (maxDepth < -1)
{
    return McpUnitySocketHandler.CreateErrorResponse(
        "Parameter 'maxDepth' must be -1 or greater",
        "validation_error"
    );
}

int limit = parameters?["limit"]?.ToObject<int?>() ?? DefaultLimit;
if (limit < 1 || limit > MaxLimit)
{
    return McpUnitySocketHandler.CreateErrorResponse(
        $"Parameter 'limit' must be between 1 and {MaxLimit}",
        "validation_error"
    );
}

var truncated = false;
foreach (var go in all)
{
    if (!regex.IsMatch(go.name))
        continue;

    if (matches.Count >= limit)
    {
        truncated = true;
        break;
    }

    matches.Add(go);
}
```

Prefab recursion 也應該接收 `limit` 並回傳是否 truncated：

```csharp
private static bool CollectMatchesRecursive(
    GameObject root,
    Regex regex,
    bool includeInactive,
    int limit,
    List<GameObject> matches)
{
    if (root == null) return false;
    if (!includeInactive && !root.activeInHierarchy) return false;

    if (regex.IsMatch(root.name))
    {
        if (matches.Count >= limit)
            return true;
        matches.Add(root);
    }

    foreach (Transform child in root.transform)
    {
        if (CollectMatchesRecursive(child.gameObject, regex, includeInactive, limit, matches))
            return true;
    }

    return false;
}
```

### 4.2 修正 `loc_get_entries include_values` 上限與 escaping

建議以最小 API 延伸加入 `max_entries`，預設保守值例如 200，上限 1000：

```ts
max_entries: z
  .number()
  .int()
  .min(1)
  .max(1000)
  .optional()
  .describe('Max entries to render when include_values=true. Default: 200, max: 1000.'),
```

渲染時只影響 text content，不改 `data.entries`：

```ts
const escapeLine = (value: unknown) =>
  String(value ?? '').replace(/\r/g, '\\r').replace(/\n/g, '\\n');

const entries: Array<{ key: string; value: string }> = response.entries || [];
const maxEntries = Math.min(params.max_entries ?? 200, 1000);
const renderedEntries = entries.slice(0, maxEntries);
const truncated = entries.length > renderedEntries.length;
const summary = response.message || `Read ${entries.length} entries`;

const text = include_values && entries.length > 0
  ? [
      summary,
      ...renderedEntries.map((e) => `${escapeLine(e.key)}: ${escapeLine(e.value)}`),
      ...(truncated ? [`... truncated ${entries.length - renderedEntries.length} entries; refine filter or raise max_entries.`] : []),
    ].join('\n')
  : summary;
```

轉發 Unity 時要一併剝離 TS-only 參數：

```ts
const { include_values, max_entries, ...unityParams } = params;
const response = await mcpUnity.sendRequest({ method: name, params: unityParams });
```

### 4.3 對齊 release metadata 與 `AGENTS.md`

若 v1.12.0 是單一 release 版本，建議：

```json
// Server~/package.json
{
  "name": "mcp-unity-server",
  "version": "1.12.0"
}
```

```json
// server.json
{
  "version": "1.12.0",
  "packages": [
    {
      "identifier": "mcp-unity",
      "version": "1.12.0"
    }
  ]
}
```

同時更新 `Server~/package-lock.json` root version，以及 `AGENTS.md` 的 tool list：

```md
- `get_gameobjects_by_name` — Find all GameObjects whose name matches a glob pattern
- `screenshot_game_view` — Capture Game View screenshots; supports `force_focus`
- `loc_get_entries` — Read Localization entries; supports `include_values`
```

若版本線刻意解耦，則改成更新 `AGENTS.md` release checklist，明確定義何時 bump Unity package、何時 bump Node package、何時 bump registry metadata。

### 4.4 補 regression tests

建議至少補以下測試：

```ts
// Server~/src/__tests__/localizationTools.test.ts
it('renders loc_get_entries values only when include_values=true and strips TS-only params', async () => {
  // assert sendRequest params exclude include_values / max_entries
  // assert text contains escaped key/value lines
});
```

```ts
// Server~/src/__tests__/getGameObjectTool.test.ts
it('registers get_gameobjects_by_name and forwards glob params to Unity', async () => {
  // assert method === 'get_gameobjects_by_name'
  // assert response is returned as JSON text
});
```

```ts
// Server~/src/__tests__/screenshotTools.test.ts
it('forwards force_focus for screenshot_game_view', async () => {
  // assert params include force_focus: true
  // assert MCP result is image content
});
```

C# 端可新增 EditMode tests，至少覆蓋：

```csharp
[Test]
public void Execute_ReturnsValidationError_WhenLimitIsOutOfRange() { }

[Test]
public void Execute_ReturnsValidationError_WhenMaxDepthIsBelowMinusOne() { }

[Test]
public void Execute_Truncates_WhenMatchesExceedLimit() { }
```

---

## 維度 5. 需求覆蓋率 (Spec Coverage)

### 5.1 已讀需求 / 基準

- `doc/codeReview/Request_20260414_UsabilityImprovements.md`
- `doc/requirement/feature_competitive_tools.md`：既有 screenshot tool spec
- `doc/requirement/feature_localization_mcp.md`：既有 Localization tool spec
- repo `AGENTS.md`：release/version bump checklist 與 update policy

本次沒有對應的 `doc/requirement/feature_*UsabilityImprovements*.md`。因此 `get_gameobjects_by_name` 以 CRR 描述作為需求基準；`screenshot_game_view` 與 `loc_get_entries` 則以既有需求文件加上 CRR usability delta 作為基準。

### 5.2 已覆蓋項目

- ✅ P0：`loc_get_entries include_values=true` 能把 entries 渲染進 MCP text content。
- ✅ P2：新增獨立 `get_gameobjects_by_name`，未破壞 `get_gameobject` 單一結果合約。
- ✅ P4：`screenshot_game_view force_focus=true` 走 async delay path，預設不搶 focus。
- ✅ Node build 與既有 Jest tests 通過。

### 5.3 遺漏清單

- 🟡 `GetGameObjectsByNameTool` Unity 端未完整實作 TS schema 宣告的 `limit` / `maxDepth` 防線。
- 🟡 v1.12.0 release metadata 未符合 `AGENTS.md` release/version bump checklist。
- 🟢 `AGENTS.md` tool list 未更新新工具與新參數。

### 5.4 未測試清單

- 🟡 `loc_get_entries include_values=true` text 渲染、TS-only 參數剝離、newline escaping、輸出 truncation。
- 🟡 `get_gameobjects_by_name` TS wrapper 註冊與 method forwarding。
- 🟡 `screenshot_game_view force_focus` schema 與 forwarding。
- 🟡 `GetGameObjectsByNameTool` C# validation、glob matching、limit truncation、prefab scope。

---

## Review 意見追蹤

- [ ] 🟡 補上 `GetGameObjectsByNameTool` Unity 端 `limit` / `maxDepth` 驗證與 early-exit
- [ ] 🟡 為 `loc_get_entries include_values` 加輸出上限與 newline escaping
- [ ] 🟡 對齊 v1.12.0 release metadata，或明確更新 release checklist 表示版本線解耦
- [ ] 🟡 補新增 wrapper/schema/C# tool 的 regression tests

---

## Refactor Prompt

根據 `doc/codeReview/Response_20260414_UsabilityImprovements.md` 的審查意見，請執行以下修正：

### 🔴 Critical（必須修復）

無。

### 🟡 Major（強烈建議）

1. 在 `Editor/Tools/GetGameObjectsByNameTool.cs` 補 Unity 端 `limit` 與 `maxDepth` validation：`limit` 必須介於 1..1000，`maxDepth` 必須 `>= -1`。同時將 `limit` 套用到 scene loop 與 prefab recursion，遇到第 `limit + 1` 個 match 時設定 `truncated=true` 並停止收集。
2. 在 `Server~/src/tools/localizationTools.ts` 為 `loc_get_entries include_values` 增加 `max_entries`（預設 200、上限 1000）或等價安全上限，並 escape value 中的 `\r` / `\n`，避免大型表格與多行 value 破壞 MCP text content。
3. 對齊 release metadata：若本次是 v1.12.0 單一 release，更新 `Server~/package.json`、`Server~/package-lock.json`、`server.json` 的版本；若版本線刻意解耦，更新 `AGENTS.md` release checklist 說明解耦規則。同時把 `get_gameobjects_by_name` 與新參數補進 `AGENTS.md` tool list。
4. 補 regression tests：至少覆蓋 `loc_get_entries include_values` text 渲染與 TS-only 參數剝離、`get_gameobjects_by_name` wrapper 註冊/轉發、`screenshot_game_view force_focus` schema/轉發，以及 C# `GetGameObjectsByNameTool` 的 validation/truncation。

### 涉及檔案

- `Editor/Tools/GetGameObjectsByNameTool.cs`
- `Server~/src/tools/localizationTools.ts`
- `Server~/src/tools/getGameObjectTool.ts`
- `Server~/src/tools/screenshotTools.ts`
- `Server~/src/__tests__/`
- `Editor/Tests/`
- `Server~/package.json`
- `Server~/package-lock.json`
- `server.json`
- `AGENTS.md`

---

⚠️ **若有對應的 Implementation Tracker** (`doc/requirement/feature_{name}_tracker.md`)，
請在對應 Phase 的「關鍵決策」區塊中新增 `[Review Fix]` 標籤記錄本次修改內容，
並在「關聯審查」區塊連結本審查報告。
