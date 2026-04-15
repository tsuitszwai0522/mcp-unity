# Code Review Response — Addressables Group Schema + Profile Management Tools

- **日期**: 2026-04-15
- **審查對象**: `doc/codeReview/Request_20260415_AddressablesSchemaAndProfile.md`
- **審查結果**: **需修改後再合併**
- **Critical**: 0
- **Major**: 4
- **Minor**: 3
- **本機驗證**:
  - 靜態審查：已讀 request、`doc/requirement/feature_addressables_mcp.md` v1.1 段落、C# tools、TS wrappers、README、CHANGELOG、AGENTS.md、NUnit 測試
  - 未重新執行 `npm run build` 或 Unity Test Runner；本報告以 diff / source review 為準

---

## 維度 1. 代碼質素 (Code Quality)

整體設計方向正確：schema update 集中在 `AddrSetGroupSchemaTool`，讀寫工具分離；profile 相關工具也沒有把 helper 擴散到不必要的共用層。`build_path` / `load_path` 透過 `ProfileValueReference.SetVariableByName(...)` 是正確 API 路徑，`addr_get_group_schema` 額外回傳 `*_value` 也很實用。

主要問題在「錯誤路徑仍可能有 side effect」與「Unity 端驗證不夠硬」。MCP wrapper 的 zod schema 可以保護正常 Node caller，但 repo 已有 `batch_execute`、Unity WebSocket bridge、以及 tool 直接測試路徑，因此 C# tool 仍然必須自己守住所有重要 contract。

- 🟡 **Major** — `addr_set_group_schema` 逐欄位邊驗證邊寫入；若後面的欄位錯誤，工具回傳 error，但前面的欄位已在 Unity 記憶體中被改掉，且 caller 拿不到 `changed` / `diff`。
- 🟡 **Major** — `TryParseEnum` 使用 `Enum.TryParse` 後沒有 `Enum.IsDefined`；`"999"` 這類未定義數字字串會被接受並寫進 schema，違反需求文件列出的 enum 值集合。
- 🟡 **Major** — release / agent guide 未覆蓋本次新增工具：`CHANGELOG.md` 沒有 Addressables v1.1 entry，版本仍停在已被 2026-04-14 usability release 使用的 `1.12.0`，`AGENTS.md` 的 current tools 也沒有新增 6 個 `addr_*` tools。
- 🟡 **Major** — 測試沒有覆蓋上述兩個核心錯誤路徑：multi-field partial failure side effect、numeric/undefined enum rejection。
- 🟢 **Minor** — `addr_set_profile_variable` 的 TS / C# description 沒明確說 `create_if_missing` 會讓新變數出現在所有 profiles；需求文件有寫，但 tool description 是 agent 最常讀到的 contract。
- 🟢 **Minor** — `TryParseBool` / `TryParseInt` 透過 `ToObject<T>` 會做 Newtonsoft coercion，Unity 端 contract 比 schema 寬；建議改成 `JTokenType` 檢查。
- 🟢 **Minor** — `ApplyProfileReference` 重複呼叫 `GetVariableNames()`；不是效能問題，但錯誤訊息與 validation 用同一份 list 會更乾淨。

---

## 維度 2. 優點 (Pros)

- ✅ `addr_set_group_schema` 的 `changed` + `diff` 設計符合 agent 工作流，尤其適合 dry-run 後再 apply。
- ✅ `addr_get_group_schema` 回傳 `build_path_value` / `load_path_value`，補上 profile token 展開後的實際 URL，可直接驗證 Remote CDN 接線。
- ✅ `AddrSetProfileVariableTool` 預設 `create_if_missing=false`，保守地避免不小心建立 global profile variable。
- ✅ 測試 fixture 會保存並還原 `_originalActiveProfileId`，也會清理 `McpAddrTest_*` profiles / variables，方向正確。
- ✅ README 已加入 6 個工具的一線說明與 example prompt，對使用者可見面有基本覆蓋。

---

## 維度 3. 缺點與風險 (Cons & Risks)

### 3.1 🟡 Major — `addr_set_group_schema` 錯誤路徑會留下部分改動

位置：`Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs:79`

目前 `Execute()` 直接迭代 `values.Properties()`，每個欄位在 `ApplyField()` 內驗證後立刻寫入 schema。若 payload 是：

```json
{
  "group": "TMP_Font_CN",
  "values": {
    "compression": "LZMA",
    "totally_fake_field": "x"
  }
}
```

