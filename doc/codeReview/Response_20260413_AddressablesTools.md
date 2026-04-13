# Code Review Response — Addressables MCP Tool Suite

- 日期: 2026-04-13
- 對應 Request: `doc/codeReview/Request_20260413_AddressablesTools.md`
- 審查範圍: `fdebeb6` + `65a10ab`（C# tools、TS wrappers、NUnit tests、需求/測試文件）

## 維度 1. 代碼質素 (Code Quality)

整體結構清晰，Addressables 子 assembly 的切分、共用 helper 抽象、Node wrapper 統一樣板都做得一致，且測試命名與分段具可讀性。主要問題不在架構，而是在「需求文件與實作行為不一致」以及「高風險分支缺測」。

## 維度 2. 優點 (Pros)

- 子 assembly + `versionDefines` + `defineConstraints` 的 optional package pattern 落地完整，符合無 Addressables 專案零影響目標。
- `AddrHelper` 統一 settings/group/entry/label 的共用邏輯，避免 15 支工具分散重複實作。
- `AddrTests` 覆蓋廣，並包含 Golden Path，對回歸有實際保護價值。
- Node wrapper (`Server~/src/tools/addressablesTools.ts`) 介面一致，對 agent 呼叫體驗友善。

## 維度 3. 缺點與風險 (Cons & Risks)

### 🟡 Major 1 — `addr_get_settings` 缺少 spec 要求的 `version` 欄位 (Spec coverage mismatch)
- 證據:
  - Spec 回傳範例包含 `version`：`doc/requirement/feature_addressables_mcp.md:53`
  - 實作回傳未包含 `version`：`Editor/Tools/Addressables/AddrGetSettingsTool.cs:54-66`
- 影響:
  - caller 無法根據 Addressables 版本做 capability 判斷（尤其後續 v2 加強 schema/build 工具時）。
  - 屬於需求覆蓋缺口（Spec Coverage 優先級最高）。

### 🟡 Major 2 — `addr_add_entries` 對 asset 不存在時與 spec 定義不一致（目前是 skip+warning，不是 `not_found` error）
- 證據:
  - Spec 明確寫 asset 不存在屬 `not_found`：`doc/requirement/feature_addressables_mcp.md:224`
  - 實作採 warning + `skipped++`：`Editor/Tools/Addressables/AddrAddEntriesTool.cs:76-82`
- 影響:
  - 批次流程可能在 caller 期待「失敗即中止」時，變成靜默部分成功，導致內容漏掛載卻繼續流程。
  - API contract 與需求不一致，增加工具使用不確定性。

### 🟡 Major 3 — `addr_init_settings` 未限制 `folder` 路徑範圍，可能產生非預期目錄副作用
- 證據:
  - 直接使用外部輸入 `folder` 並 `Directory.CreateDirectory(folder)`：`Editor/Tools/Addressables/AddrInitSettingsTool.cs:49-60`
- 影響:
  - 傳入 `../` 或非 `Assets/...` 路徑時，可能在專案外或非預期位置建立資料夾；即使後續 `Create(...)` 失敗也會留下副作用。
  - 在 agent 自動化場景下，屬於高風險寫入行為。

### 🟡 Major 4 — 測試計畫宣告「所有 error_type 都要覆蓋」但 `not_initialized` 分支實際無自動測試
- 證據:
  - 測試目標宣告需覆蓋 `not_initialized`：`doc/requirement/feature_addressables_mcp_tests.md:15`
  - 同文件後段又明確列為不測：`doc/requirement/feature_addressables_mcp_tests.md:414`
  - 測試碼內無 `not_initialized` 斷言（`AddrTests.cs` 無該字串匹配）。
- 影響:
  - 首次使用者路徑（Addressables 尚未初始化）無回歸防線，後續 refactor 容易破壞此關鍵分支。

## 維度 4. 改善建議 (Improvement Suggestions)

### 針對 Major 1：補齊 `version` 欄位

```csharp
// Editor/Tools/Addressables/AddrGetSettingsTool.cs
using UnityEditor.PackageManager;

var packageInfo = PackageInfo.FindForAssembly(typeof(AddressableAssetSettings).Assembly);

return new JObject
{
    ["success"] = true,
    ["initialized"] = true,
    ["defaultGroup"] = settings.DefaultGroup?.Name,
    ["activeProfile"] = profileName,
    ["profileVariables"] = profileVariables,
    ["groupCount"] = settings.groups.Count,
    ["entryCount"] = AddrHelper.GetTotalEntryCount(settings),
    ["labels"] = labels,
    ["version"] = packageInfo?.version ?? "unknown"
};
```

### 針對 Major 2：定義一致的 missing-asset 行為（建議預設嚴格）

```csharp
// Editor/Tools/Addressables/AddrAddEntriesTool.cs
bool failOnMissing = parameters["fail_on_missing_asset"]?.ToObject<bool>() ?? true;
var missingAssets = new JArray();

...
string guid = AssetDatabase.AssetPathToGUID(assetPath);
if (string.IsNullOrEmpty(guid))
{
    if (failOnMissing)
    {
        return McpUnitySocketHandler.CreateErrorResponse(
            $"Asset '{assetPath}' not found",
            "not_found");
    }

    missingAssets.Add(assetPath);
    warnings.Add($"Asset '{assetPath}' not found, skipped");
    skipped++;
    continue;
}

...
if (missingAssets.Count > 0) result["missingAssets"] = missingAssets;
```

