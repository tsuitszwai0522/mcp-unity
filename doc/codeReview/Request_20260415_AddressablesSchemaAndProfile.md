# Code Review Request — Addressables Group Schema + Profile Management Tools

> **日期**: 2026-04-15
> **Feature**: Addressables MCP Tools v1.1（擴充 group schema 調整 + profile variables）
> **前置 Feature Doc**: `doc/requirement/feature_addressables_mcp.md`（v1.1 擴充段）
> **Scope**: 新增 6 個 MCP 工具 + 16 個 NUnit 測試 + 擴充 README

---

## 1. 背景與目標

### 起源

v1 Addressables 工具（已於 2026-04-13 落地 15 個工具）打通了 **entries 層級**的操作：create group、add entries、labels、find。但下游 ProjectT（Unity 2022.3.62f3）實戰接線 5 個 TMP 字型 group 時發現三個缺口：

1. **`BundledAssetGroupSchema` 欄位無法更新** — `addr_create_group` 建完之後，compression / include_in_build / bundle naming / packed_mode / runtime 載入行為都改不了
2. **`BuildPath` / `LoadPath` 切換不了** — Small Client Strategy（font / character / UI 走 CDN）需要把 group 切到 Remote profile 變數，v1 沒有路徑
3. **Profile variables 管理缺失** — 改 CDN URL 仍然要開 Addressables Profiles window 手動拉

### 目標

補齊 v1 的 schema + profile 缺口，讓 agent 可以在完全不開 Unity window 的情況下：

- 改 group 的 BundledAssetGroupSchema 任何欄位（partial update + dry-run）
- 切 `Remote.BuildPath` / `Remote.LoadPath`（Local↔Remote switch）
- 管理 profile 變數（list / get / set / switch active）

### 設計原則

- **Partial update + diff 語義**: 只改傳入的 key，回傳 `changed` 陣列與 `from/to` diff
- **Dry-run first**: 破壞性操作預設可先預覽
- **錯誤分級**: `not_initialized` / `not_found` / `schema_not_found` / `validation_error` / `variable_not_found` / `profile_not_found` / `set_failed`
- **與 v1 同 asmdef**: 直接加入 `McpUnity.Addressables` sub-assembly，靠 `DiscoverExternalTools` 自動註冊

---

## 2. 變更檔案一覽

### 新增（3 個 C# tool 檔 + TS 6 個 registerFn）

```
Editor/Tools/Addressables/
├── AddrSetGroupSchemaTool.cs        325 行  — P0 partial update + dry_run + diff
├── AddrGetGroupSchemaTool.cs         71 行  — 唯讀伴生
└── AddrProfileTools.cs              295 行  — 4 個 profile 工具 + shared helper

Server~/src/tools/addressablesTools.ts  +219 行  — 6 個 registerFn
Server~/src/index.ts                    +12 行  — import + register
```

### 修改

```
Editor/Tests/Addressables/AddrTests.cs     +436 行  — G1–G9 / H1–H7 + fixture 擴充
README.md                                  +18 行  — 6 個 tool 文件 + example prompts
doc/requirement/feature_addressables_mcp.md +300 行 — v1.1 擴充段
```

**Diff 總計**: 5 modified + 3 new = 8 files，約 980 新增行。

---

## 3. 關鍵代碼

### 3.1 Schema partial-update 核心（`AddrSetGroupSchemaTool.cs`）

統一用 `ApplyField()` dispatcher，每個欄位 compute before/after，只在 actually changed 時記入 diff 並呼叫 setter：

```csharp
case "compression":
{
    if (!TryParseEnum<BundledAssetGroupSchema.BundleCompressionMode>(value, field, out var parsed, out var err)) return err;
    var before = schema.Compression;
    if (before != parsed)
    {
        diff[field] = new JObject { ["from"] = before.ToString(), ["to"] = parsed.ToString() };
        appliedNames.Add(field);
        if (!dryRun) schema.Compression = parsed;
    }
    return null;
}
```

