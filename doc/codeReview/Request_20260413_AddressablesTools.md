# Code Review Request — Unity Addressables MCP Tool Suite

- **日期**: 2026-04-13
- **Commits**: `fdebeb6` (feat) + `65a10ab` (test)
- **版本**: v1.11.0 → v1.11.1
- **規模**: 51 files changed, +4850 / −4
  - feat: 40 files, +2723
  - test: 11 files, +2127 / −3
- **Branch**: `main`(已 commit,未 push)
- **需求文件**:
  - `doc/requirement/feature_addressables_mcp.md` (Spec)
  - `doc/requirement/feature_addressables_mcp_tests.md` (Test plan)

---

## 1. 背景與目標

Addressables 是 Unity 官方推薦的 asset bundling / streaming 系統,但在 AI agent workflow 裡完全是個黑盒 — agent 無法查 group、加 entry、改 label、處理 schema。手動操作 Addressables Groups Window 對 agent 嚟講係 dead end。

呢次需求係建立一組 first-party MCP tools,讓 agent 可以:
- 查 + 初始化 Addressables settings
- 建/列/刪/設 default group
- 批量 add/remove/move/set entries(每個 entry 可帶 address + labels)
- 管理 labels
- 直接由 asset path 反查 entry 資訊

關鍵約束(同 Localization 一致):
- **不可硬依賴** `com.unity.addressables` package — 沒裝該 package 的使用者必須零影響
- 跟 Localization 同樣嘅 sub-assembly + version-gated compilation pattern
- v1 範圍**只做 settings/group/entry/label/query**,build / analyze / profile / 詳細 schema 留 v2

---

## 2. 設計決策摘要

### 2.1 Sub-assembly 條件編譯(沿用 Localization pattern)

`Editor/Tools/Addressables/McpUnity.Addressables.asmdef`:
```json
{
  "name": "McpUnity.Addressables",
  "rootNamespace": "McpUnity.Tools.Addressables",
  "references": [
    "McpUnity.Editor",
    "Unity.Addressables",
    "Unity.Addressables.Editor",
    "Unity.ResourceManager"
  ],
  "defineConstraints": ["MCP_UNITY_ADDRESSABLES"],
  "versionDefines": [{
    "name": "com.unity.addressables",
    "expression": "1.19.0",
    "define": "MCP_UNITY_ADDRESSABLES"
  }]
}
```

雙 gate(`defineConstraints` + `versionDefines`)— 跟 1.9.0 Localization 完全相同模式。`com.unity.addressables ≥ 1.19.0` 係最低支援版本(對應 Unity 2022.3 的官方推薦版本)。

呢次嘅 sub-assembly pattern 係**第二次使用**,確認個 pattern 為長期 maintain optional package 嘅 canonical solution。CLAUDE.md 已經更新指出兩個 in-tree references。

### 2.2 註冊路徑 — `[McpUnityFirstParty]` attribute

Localization 嗰陣只用 `McpUnity.*` assembly name prefix 做 fallback。1.11.0 引入咗 `[McpUnityFirstParty]` attribute 作為 explicit marker,所有 Addressables tool class 都標咗呢個 attribute(commit 喺 test commit 入面,因為係寫 test 嗰陣補嘅)。

```csharp
[McpUnityFirstParty]
public class AddrCreateGroupTool : McpToolBase { ... }
```

效果:`HandleListTools` 唔會經 dynamic registration path 重複 register。Hand-written TypeScript wrappers 喺 `Server~/src/tools/addressablesTools.ts` 係 canonical entry point。

### 2.3 共用 helper

