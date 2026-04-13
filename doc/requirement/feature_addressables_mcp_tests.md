# Feature: Addressables MCP Tools — Test Plan

> **狀態**: 需求整理
> **對應**: `feature_addressables_mcp.md`
> **前置**: v1.10.0 Addressables tool suite 已實作、Unity Addressables 已安裝
> **建立日期**: 2026-04-13

---

## 目標

對 15 個 `addr_*` tool 建立完整 EditMode 測試覆蓋,達到:

1. **Happy path** — 每個 tool 正常調用返回預期結果
2. **Error path** — 所有 `error_type` 分類(`not_initialized` / `not_found` / `duplicate` / `in_use` / `validation_error`)都有對應測試。`not_initialized` 經 `AddrHelper.SettingsProvider` 注入點模擬,避免真係拆咗 consumer project 嘅 Addressables 設定。
3. **Integration** — 一個 ordered scenario 模擬 agent 實際用法,catch 跨 tool 的狀態 bug
4. **Regression lock** — 對未來 Addressables API 升級提供安全網

跟 Localization test suite(40 test cases)同樣 scale,目標 **~45 test cases**。

---

## 分階段實作

### Stage 1 — 前置基建 + Smoke Test
**目的**: 打通 pipeline,確保 attribute 標記、asmdef gate、`InternalsVisibleTo`、`testables` 配置全部正確,避免一氣呵成寫完先發現 compile 唔到。

**產出**:
- 15 個 `Addr*Tool.cs` 全部加 `[McpUnityFirstParty]`
- `Editor/Tests/Addressables/McpUnity.Addressables.Tests.asmdef`
- `Editor/Tests/Addressables/AddrTests.cs` — 只包含 fixture + **3 個 smoke test**(Settings 類,最簡單)
- `Editor/Tools/Addressables/AddrHelper.cs` 加 `[assembly: InternalsVisibleTo]`

**驗收**: `run_tests` 跑出 3/3 pass,代表 pipeline 通。

---

### Stage 2 — Group + Label 測試
**目的**: 覆蓋獨立嘅 CRUD(唔需要 dummy asset)。

**產出**:
- Group: 15 個 test(B1–B15)
- Label: 9 個 test(C1–C9)

**驗收**: `run_tests` 跑 27 test(3 + 15 + 9)全部 pass。

---

### Stage 3 — Entry 測試 + Query
**目的**: 覆蓋需要 dummy asset 嘅 tool。呢 stage 開始要 `[OneTimeSetUp]` 建 test asset,teardown 要 clean 曬。

**產出**:
- Entry: 22 個 test(D1–D22)
- Query: 3 個 test(E1–E3)
- Dummy asset 建立邏輯(`ScriptableObject.CreateInstance` 或最簡單 `Material` asset)

**驗收**: `run_tests` 跑 52 test 全部 pass。

---

### Stage 4 — Golden Path Scenario + Polish
**目的**: 一個 ordered integration test 模擬 agent 完整用法,找跨 tool bug。

**產出**:
- F1 full-lifecycle scenario test
- 任何 Stage 1–3 發現嘅 flaky test 修正
- `doc/lessons/unity-mcp-lessons.md` 追加 Addressables API gotchas(如發現)
- CHANGELOG 加 `### Tests` section(跟 1.11.0 localization entry 同格式)

**驗收**: 53 test 全部 pass,連續跑 3 次都穩定,teardown 乾淨。

---

## 前置設定細節(Stage 1)

### 1.1 標記所有 tool 為 first-party

跟 Localization 最新做法一致(`CLAUDE.md § Adding a First-Party Optional Package Tool`)。每個 `Addr*Tool.cs` 加:

```csharp
using McpUnity.Tools;

namespace McpUnity.Tools.Addressables
{
    [McpUnityFirstParty]
    public class AddrXxxTool : McpToolBase { ... }
}
```