流程會先把 `Compression` 改成 `LZMA`，然後在第二個欄位回 `validation_error`。這有三個問題：

- caller 看到的是 error response，沒有 `changed` / `diff`，會合理地以為沒有任何欄位成功。
- in-memory Addressables settings 已經變了；即使當下沒有 `SaveSettings()`，後續任何 `SaveAssets()` 或其他 Addressables dirty flush 都可能把這個「失敗 request」的前半段持久化。
- 需求文件要求錯誤回傳清晰；目前錯誤本身清晰，但 state 結果不清晰。

自我評估有點出這個弱點，判斷屬實，而且不建議以「agent 先 dry-run」作為主要緩解。工具層應該提供可預期的錯誤語義。

### 3.2 🟡 Major — enum parser 接受未定義數字值

位置：`Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs:315`

`Enum.TryParse(raw, true, out parsed)` 對 enum 會接受數字字串；例如 `"999"` 可能 parse 成未定義的 `BundleCompressionMode` 值。這會繞過需求列出的合法集合：

- `compression`: `Uncompressed` / `LZ4` / `LZMA`
- `packed_mode`: `PackTogether` / `PackSeparately` / `PackTogetherByLabel`
- `bundle_naming`: `AppendHash` / `NoHash` / `OnlyHash` / `FileNameHash`

Node wrapper 的 zod enum 會擋住正常 MCP caller，但 Unity tool 本身仍會被 direct WebSocket、`batch_execute` 或單元測試直接呼叫。C# 端需要拒絕未定義 enum，否則會把非法 Addressables schema 狀態寫入專案。

### 3.3 🟡 Major — release metadata / AGENTS.md 未覆蓋新增工具

位置：
- `CHANGELOG.md`
- `package.json`
- `Server~/package.json`
- `server.json`
- `AGENTS.md`

`doc/requirement/feature_addressables_mcp.md` 的 v1.1 step 12 要求 CHANGELOG bump，但目前 `CHANGELOG.md` 最新 `1.12.0` 是 2026-04-14 usability release，沒有本次 Addressables schema/profile 工具。三個版本 metadata 也仍是 `1.12.0`，代表本次新增 6 個工具沒有 release identity。

此外，使用者提供的 `AGENTS.md` update policy 明確寫：「tools/resources/prompts are added/removed/renamed」時要更新此檔，但 `AGENTS.md` current tools list 沒有：

- `addr_get_group_schema`
- `addr_set_group_schema`
- `addr_list_profiles`
- `addr_get_active_profile`
- `addr_set_active_profile`
- `addr_set_profile_variable`

這會讓後續 agent 讀 repo guide 時看不到新能力，也會違反 repo 自己的 release checklist。

### 3.4 🟡 Major — 測試缺少最重要的 failure-mode regression

位置：`Editor/Tests/Addressables/AddrTests.cs`

目前 G1-G9 / H1-H7 覆蓋 happy path、dry-run、no-op、單欄位 invalid enum、未知欄位、未知 profile variable。缺口是：

- multi-field payload 中第 N 個欄位失敗時，第 1..N-1 個欄位不應留下 side effect。
- enum parser 應拒絕 numeric / undefined 值，例如 `compression = "999"`。
- bool / int Unity 端應拒絕 schema 外型別，避免 direct caller 透過 `ToObject<T>` coercion 進來。

這些是本次最容易造成 project state drift 的路徑，應該補成 NUnit regression tests。

### 3.5 🟢 Minor — `create_if_missing` 的 global 影響應寫進 tool description

位置：
- `Editor/Tools/Addressables/AddrProfileTools.cs:214`
- `Server~/src/tools/addressablesTools.ts:691`
- `README.md:322`

需求文件已正確說明 `CreateValue` 是 profile-settings level，會影響所有 profiles。但 TS description 只寫「profile-settings level」，C# description 更只寫「Creates the variable if it doesn't exist yet」。對 agent 來說，tool description 是最直接的行為提示，應該明確寫出 "affects all profiles, not only the named profile"。

### 3.6 🟢 Minor — Unity 端 primitive parser 應避免 Newtonsoft coercion

位置：`Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs:273`

`value.ToObject<bool>()` / `value.ToObject<int>()` 會接受比 JSON schema 更寬的輸入。這不是目前 Node caller 的問題，但 Unity tool contract 應該和公開 schema 一致：boolean 欄位只收 JSON boolean，integer 欄位只收 JSON integer。`retry_count` / `timeout` 是否允許負數也應明確決定；若 Unity Addressables 不接受負值，這裡應先回 `validation_error`。