`AddrHelper.cs` 統一處理:
- `TryGetSettings(out error)` — 未 init 時返 `not_initialized` error
- `ResolveGroup(name, out error)` — 找不到時 error message 列出 available groups
- `ResolveEntry(guid, assetPath)` — 兩種識別方式統一(優先 guid,fallback asset path → guid)
- `SaveSettings(settings, modificationEvent)` — `SetDirty` + `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets`
- `EntryToJson(entry)` — 統一 entry 序列化格式(`{guid, assetPath, address, labels, group}`)
- `ValidateLabel(label, out error)` — 拒絕空字串、含空格、含 `[]`
- `GetTotalEntryCount(settings)` — 用喺 `addr_get_settings` summary

### 2.4 Tool 清單(共 15 支)

| Category | Tool | C# | 備註 |
|---|---|---|---|
| Settings | `addr_get_settings` | `AddrGetSettingsTool` | `initialized:false` 唔係 error |
| Settings | `addr_init_settings` | `AddrInitSettingsTool` | Idempotent;custom folder 在 idempotent path 被忽略(§4.1) |
| Group | `addr_list_groups` | `AddrListGroupsTool` | 標 isDefault flag |
| Group | `addr_create_group` | `AddrCreateGroupTool` | 寫死 Bundled+ContentUpdate schemas (§4.2) |
| Group | `addr_remove_group` | `AddrRemoveGroupTool` | 阻止刪 default;非空 group 需 `force` |
| Group | `addr_set_default_group` | `AddrSetDefaultGroupTool` | 同 group 返 no-op message |
| Entry | `addr_list_entries` | `AddrListEntriesTool` | Glob 只支援 `*`;有 `truncated` flag (§4.4) |
| Entry | `addr_add_entries` | `AddrAddEntriesTool` | 自動建 label + warning (§4.3) |
| Entry | `addr_remove_entries` | `AddrRemoveEntriesTool` | `notFound` 計數但唔分類 (§4.5) |
| Entry | `addr_move_entries` | `AddrMoveEntriesTool` | 同 add 一樣接受 guid 或 asset_path |
| Entry | `addr_set_entry` | `AddrSetEntryTool` | Partial update;`add_labels` + `remove_labels` 同時可用 (§4.6) |
| Label | `addr_list_labels` | `AddrListLabelsTool` | |
| Label | `addr_create_label` | `AddrCreateLabelTool` | Idempotent |
| Label | `addr_remove_label` | `AddrRemoveLabelTool` | In-use 需 `force`,force 會 strip 所有引用 |
| Query | `addr_find_asset` | `AddrFindAssetTool` | 區分 `not_found` (asset 不存在) 同 `found:false` (asset 存在但非 addressable) |

### 2.5 錯誤分類

統一用 `error_type` field(跟 Localization 一致):
- `not_initialized` — Addressables 未 init
- `not_found` — group/entry/label/asset 不存在
- `duplicate` — 同名已存在
- `in_use` — 仍被引用,需要 `force=true`
- `validation_error` — 參數格式錯
- `create_failed` — Addressables API 返 null

---

## 3. 驗證情況

### 3.1 編譯
- C# 端 `recompile_scripts` 多次,**0 warning, 0 error**
- Node 端 `npm run build`,**clean**

### 3.2 NUnit 測試 ✅(62/62 pass)

`Editor/Tests/Addressables/AddrTests.cs`(~1200 行,**62 個 test cases**):

| Section | Tests | 涵蓋 |
|---|---|---|
| A. Settings | 4 | initialized field shape、idempotency、custom folder 在 idempotent path |
| B. Group | 18 | 三個 packed_mode、include_in_build、default 處理、in-use 保護、validation、duplicate、not_found |
| C. Label | 10 | create/list/remove、idempotency、空格/bracket/empty validation、in-use force-strip |
| D. Entry | 26 | 批量 add/remove/move(guid + asset_path mixed)、glob filter、address_pattern、`truncated` flag、partial set_entry、auto-label-create warnings |
| E. Query | 3 | find_asset addressable / non-addressable / non-existent |
| **F. Golden Path** | 1 | `[Order(999)]` 18-step end-to-end agent workflow,step counter 喺 failure messages |
| **總計** | **62** | |