**為何**: 避免 `McpUnitySocketHandler.HandleListTools` 走 dynamic `list_tools` 路徑把 Addressables tool 再 register 一次(造成 Node + Unity 兩邊 register 重複)。`McpUnity.*` assembly name prefix 係 fallback,attribute 係 explicit 首選。

### 1.2 Test 子 asmdef

`Editor/Tests/Addressables/McpUnity.Addressables.Tests.asmdef`:

```json
{
  "name": "McpUnity.Addressables.Tests",
  "rootNamespace": "McpUnity.Tests.Addressables",
  "references": [
    "McpUnity.Editor",
    "McpUnity.Addressables",
    "Unity.Addressables",
    "Unity.Addressables.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll",
    "Newtonsoft.Json.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "UNITY_INCLUDE_TESTS",
    "MCP_UNITY_ADDRESSABLES"
  ],
  "versionDefines": [
    {
      "name": "com.unity.addressables",
      "expression": "1.19.0",
      "define": "MCP_UNITY_ADDRESSABLES"
    }
  ]
}
```

**雙重 gate**(`defineConstraints` + `versionDefines`)確保只喺 Addressables 裝咗 + test 模式下先編譯。

### 1.3 `InternalsVisibleTo`

`AddrHelper.cs` 頂部加:
```csharp
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("McpUnity.Addressables.Tests")]
```

讓 test fixture 可以直接 call `AddrHelper.TryGetSettings` / `EntryToJson` 等 internal helpers 做 assertion。

### 1.4 Consumer `manifest.json`

跟 Localization 一樣(`doc/lessons/unity-mcp-lessons.md` 已記):
```json
{
  "testables": ["com.gamelovers.mcp-unity"]
}
```

冇呢個,`run_tests` 會返 0/0 即使 asmdef 編譯成功。

---

## Fixture 設計

### 命名 + 常數
```csharp
private const string TestPrefix = "McpAddrTest_";
private const string TestGroupName = TestPrefix + "Group";
private const string TestGroupName2 = TestPrefix + "Group2";
private const string TestLabel = TestPrefix + "label";
private const string TestLabel2 = TestPrefix + "label2";
private const string TestDir = "Assets/Tests/AddressablesTests";

// 狀態追蹤(fixture ownership)
private string _originalDefaultGroup;    // 還原 SetDefaultGroup 測試改動
private bool _ownsSettingsInit;          // 若 OneTimeSetUp 觸發咗 init,teardown 時決定要唔要清理(預設保留,因為刪 Addressables settings 副作用太大)
private List<string> _createdAssetPaths; // Stage 3+ dummy asset 清理
```

### `[OneTimeSetUp]`
1. 確保 `Assets/Tests` / `TestDir` 存在(用 `AssetDatabase.CreateFolder`,**唔用** `Directory.CreateDirectory` — 參考 `doc/lessons/unity-mcp-lessons.md` pitfall)
2. 若 `AddressableAssetSettingsDefaultObject.GetSettings(false) == null`,call `new AddrInitSettingsTool().Execute(...)`,set `_ownsSettingsInit = true`
3. 記錄 `_originalDefaultGroup = settings.DefaultGroup?.Name`
4. 預防性清理:若 project 殘留上次 test 失敗留低嘅 `McpAddrTest_*` group / label,逐個刪除
5. **Stage 3+**:建立 3 個 dummy asset(見 §Dummy Assets)

### `[OneTimeTearDown]`
按順序:
1. 移除所有 `McpAddrTest_*` group(force)
2. 移除所有 `McpAddrTest_*` label(force)
3. 還原 `settings.DefaultGroup` 到 `_originalDefaultGroup`(若有變)
4. 刪除 `_createdAssetPaths` 每個 dummy asset
5. 刪除 `TestDir`(若空)
6. `AssetDatabase.SaveAssets() + Refresh()`
7. **唔刪除** Addressables settings 本身(即使 `_ownsSettingsInit`),避免影響用戶 project