### 3.7 自我評估驗證

- **脆弱點 #1 partial apply**：屬實，且風險被低估。這不是單純 agent expectation 問題，而是 error response 與實際 state 可能不一致。
- **脆弱點 #2 CreateValue global**：屬實。預設 `create_if_missing=false` 是合理緩解，但 response / description 建議加 warning 或明確文字。
- **脆弱點 #3 profile variable 層級**：需求文件講清楚，`addr_list_profiles` 結構也能幫助理解；tool description 還可以更明確。
- **脆弱點 #4 C# 7.3 enum constraint**：可接受。repo/feature baseline 已是 Unity 2022.3，這不是本次合併阻擋點。
- **脆弱點 #5 C# using 離線驗證**：這次已修，不需要為 using directive 單獨加 hook；更實用的是確保 Unity compile / EditMode tests 在 release 前跑過。

---

## 維度 4. 改善建議 (Improvement Suggestions)

### 4.1 將 `addr_set_group_schema` 改成 validate-all then apply

建議把欄位處理拆成「先規劃變更」與「再套用」。dry-run 只回 plan；實際 apply 在所有欄位都驗證成功後才開始。

```csharp
private sealed class SchemaChange
{
    public string Field;
    public JToken From;
    public JToken To;
    public Action Apply;
}

public override JObject Execute(JObject parameters)
{
    // ... resolve settings/group/schema/values ...

    var changes = new List<SchemaChange>();
    foreach (var prop in values.Properties())
    {
        var planError = PlanField(settings, schema, prop.Name, prop.Value, changes);
        if (planError != null)
        {
            return planError;
        }
    }

    if (!dryRun)
    {
        foreach (var change in changes)
        {
            change.Apply();
        }

        if (changes.Count > 0)
        {
            AddrHelper.SaveSettings(settings, AddressableAssetSettings.ModificationEvent.GroupSchemaModified);
        }
    }

    var diff = new JObject();
    foreach (var change in changes)
    {
        diff[change.Field] = new JObject
        {
            ["from"] = change.From,
            ["to"] = change.To
        };
    }

    return new JObject
    {
        ["success"] = true,
        ["type"] = "text",
        ["group"] = group.Name,
        ["dryRun"] = dryRun,
        ["changed"] = new JArray(changes.Select(c => c.Field)),
        ["diff"] = diff
    };
}
```

欄位規劃範例：

```csharp
private static JObject PlanCompression(
    BundledAssetGroupSchema schema,
    JToken value,
    List<SchemaChange> changes)
{
    if (!TryParseEnum<BundledAssetGroupSchema.BundleCompressionMode>(
            value, "compression", out var parsed, out var err))
    {
        return err;
    }

    var before = schema.Compression;
    if (before == parsed) return null;

    changes.Add(new SchemaChange
    {
        Field = "compression",
        From = before.ToString(),
        To = parsed.ToString(),
        Apply = () => schema.Compression = parsed
    });
    return null;
}
```

`build_path` / `load_path` 也可以先 validate variable existence，最後才呼叫 `SetVariableByName`：

```csharp
private static JObject PlanProfileReference(
    AddressableAssetSettings settings,
    ProfileValueReference reference,
    string field,
    JToken value,
    List<SchemaChange> changes)
{
    string variableName = value?.ToString();
    if (string.IsNullOrWhiteSpace(variableName))
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"Field '{field}' must be a non-empty profile variable name",
            "validation_error");
    }

    string before = reference.GetName(settings);
    if (before == variableName) return null;

    var variableNames = settings.profileSettings.GetVariableNames();
    if (!variableNames.Contains(variableName))
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"Profile variable '{variableName}' does not exist. Available: [{string.Join(", ", variableNames)}]",
            "variable_not_found");
    }

    changes.Add(new SchemaChange
    {
        Field = field,
        From = before,
        To = variableName,
        Apply = () =>
        {
            if (!reference.SetVariableByName(settings, variableName))
            {
                throw new InvalidOperationException($"Failed to set profile variable '{variableName}' on {field}");
            }
        }
    });
    return null;
}
```

如果不想讓 `Apply` throw，可讓 apply phase 回傳 `set_failed`；重點是所有 validation error 必須在任何 mutation 前完成。

### 4.2 加硬 enum / primitive parser