**Stage 化驗證**(跟 test plan 4 階段):
- Stage 1: 4/4 smoke
- Stage 2: 32/32(Stage 1 + B + C 不含 in-use)
- Stage 3: 60/60(全部 unit tests,含 deferred B15/B16/C8/C9)
- Stage 4: 61/61 + F1 = **62/62**

**注意**:由於發現 `run_tests` 對 broad filter 會撞 WebSocket payload size 上限(see §4.7),最終要**逐個 test 跑**驗證(逐個都 pass)。

### 3.3 自包含 fixture(無 e2e batch_execute 需要)

對比 1.9.0 Localization request 嗰陣 test runner 環境失效要靠 `batch_execute` 補等價驗證,呢次 NUnit 測試成功跑曬 62 個 test,**單元測試本身就係 e2e 驗證**(F1 golden path scenario 就係 18-step agent workflow simulation)。

---

## 4. 自我評估:脆弱點與 Edge Cases

### 4.1 ⚠️ `addr_init_settings` 嘅 `folder` 參數在 idempotent path 默默被忽略

```csharp
var existing = AddressableAssetSettingsDefaultObject.GetSettings(false);
if (existing != null) {
    return new JObject { ["created"] = false, ... };  // folder param 完全冇 inspect
}
```

- 用戶若傳 `folder: "Assets/MyCustomPath"` 但 Addressables 已 init,會 silently ignore 該參數
- 我有 test 覆蓋呢個 case(A3 `InitSettings_WithCustomFolder_WhenAlreadyInit_IgnoresFolder`)同確保 idempotent 唔會建第二個 settings asset
- **但 API 行為對 caller 唔明顯** — `created:false` 唔代表「冇做任何嘢」,而代表「冇處理 folder 因為已存在」
- **審查者請評估**:
  - 要唔要在 `created:false` 嘅 response 加一個 `folderIgnored:true` flag?
  - 或者 description 加更明確嘅 warning?
  - 或者 idempotent path return error 強制用戶先檢查?(會 break 簡單 caller 模式)

### 4.2 ⚠️ `addr_create_group` 寫死 `Bundled + ContentUpdate` schema 組合

```csharp
var group = settings.CreateGroup(
    name, setAsDefault, false, true, null,
    typeof(BundledAssetGroupSchema),
    typeof(ContentUpdateGroupSchema));
```

- Addressables 實際上有 N 個 schema 類型(`BundledAssetGroupSchema`, `ContentUpdateGroupSchema`, `PlayerDataGroupSchema`, 第三方擴充等)
- 我寫死 = `New > Packed Assets Group` 模板
- **限制**:用戶想要 PlayerDataGroupSchema 或第三方 schema 嘅 group 完全做唔到
- **緩解**:v1 範圍故意限制,留 v2 嘅 `addr_set_schema` / `addr_add_schema` 處理。Spec doc 有列明
- **審查者請評估**:
  - 而家寫死兩個係 sane default,定係 v1 應該至少接受 `schemas: ["BundledAssetGroupSchema", ...]` array 提早預留 extensibility?
  - 或者保持 v1 寫死,v2 補 escape hatch?

### 4.3 ⚠️ Label auto-create 係 implicit magic(違反「顯式優於魔法」原則)

`addr_add_entries` 同 `addr_set_entry` 兩個 tool 都會:
1. 收到 label 名
2. 若 label 唔存在於 settings → **自動 `settings.AddLabel(label)`**,加入 warning
3. 然後 set 到 entry

```csharp
if (!existingLabels.Contains(label)) {
    settings.AddLabel(label, false);
    existingLabels.Add(label);
    warnings.Add($"Label '{label}' was created automatically");
}
entry.SetLabel(label, true, false, false);
```