### `[SetUp]` / `[TearDown]`
- Per-test 保持 clean slate:如上一個 test 未清好 `TestGroupName`,呢度補一次。
- 避免 test 之間互相污染(特別 `Order`-sensitive 測試係危險源)。

### Assert helpers
**注意**: `McpUnitySocketHandler.CreateErrorResponse` 返 `{ error: { type, message } }` **冇 `success` field**,tool 成功時返 `{ success: true, ... }`。Helper 要雙向 check:

```csharp
private static void AssertSuccess(JObject r)
{
    Assert.IsNotNull(r, "Result is null");
    Assert.IsNull(r["error"], $"Unexpected error: {r["error"]}");
    Assert.IsTrue(r.Value<bool>("success"), $"success != true. Result: {r}");
}

private static void AssertError(JObject r, string expectedType = null)
{
    Assert.IsNotNull(r, "Result is null");
    Assert.IsNotNull(r["error"], $"Expected error, got: {r}");
    if (expectedType != null)
        Assert.AreEqual(expectedType, r["error"]?["type"]?.ToString(),
            $"Error type mismatch. Full result: {r}");
}
```

### Dummy Assets(Stage 3+)

用**最小 `ScriptableObject`** 類型,喺 test assembly 內定義:

```csharp
// AddrTests.cs 同一個 file 內
internal class AddrTestDummySO : ScriptableObject { }

private string CreateDummyAsset(string fileName)
{
    var so = ScriptableObject.CreateInstance<AddrTestDummySO>();
    string path = $"{TestDir}/{fileName}.asset";
    AssetDatabase.CreateAsset(so, path);
    _createdAssetPaths.Add(path);
    return path;
}
```

**為何唔用 Material**: Material 依賴 shader,shader 喺 test 環境可能唔齊,ScriptableObject 完全自足。

**為何唔用 `create_scriptable_object` MCP tool**: test 直接 call Unity API 更快、冇 round-trip、更可靠。

---

## 完整測試清單

### A. Settings(Stage 1 — smoke; 4 tests)

| # | Test 名 | 檢查點 |
|---|---|---|
| A1 | `GetSettings_WhenInitialized_ReturnsExpectedFields` | `initialized:true`, `defaultGroup` non-null, `activeProfile == "Default"`, `profileVariables` 含 `BuildPath`/`LoadPath`, `groupCount ≥ 1` |
| A2 | `InitSettings_WhenAlreadyInitialized_IsIdempotent` | `created:false`, `settingsPath` 非空,`defaultGroup` 非空 |
| A3 | `GetSettings_ReportsLabelsArray` | `labels` 係 JArray;若 test 先加咗 `TestLabel`,能喺 response 見到 |
| A4 | `InitSettings_WithCustomFolderParam_WhenAlreadyInit_IgnoresFolder` | 傳 `folder: "Assets/Nonexistent"` 都返 `created:false`(idempotent path 唔會踩到 folder 邏輯) |

> **不測嘅 path**: `initialized:false` — 需清除 `AddressableAssetSettingsDefaultObject.Settings`,副作用太大。用 code review 覆蓋即可。

---

### B. Group(Stage 2;15 tests)

