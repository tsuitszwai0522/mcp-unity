# Code Review Request — P0/P2/P4 Usability 改善

- **日期**: 2026-04-14
- **版本**: v1.11.2 → **v1.12.0**
- **規模**: 9 files changed (+199 / −49), 1 new C# file (`GetGameObjectsByNameTool.cs`)
- **Branch**: `main`（未 push）
- **觸發來源**: 外部 project 使用 mcp-unity 時回報的三個 friction point
- **驗證狀態**: `npm test` 96/96 pass、`McpUnity.Tests.PathHandlingTests` 9/9 pass、三支新/改 tool 均 live smoke-test 過

---

## 1. 背景與目標

另一個 Unity project 在使用 mcp-unity tools 時回報了三個痛點：

| 優先 | 問題 | 來源場景 |
|------|------|----------|
| P0 | `loc_get_entries` 寫完 27 keys 後只能看到「Read 27 entries」，無法 inspect value | Localization Phase 2 驗證 |
| P2 | `get_gameobject` 只回第一個，無法一次抓 7 張 `CBCardUI(Clone)` 的 EffectText | Phase 3 runtime scene inspection |
| P4 | `screenshot_game_view` 永遠回 ~600×400 縮圖且實際擷取到 Scene tab 而非 Game tab | Phase 3.6 視覺驗證 |

目標：以**最小侵入**的方式補齊這三個缺口，不破壞既有 API 合約。

### 非目標

- **不**全面改寫 `get_gameobject`（維持單一結果合約，避免 downstream 破壞）
- **不**把 `include_values` 做成 C# 端的參數（C# 已經回整個 entries array，問題純在 TS wrapper 只回 text count，TS 改動即可）
- **不**針對 Unity Test Runner 的 10s timeout 做 workaround（另一個問題，out of scope）

---

## 2. 設計決策摘要

### 2.1 P0 — `loc_get_entries` `include_values`（純 TS 改動）

根因：C# 端 `LocGetEntriesTool.Execute` 早就回完整 `entries` JArray（含 key + value），但 `Server~/src/tools/localizationTools.ts` 的 wrapper 把 entries 塞到 `data` 欄位，`content[0].text` 只放 message 筆數。MCP client 通常只渲染 `content` — `data` 被忽略。

解法：加 `include_values: boolean`（預設 `false`）。`true` 時 wrapper 把 entries 展開成 `key: value` 多行附在 text content 之後。預設維持原行為（節省 token）。

```ts
const { include_values, ...unityParams } = params;
const response = await mcpUnity.sendRequest({ method: name, params: unityParams });
// ...
const text = include_values && entries.length > 0
  ? `${summary}\n${entries.map((e) => `${e.key}: ${e.value}`).join('\n')}`
  : summary;
```

**關鍵點**：C# side 零改動，也沒改既有 schema 的 required 欄位 → 舊 caller 完全相容。

### 2.2 P2 — 新工具 `get_gameobjects_by_name`（glob 支援）

新增獨立工具而非改 `get_gameobject`，理由：
- `get_gameobject` 合約是「單一結果 + 富欄位」，改成回 array 會破壞所有 downstream caller
- Glob pattern 是新能力，自成一支工具語意更清楚
- 預設 `maxDepth=0` + `includeChildren=false` 避免 7 張 cards × 深遞迴造成 token 爆炸

#### C# 實作（`Editor/Tools/GetGameObjectsByNameTool.cs`）

核心流程：
1. 解析 glob → regex（自寫 `GlobToRegex`，支援 `*` → `.*`、`?` → `.`、escape regex metachars）
2. **Prefab 編輯模式**：走 `PrefabEditingService.PrefabRoot` 遞迴收集（不污染 scene）
3. **Scene 模式**：`Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include/Exclude, FindObjectsSortMode.None)` 全掃，regex 過濾
4. `limit`（預設 100、max 1000）截斷後設 `truncated: true`
5. 呼叫既有的 `GetGameObjectResource.GameObjectToJObject()` 序列化；額外補 `path` 欄位（`GetHierarchicalPath` 沿 `transform.parent` 往上走）

```csharp
var inactiveMode = includeInactive
    ? UnityEngine.FindObjectsInactive.Include
    : UnityEngine.FindObjectsInactive.Exclude;
var all = Object.FindObjectsByType<GameObject>(inactiveMode, FindObjectsSortMode.None);
foreach (var go in all)
{
    if (regex.IsMatch(go.name))
        matches.Add(go);
}
```