```csharp
private static bool TryParseEnum<TEnum>(
    JToken value,
    string field,
    out TEnum parsed,
    out JObject error)
    where TEnum : struct, Enum
{
    parsed = default;
    error = null;

    if (value == null || value.Type != JTokenType.String)
    {
        error = McpUnitySocketHandler.CreateErrorResponse(
            $"Field '{field}' must be one of: {string.Join(" | ", Enum.GetNames(typeof(TEnum)))}",
            "validation_error");
        return false;
    }

    string raw = value.ToString();
    if (string.IsNullOrWhiteSpace(raw)
        || int.TryParse(raw, out _)
        || !Enum.TryParse(raw, true, out parsed)
        || !Enum.IsDefined(typeof(TEnum), parsed))
    {
        error = McpUnitySocketHandler.CreateErrorResponse(
            $"Field '{field}' must be one of: {string.Join(" | ", Enum.GetNames(typeof(TEnum)))} (got '{raw}')",
            "validation_error");
        return false;
    }

    return true;
}
```

Primitive parser 建議：

```csharp
private static bool TryParseBool(JToken value, string field, out bool parsed, out JObject error)
{
    parsed = false;
    error = null;
    if (value?.Type == JTokenType.Boolean)
    {
        parsed = value.Value<bool>();
        return true;
    }

    error = McpUnitySocketHandler.CreateErrorResponse(
        $"Field '{field}' must be a boolean",
        "validation_error");
    return false;
}

private static bool TryParseNonNegativeInt(JToken value, string field, out int parsed, out JObject error)
{
    parsed = 0;
    error = null;
    if (value?.Type == JTokenType.Integer)
    {
        parsed = value.Value<int>();
        if (parsed >= 0) return true;
    }

    error = McpUnitySocketHandler.CreateErrorResponse(
        $"Field '{field}' must be a non-negative integer",
        "validation_error");
    return false;
}
```

若 Addressables 允許 `-1` 表示 sentinel，請把 range 規則寫進需求文件與 TS schema；不要保持隱性 coercion。

### 4.3 補 release metadata / AGENTS.md

若本次是繼 `1.12.0` 後的新 feature release，建議使用下一個 minor，例如 `1.13.0`：

```json
// package.json
{
  "version": "1.13.0"
}
```

```json
// Server~/package.json
{
  "version": "1.13.0"
}
```

```json
// server.json
{
  "version": "1.13.0",
  "packages": [
    {
      "version": "1.13.0"
    }
  ]
}
```

`CHANGELOG.md` 建議加新段落：

```markdown
## [1.13.0] - 2026-04-15

### Added

- **Addressables group schema tools** — added `addr_get_group_schema` and
  `addr_set_group_schema` for partial `BundledAssetGroupSchema` updates,
  dry-run diff preview, and Local/Remote profile variable binding.
- **Addressables profile tools** — added `addr_list_profiles`,
  `addr_get_active_profile`, `addr_set_active_profile`, and
  `addr_set_profile_variable`.

### Tests

- **+16 EditMode tests** for Addressables schema/profile workflows.
```

`AGENTS.md` current tools list 也要加入 6 個新工具，否則 repo guide 與實際 MCP surface 不一致。

### 4.4 補測試

```csharp
[Test]
public void G10_SetGroupSchema_WhenLaterFieldInvalid_DoesNotApplyEarlierFields()
{
    CreateTestGroup(TestGroupName);

    new AddrSetGroupSchemaTool().Execute(new JObject
    {
        ["group"] = TestGroupName,
        ["values"] = new JObject { ["compression"] = "LZ4" }
    });

    var result = new AddrSetGroupSchemaTool().Execute(new JObject
    {
        ["group"] = TestGroupName,
        ["values"] = new JObject
        {
            ["compression"] = "LZMA",
            ["totally_fake_field"] = "x"
        }
    });

    AssertError(result, "validation_error");

    var readBack = new AddrGetGroupSchemaTool().Execute(new JObject { ["group"] = TestGroupName });
    AssertSuccess(readBack);
    Assert.AreEqual("LZ4", (readBack["values"] as JObject).Value<string>("compression"));
}

[Test]
public void G11_SetGroupSchema_NumericEnumString_ReturnsValidationError()
{
    CreateTestGroup(TestGroupName);

    var result = new AddrSetGroupSchemaTool().Execute(new JObject
    {
        ["group"] = TestGroupName,
        ["values"] = new JObject { ["compression"] = "999" }
    });

    AssertError(result, "validation_error");
}
```

TS description / README wording：