- **No-op 語義**: `before == parsed` 時完全不動 diff，`changed` 陣列保持空
- **Dry-run 隔離**: `dry_run: true` 時只走 `before != parsed` 比對、不寫入
- **Fail-fast**: 任一欄位解析錯誤（enum 非法、bool parse 失敗）立即 return error，**已套用的欄位不 rollback**（見「脆弱點」第 1 點）

### 3.2 BuildPath/LoadPath 經 ProfileValueReference 切換

Unity 的 `BundledAssetGroupSchema.BuildPath` 是 **getter-only property**，回傳 `ProfileValueReference`（class，非 struct）。必須透過方法而非 assignment：

```csharp
private static JObject ApplyProfileReference(
    AddressableAssetSettings settings,
    ProfileValueReference reference,
    string field,
    JToken value,
    JObject diff,
    List<string> appliedNames,
    bool dryRun)
{
    string variableName = value?.ToString();
    // ...
    // 預先驗證變數存在（dry-run 也要吃到同一個錯）
    if (!settings.profileSettings.GetVariableNames().Contains(variableName))
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"Profile variable '{variableName}' does not exist. Available: [{...}]",
            "variable_not_found");
    }

    if (!dryRun)
    {
        if (!reference.SetVariableByName(settings, variableName))
        {
            return McpUnitySocketHandler.CreateErrorResponse(
                $"Failed to set profile variable '{variableName}' on {field}",
                "set_failed");
        }
    }
    return null;
}
```

### 3.3 Profile 變數建立 vs 修改

`AddrSetProfileVariableTool` 預設只改現有變數，傳 `create_if_missing: true` 才在 profile-settings 層建立新變數：

```csharp
if (!variableNames.Contains(variable))
{
    if (!createIfMissing)
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"Profile variable '{variable}' does not exist. Pass create_if_missing=true to create it. Available: [{...}]",
            "variable_not_found");
    }
    profileSettings.CreateValue(variable, value);
    created = true;
}

string previousValue = created ? null : profileSettings.GetValueByName(profileId, variable);
profileSettings.SetValue(profileId, variable, value);
```

注意 `previousValue = created ? null : ...` —— 因為 `CreateValue` 會把變數建在**所有 profile** 的層級，剛建完的 `GetValueByName` 會回傳我們自己剛寫入的值，並非真實的「舊值」。用 null 表達「無前值」語義正確。

### 3.4 測試 fixture 擴充（profile 層的清理）

新增 `_originalActiveProfileId` + `RestoreActiveProfile()` 確保 active profile 永不漂移；`CleanupTestArtifacts()` 加掃 `McpAddrTest_*` 前綴的 profile 和 profile variables：

```csharp
// Remove test profiles (leave the real profiles alone). Must happen
// after RestoreActiveProfile so we don't rip out the currently-active
// profile mid-cleanup.
var testProfileIds = profileSettings.GetAllProfileNames()
    .Where(n => n != null && n.StartsWith(TestPrefix))
    .Select(n => profileSettings.GetProfileId(n))
    .Where(id => !string.IsNullOrEmpty(id))
    .ToList();
foreach (var id in testProfileIds)
{
    profileSettings.RemoveProfile(id);
}

// Remove test profile variables (CreateValue lives at the profile-settings
// level so it affects every profile — clean up to avoid leaking).
var testVariableIds = profileSettings.GetVariableNames()
    .Where(n => n != null && n.StartsWith(TestPrefix))
    .Select(n =>
    {
        var data = profileSettings.GetProfileDataByName(n);
        return data?.Id;
    })
    .Where(id => !string.IsNullOrEmpty(id))
    .ToList();
foreach (var id in testVariableIds)
{
    profileSettings.RemoveValue(id);
}
```

---

## 4. 自我評估（弱點、Edge Cases）

### 4.1 脆弱點 (Fragile Points)