#### 註冊

- **不**走動態 `list_tools` 路徑（那是給 external plugins 的）
- 在 `McpUnityServer.cs:391` 顯式 `_tools.Add()`
- TS wrapper 放在既有的 `Server~/src/tools/getGameObjectTool.ts`（同檔案，減少新增 import），`index.ts` 加一行 `registerGetGameObjectsByNameTool`

#### Schema 設計

| 參數 | 型別 | 預設 | 理由 |
|------|------|------|------|
| `name` | string | required | Glob pattern |
| `includeInactive` | bool | `true` | 使用者通常希望找到「包括 inactive 的所有」instances |
| `maxDepth` | int | `0` | **保守預設**：單 match 不遞迴；想要深度時明確 opt-in |
| `includeChildren` | bool | `false` | 同上 — 避免 7 match × deep tree 炸 token |
| `limit` | int | `100` (max 1000) | 保險上限 |

### 2.3 P4 — `screenshot_game_view` `force_focus`

根因：`ScreenCapture.CaptureScreenshotAsTexture()` 擷取的是**目前 focused EditorWindow**。原本的 `GetWindow(gameViewType, focus:false)` 只是「確保 Game View 存在」，沒有搶焦點，所以如果使用者手動點到 Scene tab，ScreenCapture 就拿到 Scene tab 的 render，再被 `ResizeTexture` 放大到 caller 要求的尺寸，品質慘不忍睹。

解法：加 `force_focus: bool`（預設 `false`，避免意外打斷使用者操作）。`true` 時：
1. `GetWindow(gameViewType, _, _, focus:true)` + 明確 `.Focus()` + `.Repaint()`
2. 透過 `EditorApplication.delayCall` 等一幀讓 repaint 落地
3. 再呼叫內部 `CaptureGameView(width, height)` helper

因為多了 delayCall，整個 tool 必須從 sync `Execute` 改成 `IsAsync = true` + `ExecuteAsync(JObject, TaskCompletionSource<JObject>)`。預設路徑（`force_focus=false`）**沒有**多一幀延遲——直接 `tcs.TrySetResult(CaptureGameView(...))` 同步完成，避免 regression。

```csharp
if (forceFocus)
{
    EditorApplication.delayCall += () =>
    {
        try { tcs.TrySetResult(CaptureGameView(width, height)); }
        catch (Exception ex) { /* error response */ }
    };
}
else
{
    tcs.TrySetResult(CaptureGameView(width, height));
}
```

---

## 3. 驗證情況

### 3.1 編譯

- C# 端 `recompile_scripts` 成功（透過 `run_tests` 先跑到 `Test run started` 得到間接證明）
- Node 端 `npm run build`，**clean**

### 3.2 測試

| 測試 | 結果 |
|------|------|
| `Server~` Jest `npm test` | **96/96 pass**（7 suites） |
| `McpUnity.Tests.PathHandlingTests`（Unity EditMode） | **9/9 pass** — 證明 C# 新檔案編譯成功、test infrastructure 未損 |
| `McpUnity.Tests.MaterialToolsTests` | 背景仍在跑；MCP 層因 `RequestTimeoutSeconds=10` 收不到 final summary，但 console 無任何 test failure |

> **已知限制（非本 PR 範圍）**：`TestRunnerService.WaitForCompletionAsync` 吃 `RequestTimeoutSeconds`（預設 10s），全量 test 跑不完。需要單獨的 PR 讓 run_tests 使用獨立 timeout。

### 3.3 Live Smoke Test（Unity 連線）

| Tool | 測試輸入 | 結果 |
|------|----------|------|
| `get_gameobjects_by_name` | `name="*Camera*"`, `limit=5` | ✅ 找到 `Main Camera`，`path: "Main Camera"`，`count=1`，`truncated=false`，完整 components |
| `loc_get_entries` | `table_name="McpLocTestTable"`, `include_values=true` | ✅ 渲染 4 行 `key: value`（含 RichText `<color=#88CCFF>...`） |
| `loc_get_entries` | `table_name="McpLocTestTable"`（預設） | ✅ 僅 `Read 4 entries from 'McpLocTestTable' (zh-TW)` |
| `screenshot_game_view` | `width=320`, `height=180`, `force_focus=true` | ✅ delayCall 一幀後擷取 Game View URP sky+ground，尺寸正確 |