| # | Test 名 | 檢查點 |
|---|---|---|
| B1 | `CreateGroup_CreatesWithDefaultSchemas` | `created:true`; `settings.FindGroup(name) != null`; `GetSchema<BundledAssetGroupSchema>() != null`; `GetSchema<ContentUpdateGroupSchema>() != null` |
| B2 | `CreateGroup_PackedModePackSeparately_AppliesToSchema` | `schema.BundleMode == BundlePackingMode.PackSeparately` |
| B3 | `CreateGroup_PackedModePackTogetherByLabel_AppliesToSchema` | 同上,不同 enum |
| B4 | `CreateGroup_IncludeInBuildFalse_AppliesToSchema` | `schema.IncludeInBuild == false` |
| B5 | `CreateGroup_SetAsDefaultTrue_ChangesDefault` | `settings.DefaultGroup.Name == TestGroupName`;**`[TearDown]` 必須還原** |
| B6 | `CreateGroup_Duplicate_ReturnsDuplicateError` | `error.type == "duplicate"` |
| B7 | `CreateGroup_InvalidPackedMode_ReturnsValidationError` | 傳 `packed_mode: "InvalidMode"`;`error.type == "validation_error"` |
| B8 | `CreateGroup_EmptyName_ReturnsValidationError` | 傳 `name: ""`;`error.type == "validation_error"` |
| B9 | `ListGroups_IncludesCreatedGroupWithCorrectSchema` | response.groups 含 TestGroupName,`schemas` array 含 `"BundledAssetGroupSchema"` |
| B10 | `ListGroups_MarksDefaultGroup` | 確認 `isDefault:true` flag 正確 |
| B11 | `SetDefaultGroup_ChangesDefaultAndReportsPrevious` | `defaultGroup == new`, `previousDefault == old`;**`[TearDown]` 必須還原** |
| B12 | `SetDefaultGroup_SameGroup_ReturnsNoOpMessage` | `message` 含 "already"(或同效) |
| B13 | `SetDefaultGroup_NonExistent_ReturnsNotFound` | `error.type == "not_found"` |
| B14 | `RemoveGroup_Empty_Succeeds` | `deleted:true`, `removedEntryCount:0`; `FindGroup` 返 null |
| B15 | `RemoveGroup_NonEmpty_WithoutForce_ReturnsInUse` | 依賴 Entry 測試已執行;`error.type == "in_use"` |
| B16 | `RemoveGroup_NonEmpty_WithForce_RemovesEntries` | `force:true`; `removedEntryCount > 0` |
| B17 | `RemoveGroup_DefaultGroup_ReturnsValidationError` | 試刪 `_originalDefaultGroup`;`error.type == "validation_error"` |
| B18 | `RemoveGroup_NonExistent_ReturnsNotFound` | `error.type == "not_found"` |

(實際 B 節 18 個,比 §測試清單概覽估算多 3 個,因為拆開細節)

---

### C. Label(Stage 2;9 tests)

| # | Test 名 | 檢查點 |
|---|---|---|
| C1 | `CreateLabel_New_ReturnsCreatedTrue` | `created:true`; `settings.GetLabels().Contains(label)` |
| C2 | `CreateLabel_Existing_IsIdempotent` | `created:false`; 冇 error |
| C3 | `CreateLabel_WithSpace_ReturnsValidationError` | 傳 `"bad label"`;`error.type == "validation_error"` |
| C4 | `CreateLabel_WithBracket_ReturnsValidationError` | 傳 `"[bad]"`;同上 |
| C5 | `CreateLabel_Empty_ReturnsValidationError` | 傳 `""`;同上 |
| C6 | `ListLabels_IncludesCreatedLabel` | response.labels 含 TestLabel |
| C7 | `RemoveLabel_Unused_Succeeds` | `deleted:true`, `affectedEntries:0` |
| C8 | `RemoveLabel_InUse_WithoutForce_ReturnsInUse` | 依賴 entry 已建;`error.type == "in_use"` |
| C9 | `RemoveLabel_InUse_WithForce_StripsFromEntriesAndDeletes` | `affectedEntries > 0`; label 消失;entry.labels 唔再含 |
| C10 | `RemoveLabel_NonExistent_ReturnsNotFound` | `error.type == "not_found"` |

(10 個,同 §概覽 9 個差 1,為完整錯誤覆蓋)

---

### D. Entry(Stage 3;22 tests)

**前置**: 3 個 dummy asset 已喺 `OneTimeSetUp` 建好:
- `_asset1Path = TestDir/DummyA.asset`
- `_asset2Path = TestDir/DummyB.asset`
- `_asset3Path = TestDir/DummyC.asset`