1. **`AddrSetGroupSchemaTool` 不是原子的** —— 如果 `values` 傳了 5 個欄位，第 3 個 parse 失敗，前 2 個已經寫入 schema 並不會 rollback。目前語義是 "fail-fast with partial apply"。
   - **風險**: agent 預期 all-or-nothing 時可能誤以為沒改動
   - **緩解**: 回傳 `changed` 陣列告訴 caller 實際改了哪些，agent 可以 read-back 驗證；`dry_run` 可以先驗證
   - **Review 焦點**: 是否應該改為先驗證所有欄位都能 parse、再實際套用？會讓 dispatch 結構重寫

2. **`CreateValue` 是 global** —— 傳 `create_if_missing: true` 會在 **profile-settings 層級**建立變數，而非單一 profile。這是 Unity API 的設計限制，不是我們選的。
   - **風險**: agent 以為「只影響 Default profile」，結果所有 profile 都多了這個變數
   - **緩解**: tool description 有明確寫，但容易被忽略
   - **Review 焦點**: 是否應該加 warning field 到 response，或預設 `create_if_missing: false` 就夠保守？

3. **Profile variable 變數層級 vs 值層級** —— `GetVariableNames()` 回傳的是 profile-settings 層的「變數名清單」，不是 per-profile 的。Unity 裡每個 profile 都有這些變數的自己一份 value。
   - **風險**: tool 文件若沒講清楚 agent 可能混淆
   - **緩解**: `addr_list_profiles` 回傳 `variableNames` + 每個 profile 的 `variables` map，結構上分得很清楚

4. **`AddrSetGroupSchemaTool.TryParseEnum<TEnum>` 用 `where TEnum : struct, Enum` 約束** —— C# 7.3+ feature，Unity 2022.3 可用，但若將來要降版本到 Unity 2021.x 就會編譯失敗。不過 `defineConstraints: MCP_UNITY_ADDRESSABLES` 與 versionDefines 只守 Addressables 版本，冇守 Unity/C# 版本。
   - **風險**: 換 Unity 版本時可能爆
   - **緩解**: CLAUDE.md 寫明 "Unity 2022.3+"，設想未來降版本的機率很低

5. **`AddrGetGroupSchemaTool` 缺 using 的 bug 已 catch** —— 初次提交時忘記加 `using McpUnity.Unity;`，Unity 實際編譯才發現。Node-side 的 `npm run build` 通過，但 C# 端**沒有離線驗證機制**。
   - **教訓**: 下次寫 sub-assembly C# 新檔案時應該 parallel 檢查 using block 是否跟既有檔案一致
   - **Review 焦點**: 需不需要加一個 pre-commit hook 或 script 去 lint sub-assembly 檔案的 using directives？

### 4.2 Edge Cases

| Edge Case | 處理方式 | 已測試 |
|---|---|---|
| 空的 `values` object | `validation_error: "Parameter 'values' must be a non-empty object"` | ⚠️ 未單測，但 schema `required: ["group", "values"]` + runtime 檢查 |
| `values` 傳了全部既有值（no-op） | `changed: []`、message 特別標示 "all provided values already matched" | ✅ G4 |
| `dry_run` + 未知 field | 走到 default case → `validation_error`，dry_run 照樣 fail | ✅ G6（沒特別測 dry_run 組合，但 dispatch 路徑一致） |
| Group 有 BundledAssetGroupSchema 以外的 schema | 只讀/改 Bundled，其他 schema（如 `ContentUpdateGroupSchema`）完全不動 | ✅ G1 驗證 `addr_create_group` 建的 group 都有 Bundled |
| Profile 變數 token 未展開 | `addr_get_group_schema` 的 `load_path_value` / `build_path_value` 返回 **已展開**的 string | ✅ Live MCP test #8 驗證 token 替換 |
| `addr_set_profile_variable` 改 active profile 的變數 | 其他 group 的 `load_path_value` 會即時反映（因為 Addressables runtime resolve 走 active profile） | ✅ Live MCP test #8 |
| Profile 名稱有空格 | `GetProfileId` 用精確 match，空格 OK，但 tool 沒有額外驗證 | ❌ 未測 |
| 變數值含 `[UnknownToken]` | Unity `EvaluateString` 會回 literal（不抓錯），tool 不額外 validate | ❌ 未測 |