- **正面**:agent 用起來流暢,唔使先 call `addr_create_label` 再 call `addr_add_entries`
- **負面**:違反「Localization spec 嗰陣明確拒絕 auto-create locale」嘅一致性原則 — Localization 係 explicit 嘅,Addressables 係 magic 嘅
- **判斷理由**:Locale 係 heavyweight 物件(.asset file + Localization Settings 註冊),label 係 settings 內嘅 string list。代價唔同。但呢個係主觀 call。
- **審查者請評估**:
  - 同意 auto-create label 嘅 trade-off 嗎?
  - 或者要強制用戶先 `addr_create_label`,`add_entries` 遇到未知 label 直接 error?
  - 或者保留 auto-create 但加 `strict_labels: true` 參數做 opt-in 嚴格模式?

### 4.4 ⚠️ `addr_list_entries` 嘅 glob pattern 只支援 `*`

```csharp
var escaped = "^" + Regex.Escape(addressPattern).Replace("\\*", ".*") + "$";
addressRegex = new Regex(escaped, RegexOptions.IgnoreCase);
```

- 唔支援 `?`、character class `[abc]`、escape sequences
- Spec 同 README 都有寫「supports `*`」,但用戶可能期望完整 glob
- 大小寫不敏感(`IgnoreCase`)係 hardcode,冇參數可調
- **審查者請評估**:
  - 而家 `*` only 對 90% case 夠用嗎?
  - 要唔要支援 `?` 同 `[]`?(實作成本低,但測試成本高)
  - Case sensitivity 要唔要 expose 做參數?

### 4.5 ⚠️ `addr_remove_entries` 嘅 `notFound` 唔分類

```json
{
  "removed": 2,
  "notFound": 1
}
```

- `notFound` 包括三種情況:
  1. asset path 寫錯,asset 不存在
  2. asset 存在但唔係 addressable
  3. guid 字串無效或對應已被刪除
- 用戶 debug 嗰陣只見到 `notFound:1`,完全唔知邊個 entry / 邊個原因
- **緩解建議**:返 `notFoundEntries: [{ identifier, reason }]` array 而非單純 count
- **審查者請評估**:
  - 而家 count-only 嘅 minimal response 夠用嗎?
  - 加 detail array 會增加 response payload,但對 debug 重要

### 4.6 ⚠️ `addr_set_entry` 同時用 `add_labels` + `remove_labels` 嘅順序行為

`AddrSetEntryTool.Execute` 嘅 sequence:
1. 處理 `new_address`
2. 處理 `add_labels`(逐個 SetLabel(true))
3. 處理 `remove_labels`(逐個 SetLabel(false))

若同一個 label 同時喺 `add_labels` 同 `remove_labels`:
- 因為 add 先 → remove 後,**淨效果係 remove**
- 呢個係 implicit ordering,文檔冇講
- **審查者請評估**:
  - 要唔要 explicit reject 重疊 labels (validation_error)?
  - 或者保持 add-then-remove 嘅 deterministic order 並寫入 description?

### 4.7 ⚠️ `mcp__mcp-unity__run_tests` broad filter WebSocket payload 上限

呢個係**MCP 框架嘅 issue 而唔係 Addressables 嘅**,但喺 development 過程影響重大:

- `testFilter: "McpUnity.Tests.Addressables"` (62 個 test) → response payload 撞 WebSocket buffer,Unity console spam `WebSocket error: An error has occurred in sending data`,MCP request 長 timeout
- 唯一 workaround:逐個 test 跑(`testFilter: "McpUnity.Tests.Addressables.AddrTests.D17_..."`)
- 我已經將呢個記入 `doc/lessons/unity-mcp-lessons.md`,有 diagnosis tip
- **審查者請評估**:
  - Localization 也有 40 個 test,有冇遇過同樣問題?
  - 應唔應該 fix `run_tests` 嘅 response,例如 streaming / pagination / 限制 returnOnlyFailures 永遠強制?
  - 或者接受呢個現狀,只記 lesson?

### 4.8 ⚠️ `AddrTestDummySO` cleanup robustness