| # | Test 名 | 檢查點 |
|---|---|---|
| D1 | `AddEntries_SingleAsset_NoAddress_UsesAssetPathAsDefault` | `added:1`; `entry.address == asset1Path` |
| D2 | `AddEntries_WithCustomAddress_UsesProvidedAddress` | `entry.address == "custom/ui/main"` |
| D3 | `AddEntries_WithNewLabel_AutoCreatesLabelWithWarning` | `settings.GetLabels().Contains(newLabel)`; `warnings` 含 "created automatically" |
| D4 | `AddEntries_WithExistingLabel_AppliesWithoutWarning` | 預先 create label;`warnings` 唔含 "created" |
| D5 | `AddEntries_InvalidAssetPath_SkippedWithWarning` | `skipped:1`, `added:0`; `warnings` 含 "not found" |
| D6 | `AddEntries_EmptyArray_ReturnsValidationError` | `error.type == "validation_error"` |
| D7 | `AddEntries_NonExistentGroup_ReturnsNotFound` | `error.type == "not_found"` |
| D8 | `AddEntries_BatchThreeAssets_AllRegistered` | `added:3`; `group.entries.Count == 3` |
| D9 | `ListEntries_NoFilter_ReturnsAll` | 確認至少含 3 個 test entry |
| D10 | `ListEntries_GroupFilter_OnlyReturnsThatGroup` | 每 `entry.group == TestGroupName` |
| D11 | `ListEntries_LabelFilter_OnlyReturnsEntriesWithLabel` | 每 entry.labels 含 filter label |
| D12 | `ListEntries_AddressPatternGlob_MatchesStar` | `"ui/*"` match `ui/main` 但唔 match `other` |
| D13 | `ListEntries_AssetPathPrefix_MatchesPrefix` | `asset_path_prefix: TestDir` 只返 test entries |
| D14 | `ListEntries_Limit1_SetsTruncatedTrue` | `limit:1`; `entries.Count == 1`; `total >= 3`; `truncated:true` |
| D15 | `SetEntry_ChangeAddress_UpdatesAddress` | `entry.address` 變為 `new_address` |
| D16 | `SetEntry_AddLabels_AppliesAllAndAutoCreatesMissing` | entry.labels 含新 label;`warnings` 含 "created" |
| D17 | `SetEntry_RemoveLabels_StripsSpecified` | 指定 label 被移除,其他保留 |
| D18 | `SetEntry_PartialUpdateOnlyAddress_DoesNotTouchLabels` | 傳 `new_address` 不傳 labels;entry.labels 保持不變 |
| D19 | `SetEntry_ByAssetPathNotGuid_Resolves` | 只傳 `asset_path`;成功 |
| D20 | `SetEntry_NeitherGuidNorAssetPath_ReturnsValidationError` | `error.type == "validation_error"` |
| D21 | `SetEntry_NonExistent_ReturnsNotFound` | `error.type == "not_found"` |
| D22 | `MoveEntries_AcrossGroups_UpdatesParent` | `moved:N`; `entry.parentGroup.Name == targetGroup` |
| D23 | `MoveEntries_NonExistentTarget_ReturnsNotFound` | `error.type == "not_found"` |
| D24 | `MoveEntries_EntryByAssetPath_Resolves` | 只傳 `asset_path` 都 work |
| D25 | `RemoveEntries_BatchThree_RemovesAll` | `removed:3`; `FindAssetEntry` 返 null |
| D26 | `RemoveEntries_MixedValidAndInvalid_ReportsBoth` | `removed:N`, `notFound:M` |

(26 個,比概覽 22 個多,為細緻覆蓋)

---

### E. Query(Stage 3;3 tests)