### 針對 Major 3：限制 `folder` 只能在 `Assets/` 且拒絕 path traversal

```csharp
// Editor/Tools/Addressables/AddrInitSettingsTool.cs
string folder = parameters["folder"]?.ToString();
folder = string.IsNullOrWhiteSpace(folder) ? DefaultConfigFolder : folder.Trim();
folder = folder.Replace('\\', '/').TrimEnd('/');

if (!folder.StartsWith("Assets/"))
{
    return McpUnitySocketHandler.CreateErrorResponse(
        "Parameter 'folder' must be under 'Assets/'",
        "validation_error");
}
if (folder.Contains("../") || folder.Contains("..\\"))
{
    return McpUnitySocketHandler.CreateErrorResponse(
        "Parameter 'folder' must not contain parent traversal ('..')",
        "validation_error");
}

// 建議改用 AssetDatabase.CreateFolder 逐層建立，避免直接 IO 副作用
```

### 針對 Major 4：為 `not_initialized` 做可控、低副作用測試注入點

```csharp
// Editor/Tools/Addressables/AddrHelper.cs
internal static System.Func<AddressableAssetSettings> SettingsProvider =
    () => AddressableAssetSettingsDefaultObject.GetSettings(false);

public static AddressableAssetSettings TryGetSettings(out JObject error)
{
    error = null;
    var settings = SettingsProvider();
    if (settings != null) return settings;

    error = McpUnitySocketHandler.CreateErrorResponse(
        "Addressables is not initialized...",
        "not_initialized");
    return null;
}
```

```csharp
// Editor/Tests/Addressables/AddrTests.cs
[Test]
public void A0_ListGroups_WhenNotInitialized_ReturnsNotInitialized()
{
    var original = AddrHelper.SettingsProvider;
    AddrHelper.SettingsProvider = () => null;
    try
    {
        var result = new AddrListGroupsTool().Execute(new JObject());
        AssertError(result, "not_initialized");
    }
    finally
    {
        AddrHelper.SettingsProvider = original;
    }
}
```

## 維度 5. 需求覆蓋率 (Spec Coverage)

### 已覆蓋
- 15 支 `addr_*` 工具存在且 Node/C# method 名一致。
- Optional package gating (`MCP_UNITY_ADDRESSABLES`) 與分離 assembly 正確落地。
- 主要 CRUD 行為與大部分回傳結構符合需求文件。

### 遺漏/不一致（高優先）
- `addr_get_settings` 缺 `version` 欄位（Spec 有、實作無）。
- `addr_add_entries` 對 missing asset 的錯誤語義與 Spec 不一致（Spec 要 `not_found` error，實作為 skip+warning）。

### 未測試清單
- `not_initialized` error path 無自動化測試（與測試目標聲明不一致）。

## Review 意見追蹤
- [ ] 🟡 補齊 `addr_get_settings` 的 `version` 回傳欄位，對齊需求文件
- [ ] 🟡 對齊 `addr_add_entries` 的 missing-asset contract（`not_found` 或明確新增 strict/lenient 模式）
- [ ] 🟡 為 `addr_init_settings.folder` 加入 `Assets/` 範圍與 path traversal 驗證
- [ ] 🟡 補上 `not_initialized` 分支的可重複自動化測試

## Refactor Prompt

根據 `doc/codeReview/Response_20260413_AddressablesTools.md` 的審查意見，請執行以下修正：

### 🟡 Major（強烈建議）
1. `addr_get_settings` 回傳新增 `version`，並同步更新 TS wrapper data 映射與對應測試。
2. 明確化 `addr_add_entries` 遇到不存在 asset 的 contract，至少提供可控 strict mode（預設行為需與 spec 對齊）。
3. `addr_init_settings` 對 `folder` 做嚴格驗證（必須在 `Assets/`、拒絕 `..` traversal），避免目錄副作用。
4. 為 `not_initialized` 加入可重複、低副作用的單元測試注入點，讓 error path 真正受測。

### 涉及檔案
- `Editor/Tools/Addressables/AddrGetSettingsTool.cs`
- `Editor/Tools/Addressables/AddrAddEntriesTool.cs`
- `Editor/Tools/Addressables/AddrInitSettingsTool.cs`
- `Editor/Tools/Addressables/AddrHelper.cs`
- `Server~/src/tools/addressablesTools.ts`
- `Editor/Tests/Addressables/AddrTests.cs`
- `doc/requirement/feature_addressables_mcp.md`（若 contract 決策有變，需同步修文）
- `doc/requirement/feature_addressables_mcp_tests.md`（修正測試目標與不測條目矛盾）

---

⚠️ 若有對應 Implementation Tracker，請在相關 Phase 加上 `[Review Fix]` 決策記錄與本審查報告連結。