```ts
const description =
  'Set an Addressables profile variable on a named profile. Pass create_if_missing=true to create the variable at the profile-settings level; newly-created variables are added to ALL profiles, not only the named profile.';
```

---

## 維度 5. 需求覆蓋率 (Spec Coverage)

對照 `doc/requirement/feature_addressables_mcp.md` v1.1：

- ✅ 新增 6 個工具：C# `Name` 與 TS `name` 對齊。
- ✅ `addr_set_group_schema` 支援需求列出的 10 個 `values` 欄位。
- ✅ `dry_run` happy path 有測，read-back 不持久化有測。
- ✅ no-op `changed: []` 有測。
- ✅ `addr_get_group_schema` 回傳 12 個欄位，包含 `build_path_value` / `load_path_value`。
- ✅ profile list / active / set active / set variable 基本流程有測。
- ❌ **需求遺漏**：v1.1 step 12 / release checklist 未完成。`CHANGELOG.md`、version metadata、`AGENTS.md` 未反映本次新增工具。
- ❌ **錯誤語義缺口**：需求要求 enum 非法要明確 `validation_error`，但 C# parser 會接受 numeric undefined enum。
- ❌ **錯誤狀態缺口**：需求要求 partial update 語義清楚；目前 error path 下 state 可能部分改動但 response 沒揭露。

未測試清單：

- `addr_set_group_schema` multi-field validation failure 不應留下前面欄位的改動。
- numeric / undefined enum rejection。
- Unity 端 bool / int 型別嚴格性與 range 規則。
- `create_if_missing=true` 的 global profile variable 影響是否有 warning / description 覆蓋。

---

## Review 意見追蹤

- [ ] 🟡 修正 `addr_set_group_schema` validation failure 時的 partial side effect，改成 validate-all then apply 或等價語義
- [ ] 🟡 修正 C# enum parser，拒絕 numeric / undefined enum 值
- [ ] 🟡 補齊 CHANGELOG、版本 metadata、AGENTS.md current tools list
- [ ] 🟡 補 NUnit regression tests：multi-field failure no-side-effect、numeric enum rejection、primitive parser strictness
- [ ] 🟢 在 TS / C# tool description 與 README 中明確寫出 `create_if_missing` 會影響所有 profiles
- [ ] 🟢 將 bool / int parser 改成 `JTokenType` 驗證，並明確決定 `retry_count` / `timeout` range
- [ ] 🟢 `ApplyProfileReference` 共用同一份 `variableNames` list，避免 validation 與錯誤訊息重複取值

## Refactor Prompt

根據 `doc/codeReview/Response_20260415_AddressablesSchemaAndProfile.md` 的審查意見，請執行以下修正：

### 🔴 Critical（必須修復）

無。

### 🟡 Major（強烈建議）

1. 修改 `Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs`，將 `addr_set_group_schema` 改成先驗證所有欄位並建立 change plan，所有 validation 都通過後才套用 mutation；確保任一欄位失敗時，前面欄位不會留下 in-memory side effect。
2. 修改 `TryParseEnum<TEnum>`，拒絕 numeric string 與 `Enum.IsDefined == false` 的未定義 enum 值；同步補嚴格 primitive parser，至少為 bool / int direct caller 建立 Unity 端防線。
3. 補 `Editor/Tests/Addressables/AddrTests.cs` regression tests：multi-field payload 後段失敗不改前段欄位、`compression = "999"` 回 `validation_error`、bool/int schema 外型別或 range 規則。
4. 補 release / guide 文件：`CHANGELOG.md` 新增本次 Addressables schema/profile release entry，依實際 release 決策同步更新 `package.json`、`Server~/package.json`、`Server~/package-lock.json`、`server.json`，並在 `AGENTS.md` current tools list 加入 6 個新 `addr_*` tools。

### 涉及檔案

- `Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs`
- `Editor/Tools/Addressables/AddrProfileTools.cs`
- `Editor/Tests/Addressables/AddrTests.cs`
- `Server~/src/tools/addressablesTools.ts`
- `README.md`
- `AGENTS.md`
- `CHANGELOG.md`
- `package.json`
- `Server~/package.json`
- `Server~/package-lock.json`
- `server.json`

---

⚠️ **若有對應的 Implementation Tracker** (`doc/requirement/feature_addressables_mcp_tracker.md`)，
請在對應 Phase 的「關鍵決策」區塊中新增 `[Review Fix]` 標籤記錄本次修改內容，
並在「關聯審查」區塊連結本審查報告。