| # | Test 名 | 檢查點 |
|---|---|---|
| E1 | `FindAsset_Addressable_ReturnsFullEntryInfo` | `found:true`; `entry.group`, `address`, `labels` 正確 |
| E2 | `FindAsset_ExistsButNotAddressable_ReturnsFoundFalse` | 建 dummy asset 但唔加入 Addressables;`found:false`; `guid` 仍返 |
| E3 | `FindAsset_NonExistentPath_ReturnsNotFound` | `error.type == "not_found"` |

---

### F. Golden Path Scenario(Stage 4;1 ordered integration test)

**F1. `FullLifecycle_InitToCleanup_SucceedsAtEveryStep`**

用 `[Test, Order(999)]` 排喺所有 unit test 之後。單一 method,sequential assertion,模擬 agent 實際用法:

```
Step 1:  addr_init_settings              → idempotent (created:false)
Step 2:  addr_get_settings               → initialized:true, 記錄 defaultGroup
Step 3:  addr_create_group(TestGroup1, PackSeparately, includeInBuild:false)
Step 4:  addr_create_label(TestLabel)
Step 5:  addr_add_entries(TestGroup1, [
            {asset1, labels:[TestLabel]},
            {asset2, address:"ui/main", labels:[TestLabel]},
            {asset3}
         ])                               → added:3, warnings 若有則 log
Step 6:  addr_list_entries(group=TestGroup1)    → total:3
Step 7:  addr_find_asset(asset2)                → found:true, group, address==ui/main
Step 8:  addr_set_entry(asset1, new_address:"ui/alt", add_labels:[TestLabel2])
                                                 → TestLabel2 auto-created
Step 9:  addr_list_labels                        → 含 TestLabel + TestLabel2
Step 10: addr_create_group(TestGroup2)
Step 11: addr_move_entries(TestGroup2, [asset1]) → moved:1
Step 12: addr_list_entries(group=TestGroup2)     → total:1; asset1 groups match
Step 13: addr_remove_label(TestLabel2, force:true)
                                                 → affectedEntries:1
Step 14: addr_remove_entries([asset2, asset3, {invalid}])
                                                 → removed:2, notFound:1
Step 15: addr_remove_group(TestGroup1, force:true)  → 已清 entries
Step 16: addr_remove_group(TestGroup2, force:true)  → 含 asset1 → removedEntryCount:1
Step 17: addr_list_groups                        → 兩個 test group 都消失
Step 18: 最終 assert _originalDefaultGroup 仍係 default
```

**設計重點**:
- 每步之間有 `AssertSuccess` 或 `AssertError`,任何一步失敗都會 hard fail 並標明 step 號
- Step 之間嘅狀態傳遞透過 local 變數(guid、entry count)避免重查 Addressables API
- 最後一步必須驗證 default group 冇被污染

---

## 執行策略

### 用 MCP `run_tests`
Stage 1 驗收:
```json
{ "testMode": "EditMode", "testFilter": "McpUnity.Tests.Addressables" }
```

如果 `run_tests` 返 0/0,照 `doc/lessons/unity-mcp-lessons.md` 嘅 pitfall check:
1. Consumer project `manifest.json` 有冇 `testables: ["com.gamelovers.mcp-unity"]`
2. `McpUnity.Addressables.Tests.asmdef` 嘅 `defineConstraints` 有冇齊
3. `com.unity.addressables` 版本 ≥ 1.19.0

### Edit → recompile → run 順序
**務必**:
1. Write/Edit 改 C# 檔案
2. `recompile_scripts`(可能需要先手動喺 Unity refresh,因為 `recompile_scripts` 唔會 `AssetDatabase.Refresh` — 已記於 lessons)
3. 確認 console 無 compile error(`get_console_logs logType=error`)
4. `run_tests`

中間跳步會見到 stale DLL 嘅舊結果(lessons 已記)。

---

## 明確不測(列出以避免將來質疑)