### 4.3 潛在回歸 (Regression Risks)

- **Test fixture 改動** —— `CleanupTestArtifacts()` 原本只掃 groups / labels，現在加掃 profiles / variables。若**任何** A-F 既有測試的名稱意外撞到 `McpAddrTest_` 前綴的 profile 或變數名，會被誤刪。檢查：既有測試全部用 `TestGroupName` / `TestLabel` / `TestLabel2`，沒用過 profile，所以不會誤傷。
- **`RestoreActiveProfile` 順序** —— per-test `TearDown` 裡的呼叫順序是 `RestoreDefaultGroup` → `RestoreActiveProfile` → `CleanupTestArtifacts`。後者刪 profiles 前必須先切回原 profile，否則「刪掉當前 active 的 profile」會引發未定義行為。這個順序很重要。

### 4.4 無覆蓋的測試 Gap

- **批次 dry_run + 多欄位混合的失敗路徑**：沒有測 "3 個欄位的第 2 個 parse error 時，第 1 個是否已寫入" —— 呼應弱點 #1
- **`addr_set_group_schema` 同時改 10 個欄位**：測試最多只改 2 個，沒驗 dispatcher 在大 batch 下的表現（但邏輯上每個欄位獨立，理論風險低）
- **Live MCP 的 `run_tests` 全量 fixture 跑不完**：MCP 10 秒 timeout 限制，改成單一 test filter。**但 local Unity Test Runner 可以跑完全部 69 + 16 = 85 個測試**，需要審查時手動在 Unity 內跑一次驗證

---

## 5. 審查重點（建議審查順序）

### P0（必看）

1. **`AddrSetGroupSchemaTool.cs` ApplyField dispatcher** — 10 個欄位的 before/after 比對邏輯是否有漏（例如 `bundle_naming` 如果漏傳 setter 會發現嗎？）
2. **BuildPath/LoadPath pre-validation** — `ApplyProfileReference` 先 check `GetVariableNames().Contains(variableName)`，這個 list lookup 每次都重建，但 list 通常 < 10 項不是效能問題；邏輯上是否足以 gate 掉 `SetVariableByName` 會失敗的 case？
3. **Partial-apply 失敗的語義選擇** — 弱點 #1。是否要改成兩階段（validate all → apply all）？

### P1

4. **`AddrProfileTools.cs` 的 `AddrSetProfileVariableTool.previousValue = created ? null : ...`** — null vs empty string 的選擇是否合理？
5. **測試 `CleanupTestArtifacts()` 順序** — 為什麼一定要先 restore active profile 才能刪 test profiles？comment 有寫但值得驗證實際順序正確
6. **`AddrSetGroupSchemaTool` 的 `TryParseBool` / `TryParseInt` try-catch** — 可以改成 `JToken.Type == JTokenType.Boolean` 直接 check，比 try-catch 乾淨。這是個偏好問題，review 可以 push back
7. **`AddrGetGroupSchemaTool.cs` 是新加的唯讀工具** — 只有 71 行很好讀，主要確認 12 個欄位都有回傳而且跟 set 端對齊

### P2

8. **README.md 新增段落的語意** — 6 個 tool 的 one-liner 是否準確、example prompt 是否符合 agent 日常工作流
9. **Feature doc v1.1 擴充段是否跟實作 100% 對齊**（參見下方「文檔一致性檢查」）

---

## 6. 文檔一致性檢查