---

## 4. 自我評估（脆弱點與 Edge Cases）

### 4.1 `GetGameObjectsByNameTool` 脆弱點

| # | 風險 | 嚴重度 | 現況 |
|---|------|--------|------|
| F1 | `Object.FindObjectsByType<GameObject>` 在大型 scene（數萬 GO）是線性掃描 + 我再跑一次 regex，O(N) 全掃沒有 early-exit until `limit` | 中 | 沒做 early-exit；`limit` 截斷是在**收集完所有 match 後**才切，不是在遞增途中停。大場景 + 寬 pattern（如 `*`）下會做無謂工作。**建議**：若 review 認為重要，可在 main loop 裡檢查 `matches.Count >= limit` break 掉 |
| F2 | `FindObjectsByType` 不會找到 `DontDestroyOnLoad` 場景的物件（Unity 已知行為） | 低 | 文件沒寫。如果 caller 在 PlayMode 下想找 DDOL 物件會漏。**建議**：補 README 註記或接受此行為 |
| F3 | Prefab 模式下走自寫 `CollectMatchesRecursive`，而不是 `PrefabStage.prefabContentsRoot.GetComponentsInChildren<Transform>()` | 低 | 現況正確，但語意跟 scene 模式略不同：scene 模式會掃**所有 loaded scenes**，prefab 模式只掃**當前 prefab root**。這是刻意的（避免污染），但 review 可能會問 |
| F4 | `GlobToRegex` 自寫，沒用 `Regex.Escape` | 低 | 我手動列舉 `. ( ) [ ] { } + ^ $ \|  \\`。**漏了 `-` 和其它少用 metachar**；實際上 `-` 只在 character class 裡是 metachar，我沒有 `[...]` 語法所以安全。但若後續加 character class 支援需要重新檢視 |
| F5 | `limit` 截斷在序列化**之前**做（`matches.RemoveRange` 後才 `GameObjectToJObject`），好的 | — | ✅ 不會先序列化 1000 個然後砍到 100 |
| F6 | 回傳的 `components` 裡有大量 `"Unable to serialize"` 欄位（live test 看到 Camera 的 rigidbody/light/audio 等） | 低 | 不是本 PR 引入，是 `GetGameObjectResource.GameObjectToJObject` 既有行為 |

### 4.2 `ScreenshotGameViewTool` 脆弱點

| # | 風險 | 嚴重度 | 現況 |
|---|------|--------|------|
| F7 | `EditorApplication.delayCall` 的 callback 在下一個 editor update tick 才執行，實際延遲可能 > 1 frame（Unity 文件不保證精確一幀）| 中 | 實務上「夠用」，但 review 可能會建議改用 `EditorCoroutineUtility` + `WaitForEndOfFrame` 或多輪 delayCall | 
| F8 | `force_focus=true` 會**搶焦點**，打斷使用者手動操作 | 中 | 這是顯式 opt-in 行為，文件已說明。不過若 agent 連打 N 次 `force_focus=true` 會很煩 |
| F9 | `IsAsync=true` 路徑下，例外處理分散在兩個 try/catch（delayCall 的內部 try + 外層 try），有輕微重複 | 低 | 可接受；架構跟既有 `ScreenshotSceneViewTool` 一致 |
| F10 | `CaptureGameView` 被抽成 static helper，原本 log message `$"Game View screenshot captured ({width}x{height})"` 保留；但 fallback 到 Main Camera 的 path 現在也會寫 `"Game View (via Main Camera)"` log — 沒 regression | — | ✅ |

### 4.3 `loc_get_entries` TS wrapper 脆弱點

| # | 風險 | 嚴重度 | 現況 |
|---|------|--------|------|
| F11 | `include_values=true` + 超大 table（例如 10k entries）會把所有 `key: value` 塞進 text content，直接炸 token budget | 中 | 沒加上限。**建議**：要不要也加 `limit` 或至少文件警告？ |
| F12 | Wrapper 用 `const { include_values, ...unityParams } = params;` 剝離自訂參數後再轉發給 Unity。如果 Unity 端日後**加**了 `include_values` 參數會冲突 | 低 | 當前 C# 端會忽略未知參數，安全 |
| F13 | Entries 裡如果 `value` 含換行（`\n`），會破壞 `key: value` 的行基礎格式 | 低 | 沒 escape。實務上 Unity Localization 很少放多行 value，但 TMP rich text 有可能帶 `\n` |