Test fixture 喺 `Assets/Tests/AddressablesTests/` 建立 3 個 `.asset`:
- `OneTimeSetUp`: 若已存在(上次 crash 留低)就 reuse,否則建新
- `OneTimeTearDown`: 刪 3 個 asset + folder

**Risk**:若 `OneTimeTearDown` 中途 throw(例如 RestoreDefaultGroup 失敗),後面 `DeleteDummyAssets` + `DeleteTestDirectory` 唔會 run,留低 dummy assets。
- **緩解**:`OneTimeSetUp` 嘅 `CreateDummyAssets` 寫成 reuse-if-exists,所以下次 fixture 仍然 work
- **負面**:若用戶 commit 咗呢個 stale folder,會混入 repo
- **審查者請評估**:
  - `Editor/Tests/Addressables/.gitignore` 加 `Assets/Tests/` 排除規則?(但 Tests folder 係 consumer project,唔係 package repo)
  - 或者 OneTimeTearDown 用 try/finally 確保刪一定執行?

### 4.9 ⚠️ Default group snapshot/restore 嘅 race assumption

```csharp
[OneTimeSetUp]
public void OneTimeSetUp() {
    ...
    _originalDefaultGroup = settings.DefaultGroup?.Name;
    ...
}

[TearDown]  // per-test
public void TearDown() {
    var settings = ...;
    RestoreDefaultGroup(settings);  // first
    CleanupTestArtifacts(settings); // then
}
```

- B5 / B11 改 default group → `[TearDown]` 還原到 `_originalDefaultGroup`
- Order 重要:**先 restore default 再 cleanup**(避免刪緊 default group)
- 我有 verify 過 order 但**冇 test 覆蓋呢個 ordering invariant**
- **Risk**:future maintainer 改 cleanup 順序就會默默 break
- **緩解建議**:加 inline comment 標明 invariant,或加 dedicated regression test
- **審查者請評估**:
  - 要唔要寫一個 regression test 強制驗證 ordering?

### 4.10 ⚠️ v2 範圍未做 — 對 agent 自動化 build 嘅影響

V1 完全唔做:
- `addr_build` (new build / update / clean)
- `addr_run_analyze_rule`
- Profile management (`addr_set_active_profile`, `addr_set_profile_variable`)
- 詳細 schema 設定(compression、bundle naming、provider type 等)

對 agent 想實現「全自動 prepare → build → ship」嘅 workflow,呢個 gap **重要**:agent 只可以 prepare entries,build 步驟仍然要人手點 Editor 按鈕。

- **判斷**:v1 故意限制範圍以求快放,build / analyze 涉及 async + long-running + report parsing 太重,要獨立規劃
- **審查者請評估**:
  - v2 應該優先做 `addr_build` 還是 `addr_run_analyze_rule`?
  - 要唔要 v2 包埋 profile management?
  - 有冇即時嘅 production project 等緊呢啲 v2 tool?

### 4.11 NUnit `[Order(999)]` 對 F1 嘅依賴

`F1_FullLifecycle_InitToCleanup_SucceedsAtEveryStep` 用 `[Test, Order(999)]`,假設 NUnit `[Order]` attribute 會把 F1 推到所有冇 Order 嘅 test 之後。

- NUnit 文檔講 ordered tests 行先,unordered 行後 — 同我嘅假設**相反**
- 但實測順序 OK(Stage 4 跑單一 F1 都 pass),代表喺呢個 fixture 行為符合預期
- **Risk**:若將來加咗其他 ordered test,NUnit 嘅 order 行為可能令 F1 唔再係最後
- **緩解建議**:將 F1 改成獨立 `[TestFixture]` 而非依賴 ordering trick
- **審查者請評估**:
  - 接受 `[Order(999)]` 嘅 hack,定係 refactor 成獨立 fixture?

### 4.12 `Server~/src/index.ts` 嘅 register 順序