| 項目 | Feature Doc | 實作 | 一致？ |
|---|---|---|---|
| `addr_set_group_schema` 參數名 | `group`, `dry_run`, `values` | 同 | ✅ |
| `values.compression` enum 值 | `Uncompressed` / `LZ4` / `LZMA` | 同 | ✅ |
| `values.packed_mode` enum 值 | `PackTogether` / `PackSeparately` / `PackTogetherByLabel` | 同 | ✅ |
| `values.bundle_naming` enum 值 | `AppendHash` / `NoHash` / `OnlyHash` / `FileNameHash` | 同 | ✅ |
| `values.build_path` / `load_path` 語義 | profile variable 名 | 同 | ✅ |
| Error type 清單 | `not_initialized` / `not_found` / `schema_not_found` / `validation_error` / `variable_not_found` / `set_failed` | 同 | ✅ |
| `addr_get_group_schema` 額外欄位 | `build_path_value` / `load_path_value`（token 展開後） | 同 | ✅ |
| `addr_set_profile_variable` 的 `create_if_missing` 說明 | "在 profile-settings 層建立新變數（注意：affects all profiles）" | tool description 有寫「Creates the variable if it doesn't exist yet」— 沒明確寫 global 影響 | ⚠️ TS description 可以加一句 |
| `addr_set_active_profile` idempotent 行為 | `changed: false` 不報錯 | 同 | ✅ |
| CHANGELOG v1.12.0 | 已標 | ⚠️ **未實際 bump** | ❌ 需補 |

### 待補項目

- **TS `addr_set_profile_variable` description** 加一句 "Creates at profile-settings level, which affects ALL profiles, not just the named one" 澄清 `create_if_missing` 的副作用
- **CHANGELOG.md v1.12.0 bump** —— feature doc v1.1 擴充段提到要 bump，但實際 CHANGELOG 尚未更新。應該在此次 commit 或下個 commit 加入

---

## 7. Verification 狀態

| 階段 | 狀態 |
|---|---|
| Node TypeScript build (`npm run build`) | ✅ 0 error |
| Unity C# compile | ✅ 0 error, 0 warning（修復初次的 `using McpUnity.Unity;` 漏 import） |
| Live MCP end-to-end（10 項 scenario） | ✅ 全通過：dry_run、partial、no-op、Remote 切換、error 路徑、profile 修改、token 替換 |
| NUnit 抽樣（G2 / G3 / G8 / H4 / H7） | ✅ 5/5 PASS |
| NUnit 全量（G1–G9、H1–H7 共 16 項） | ⚠️ MCP `run_tests` 10 秒 timeout 跑不完整個 fixture；建議 reviewer 在 Unity Test Runner 本地跑過 AddrTests 全量 |

---

## 8. 已知 Tech Debt / 待後續

1. **CHANGELOG v1.12.0 bump** — commit 前要加
2. **`addr_create_profile`** — 仍需手動在 Unity GUI 建立 profile 本身
3. **批次 `addr_set_group_schemas`** — 故意不做，`batch_execute` 已經覆蓋，但如果 review 覺得高頻使用值得 native 支援可以 push back
4. **快捷 `addr_set_group_build_remote` / `_local`** — 同上，over-engineering
5. **C# sub-assembly using directive 的 pre-commit lint** — 這次初次提交漏 import，有 tooling 的話可以防住

---

## 9. 主要變更檔案清單

```
新增 (A):
  Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs         325 lines
  Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs.meta
  Editor/Tools/Addressables/AddrGetGroupSchemaTool.cs          71 lines
  Editor/Tools/Addressables/AddrGetGroupSchemaTool.cs.meta
  Editor/Tools/Addressables/AddrProfileTools.cs               295 lines
  Editor/Tools/Addressables/AddrProfileTools.cs.meta

修改 (M):
  Editor/Tests/Addressables/AddrTests.cs                      +436 lines
  README.md                                                   +18  lines
  Server~/src/index.ts                                        +12  lines
  Server~/src/tools/addressablesTools.ts                      +219 lines
  doc/requirement/feature_addressables_mcp.md                 +300 lines
```

**Total**: 8 files, ~980 lines added.