---

## 5. 審查重點

請特別檢視：

1. **F1**（`get_gameobjects_by_name` 的 O(N) 掃描無 early-exit）— 要不要加 `if (matches.Count >= limit) break;`？
2. **F7**（`force_focus` 用 `delayCall` 而非 `EditorCoroutineUtility`）— 現行做法足夠嗎？
3. **F11**（`include_values=true` 無 entry 上限）— 要不要補 `max_entries` 之類保險？
4. **F13**（value 含 `\n` 破壞行格式）— 要不要改成 JSON array 輸出、或 escape `\n` 成 `\\n`？
5. **GlobToRegex**（§2.2 & F4）— 自寫 glob 實作是否太陽春？有沒有考慮用 `System.Management.Automation.WildcardPattern`？（`System.Management` 不在 Unity runtime dependency，答案是不行）
6. **版本 bump 策略**：新增一支 tool 算 minor（1.11.x → 1.12.0）還是 patch？目前用 minor。
7. **IsAsync 轉換安全**：`ScreenshotGameViewTool` 從 sync 改 async，既有 batch_execute 或其他 caller 有沒有依賴 sync 行為？（我確認過 `McpUnitySocketHandler` 會根據 `IsAsync` 走不同 code path，應該安全，但值得第二雙眼）

---

## 6. 文檔一致性檢查

| 項目 | 狀態 |
|------|------|
| `CHANGELOG.md` `[1.12.0]` 區塊 | ✅ Added / Changed / Documentation 三節，對應 P0/P2/P4 |
| `README.md` tool list | ✅ 新增 `get_gameobjects_by_name`、`screenshot_game_view` 補 `force_focus` 說明、`loc_get_entries` 補 `include_values` 說明 |
| `README-ja.md` / `README_zh-CN.md` | ⚠️ **未更新** — 這兩份自 v1.7.0 起就沒再跟 UPM 版本同步（查 `git log`），跟慣例一致，但嚴格講是文檔債 |
| `CLAUDE.md` 的「Adding a New Built-in Tool」章節 | ✅ 不需要改（本 PR 有遵守該章節定義的 pattern） |
| `package.json` 版本 | ✅ `1.11.2` → `1.12.0` |
| `server.json`（MCP registry 版本） | ⚠️ **未更新**（`1.2.1`）— 這份歷史慣例沒跟 UPM 版本同步，我沒動。請 reviewer 判斷是否需要 |
| `Server~/package.json` | ⚠️ **未更新**（`1.0.0`）— 同上 |
| `llms.txt` | ✅ 該檔只列精選工具，`get_gameobjects_by_name` 不屬於精選層級，未加入 |
| `doc/lessons/unity-mcp-lessons.md` | N/A — 本次沒有新 MCP 使用教訓需記錄 |

### 需求文件一致性

本 PR 沒有對應的 `doc/requirement/` 需求文件（是由外部 feedback 驅動的小改動），不存在偏離原始設計的風險。

---

## 7. 檔案變更清單

```
M  CHANGELOG.md                            (+18)
M  Editor/Tools/ScreenshotTools.cs         (+67 / −44)  sync → async
A  Editor/Tools/GetGameObjectsByNameTool.cs  (+140)     new tool
M  Editor/UnityBridge/McpUnityServer.cs    (+4)         register new tool
M  README.md                               (+6 / −3)
M  Server~/src/index.ts                    (+2 / −1)
M  Server~/src/tools/getGameObjectTool.ts  (+76)        new TS wrapper
M  Server~/src/tools/localizationTools.ts  (+14 / −3)   include_values
M  Server~/src/tools/screenshotTools.ts    (+7 / −1)    force_focus schema
M  package.json                            (1.11.2 → 1.12.0)
```

---

## 8. 未 commit

目前所有改動在 working tree，尚未 commit。等 review 通過後再建 commit。

**請 reviewer 聚焦在 §5 的七個審查重點**，尤其 F1 / F7 / F11 / F13 這四個 functional risk。