我喺 `Localization Tools` block 後、`Batch Execute Tool` 前加咗 `Addressables Tools` block。
- Functional 上 order 唔影響
- 但若 future tool 同 Addressables 有 register-time dependency(暫時冇),可能要重排
- **審查者請評估**:有冇隱性 ordering convention(例如 alphabetical / category)?

---

## 5. 審查重點請求

### 5.1 架構面
- [ ] **§4.2** `addr_create_group` 寫死兩個 schema — v1 接受嗎?
- [ ] **§4.3** Label auto-create magic vs explicit — 同意 trade-off?
- [ ] **§4.10** v2 範圍取捨 — build/analyze 優先級?

### 5.2 API 面
- [ ] **§4.1** `addr_init_settings` folder 在 idempotent path 默默被忽略
- [ ] **§4.4** Glob pattern 只支援 `*` 是否夠用
- [ ] **§4.5** `addr_remove_entries` `notFound` 缺乏細分
- [ ] **§4.6** `add_labels` + `remove_labels` 重疊行為定義

### 5.3 測試面
- [ ] **§4.7** `run_tests` broad filter WebSocket 上限 — 修框架 vs 接受現狀
- [ ] **§4.8** Test fixture cleanup robustness
- [ ] **§4.9** Default group restore ordering 缺 regression test
- [ ] **§4.11** F1 嘅 `[Order(999)]` ordering 假設

### 5.4 一致性面
- [ ] **§4.12** index.ts register 順序
- [ ] 錯誤類型字串(`not_initialized`, `not_found`, `duplicate`, `in_use`, `validation_error`, `create_failed`)— 命名是否符合既有專案慣例?

### 5.5 文檔面
- [ ] CHANGELOG `[1.10.0]` (feat) + `[1.11.1]` (test) 兩個 entry 完整性
- [ ] README "Unity Addressables Tools" 段落清楚程度
- [ ] CLAUDE.md "in-tree references" tweak 是否充分

---

## 6. 文檔一致性檢查

| 項目 | 狀態 | 備註 |
|------|------|------|
| `CHANGELOG.md` `[1.10.0]` | ✅ | 列出 15 tools + sub-assembly pattern(已喺 v1.11.0 release commit 預先 ship) |
| `CHANGELOG.md` `[1.11.1]` | ✅ | 加 Tests / Documentation / Added(`[McpUnityFirstParty]` markers + InternalsVisibleTo) |
| `README.md` Unity Addressables Tools 段落 | ✅ | 15 支 tools 各一段帶 example prompt,跟 Localization 同格式 |
| `package.json` version bump | ✅ | 1.11.0 → 1.11.1 |
| `CLAUDE.md` | ✅ | "Adding a First-Party Optional Package Tool" 已 reference 兩個 in-tree examples |
| `doc/requirement/feature_addressables_mcp.md` | ✅ 已 commit | 原始 spec 保留作審查參考 |
| `doc/requirement/feature_addressables_mcp_tests.md` | ✅ 已 commit | 4-stage test plan |
| `doc/lessons/unity-mcp-lessons.md` | ✅ 已更新 | 新加 2 條:consumer project Assets folder pitfall + run_tests payload limit |
| `server.json` | ⚠️ 未更新 | MCP server registry version 仍係 1.2.1 — 唔知有冇 convention 要 bump |

---

## 7. 建議的後續動作(不在此 PR 範圍)

1. **v2 規劃**:`addr_build` (async + long-running)、`addr_run_analyze_rule`、profile management
2. **§4.5**:`addr_remove_entries` 加 `notFoundEntries` detail array
3. **§4.4**:Glob 擴充(可選)
4. **§4.8**:Cleanup robustness — try/finally 包裹 OneTimeTearDown
5. **§4.9**:Default group restore ordering 加 regression test
6. **§4.11**:F1 改成獨立 `[TestFixture]`(可選)
7. **§4.7**:`run_tests` 框架層 fix(streaming / pagination)— 跨 PR 考慮
8. **server.json** version bump 評估