1. **`addr_get_settings` 嘅 `initialized:false` 回傳 shape** — 屬於 happy path 嘅特殊 branch,而唔係 error。需要移除 `AddressableAssetSettingsDefaultObject.Settings` 先可以真正觸發,副作用太大,改為 code review + 手動冒煙測試覆蓋。(注意:經 `AddrHelper.TryGetSettings` 嘅 `not_initialized` **error** branch 已經喺 `A0_Tools_WhenNotInitialized_*` 用 SettingsProvider 注入覆蓋。)
2. **`addr_init_settings` 實際建立新 folder** — fixture 進入時 project 通常已 init,所以只測 idempotent path + folder 參數驗證(拒絕 `Assets/` 以外同 path traversal)。`addr_init_settings` 第一次建 settings 嘅路徑由 README/CHANGELOG 文字覆蓋。
3. **並發 / domain reload** — Addressables 本身單線程,無意義測。
4. **Build-time 行為** — v2 才有 build tool。
5. **完整 Schema 深層欄位**(compression、BundleNaming、Provider type 等)— v1 只暴露 3 個欄位(`BundleMode`、`IncludeInBuild`、schema 類型組合),深度測試留 v2 `addr_set_schema` 嗰陣。
6. **性能 / load test** — 加 10000 個 entry 一次。超出單元測試範圍。

---

## 風險登記

| 風險 | 影響 | 緩解 |
|---|---|---|
| `SetAsDefault` / `SetDefaultGroup` 測試改咗 default group | 影響其他 test + 用戶 project | `OneTimeSetUp` 記 `_originalDefaultGroup`;`[TearDown]` 還原;多一個 `[OneTimeTearDown]` 做 final restore |
| Fixture 中途 crash,殘留 `McpAddrTest_*` artifacts | 下次 test 會見到 duplicate | `OneTimeSetUp` 先 scan + 清理所有 `McpAddrTest_*` 開頭嘅 group/label,再開始 |
| Addressables API 版本差異 | 測試喺 1.19.x 通,1.21.x 唔通(或反之) | asmdef `versionDefines expression: "1.19.0"` 已 gate 最低版本;若有 breaking,留待 lessons 補 workaround |
| `AssetDatabase.DeleteAsset` 觸發 Addressables asset modification processor 自動 remove entry | 可能令「移除 asset 但保留 entry」嘅 test 行為唔如預期 | Teardown 順序:先 `RemoveAssetEntry` 再 `DeleteAsset`,避免依賴 processor |
| Dummy `AddrTestDummySO` 類型 leak 到其他 asmdef | ScriptableObject type 唔見到 | 用 `internal` 並限定喺 Test asmdef 內,不要 register 到 MCP |

---

## 文件輸出物

| 檔案 | Stage | 目的 |
|---|---|---|
| `Editor/Tools/Addressables/Addr*Tool.cs` × 15 | 1 | 加 `[McpUnityFirstParty]` |
| `Editor/Tools/Addressables/AddrHelper.cs` | 1 | 加 `[assembly: InternalsVisibleTo]` |
| `Editor/Tests/Addressables/McpUnity.Addressables.Tests.asmdef` | 1 | Test 子 asmdef |
| `Editor/Tests/Addressables/AddrTests.cs` | 1→4 | Stage 漸進追加 |
| `doc/lessons/unity-mcp-lessons.md` | 4(若有) | Addressables API gotchas |
| `CHANGELOG.md` | 4 | `### Tests` section 加入 1.11.x patch entry,跟 1.11.0 Localization 同格式 |

---

## 階段通過條件

| Stage | 必須綠 | 允許紅 |
|---|---|---|
| 1 | 3/3 smoke test | — |
| 2 | 31/31(3 + 18 + 10) | — |
| 3 | 60/60(31 + 26 + 3) | — |
| 4 | 61/61 連續跑 3 次穩定 | — |

若任何階段有 red,唔進入下一階段,先修 root cause(對 lessons 有貢獻時就追加)。
