# Code Review Response — Unity Localization MCP Tool Suite

- **審查日期**: 2026-04-13
- **對應 Request**: `Request_20260413_LocalizationTools.md`
- **Commit**: `e46ab7f`
- **版本**: v1.8.2 → v1.9.0
- **需求文件**: `doc/requirement/feature_localization_mcp.md`
- **審查者**: Claude (Code Reviewer)

---

## 0. 總評（Verdict）

| 維度 | 評分 | 說明 |
|------|------|------|
| 需求覆蓋率 | 🟢 完整 | spec 6 支 + 1 支合理擴充，回傳結構與 spec 範例一致 |
| 架構設計 | 🟢 優秀 | sub-assembly + versionDefines 是 Unity Editor 套件最乾淨的 optional package 模式 |
| 代碼品質 | 🟡 良好 | Helper 抽象到位、命名清楚，但有 3 處實作層級的 bug 需修正 |
| 測試完整性 | 🟡 部分 | NUnit 寫得完整但 Test Runner 未驗證；`loc_add_locale` 沒有獨立 test case |
| 文檔一致性 | 🟡 部分 | CHANGELOG/README 完整但 CLAUDE.md 缺 sub-assembly pattern 說明 |

**結論**：可合併（已 merged on main），但建議在 v1.9.1 補丁中修正以下 **3 個必修項**，並於後續 follow-up 處理 6 個建議項。

**最優先必修**：
1. **§B1** `LocDeleteEntryTool` 的 orphan entry leak（資料正確性）
2. **§B2** `LocSetEntriesTool` 的錯誤訊息覆蓋（可觀測性）
3. **§B3** `LocSetEntriesTool` 部分失敗時的 in-memory 污染（一致性）

---

## 1. 需求覆蓋率（Spec Coverage）

### 1.1 工具清單對照

| Spec # | Spec Tool | 實作 | 參數對齊 | 回傳對齊 | 狀態 |
|--------|-----------|------|----------|----------|------|
| 1 | `loc_list_tables` | `LocListTablesTool.cs` | ✅ 無參數 | ✅ `{tables: [{name, locales, entryCount}]}` | ✅ |
| 2 | `loc_get_entries` | `LocGetEntriesTool.cs` | ✅ `table_name`/`locale`/`filter` | ✅ `{table, locale, entries}` | ✅ |
| 3 | `loc_set_entry` | `LocSetEntryTool.cs` | ✅ `table_name`/`locale`/`key`/`value` | ✅ `{action, key, value}` | ✅ |
| 4 | `loc_set_entries` | `LocSetEntriesTool.cs` | ✅ `table_name`/`locale`/`entries[]` | ✅ `{created, updated, total}` | ✅ |
| 5 | `loc_delete_entry` | `LocDeleteEntryTool.cs` | ✅ `table_name`/`key` | ✅ `{deleted, key}` | ✅（資料層 bug，見 §B1） |
| 6 | `loc_create_table` | `LocCreateTableTool.cs` | ✅ `table_name`/`locales` + extra `directory` | ✅ `{created, name, path}` | ✅ |
| ➕ | `loc_add_locale` | `LocAddLocaleTool.cs` | — | `{action, code, path}` | 🆕 spec 之外 |

**結論：spec 100% 覆蓋。** 7th tool (`loc_add_locale`) 為合理擴充，無此 tool 則 fresh project 無法完成 bootstrap（`loc_create_table` 故意拒絕自動建 locale）。

### 1.2 實作規範（Spec §145-150）對照

| 規範 | 實作 | 證據 |
|------|------|------|
| 繼承 `McpToolBase` | ✅ | 7 支 tool 均繼承 |
| 命名前綴 `loc_` | ✅ | 與 spec 一致 |
| `EditorUtility.SetDirty` + `SaveAssets` | ✅ | `LocTableHelper.MarkDirtyAndSave` 統一處理 |
| 錯誤處理：table 不存在 | ✅ | `table_not_found` + 列出 available |
| 錯誤處理：locale 不存在 | ✅ | `locale_not_found` + 列出 available |
| 錯誤處理：key 格式不合法 | ✅ | `validation_error`（空白/前後空白） |

> 📍 **路徑差異**：spec 要求放在 `Assets/ProjectT/Editor/AIQAMCP/`（plugin 路徑），實作放在 `Editor/Tools/Localization/`（main package）。Request §1 已說明此為討論後決定的範圍升級，**符合「升級為 first-party」的修訂後共識**。

### 1.3 遺漏清單

✅ **無遺漏**。spec 中所有功能點均有對應實作。

### 1.4 未測試清單（Test Coverage Gap）

- ⚠️ `loc_add_locale` — 在 `OneTimeSetUp` 有間接呼叫，但**沒有獨立 test case**：
  - `already_exists` 路徑未測
  - `invalid_locale_code` 路徑未測
  - directory 自動建立路徑未測
- ⚠️ `loc_get_entries` 跨 locale 讀取（如 spec 中提到的 en locale 場景）未測
- ⚠️ Test Runner 環境問題未解決，**整個 test suite 未實際被 Unity Test Runner 執行過**（Request §3.3 已自承）

---

## 2. 代碼質素（Code Quality）

| 指標 | 評估 |
|------|------|
| 命名 | 🟢 一致、清楚（`Loc*Tool` / `loc_*` 對稱） |
| 重複 | 🟢 `LocTableHelper` 抽得乾淨，6/7 支 tool 都用到 |
| 抽象層級 | 🟢 適中，沒有過度設計 |
| 錯誤訊息 | 🟢 大多很有用（available 列表、key 名等），但有 1 處例外（§B2） |
| 一致性 | 🟡 與既有 `CreateScriptableObjectTool` 的 folder 建立風格不一致（§4.7） |
| 安全性 | 🟡 directory 參數沒檢查 `Assets/` 前綴（§C2） |

---

## 3. 優點（Strengths）

### 3.1 🎯 sub-assembly + versionDefines 是教科書級的 optional package 模式

`McpUnity.Localization.asmdef` 同時用 `defineConstraints` + `versionDefines` 雙重保險：

```json
"defineConstraints": ["MCP_UNITY_LOCALIZATION"],
"versionDefines": [
  { "name": "com.unity.localization", "expression": "1.0.0", "define": "MCP_UNITY_LOCALIZATION" }
]
```

效果：
- ✅ 沒裝 `com.unity.localization` 的專案：整個 assembly **不編譯**，0 影響
- ✅ 主 `McpUnity.Editor.asmdef` **完全不變**，無 conditional reference
- ✅ 透過 `DiscoverExternalTools()` 自動撿起，無需改 `RegisterTools()`

**這個模式應該寫進 CLAUDE.md 成為日後加 Cinemachine / Addressables / TextMeshPro 的官方參考。** 是這次 PR 最大的架構價值。

### 3.2 🎯 LocTableHelper 的錯誤訊息設計

```csharp
// LocTableHelper.cs:38-42
var available = string.Join(", ", collections.Select(c => c.TableCollectionName));
error = McpUnitySocketHandler.CreateErrorResponse(
    $"StringTable '{name}' not found. Available: [{available}]",
    "table_not_found");
```

當 AI agent 打錯名字時直接拿到 available list，能在下一輪自我修正而不需追加 `loc_list_tables` 呼叫。**大幅降低錯誤路徑的對話往返**。

### 3.3 🎯 set_entry vs set_entries 的 save 對稱性正確

- 單筆：每次 `SaveAssets`
- 批量：迴圈外才 `SaveAssets`

避免了「批量 100 筆 → 100 次 reimport」的災難。Request §4.6 自評正確。

### 3.4 🎯 `loc_create_table` 的 locale 警告而不自動建立

```csharp
// LocCreateTableTool.cs:79-81
warnings.Add($"Locale '{code}' is not configured in Localization Settings; skipped");
```

符合 CLAUDE.md 「顯式優於魔法」精神。AI agent 看到 warning 後會自己呼叫 `loc_add_locale`，比黑盒自動行為更可控。

### 3.5 🎯 HandleListTools 的 `McpUnity.*` 排除有清楚註解

```csharp
// McpUnitySocketHandler.cs:63-67
// Sub-assemblies prefixed "McpUnity." (e.g. McpUnity.Localization) are first-party
// extensions with hand-written TS wrappers, so they are also excluded.
```

註解直接寫出 invariant，避免日後維護者把這條 if 當垃圾代碼刪掉。

---

## 4. 缺點與風險（Issues & Risks）

### 🔴 Tier A — 必修（資料正確性 / 一致性）

#### **B1. `LocDeleteEntryTool` 只移除 SharedData，造成 orphan entries**（資料 leak）

**位置**：`Editor/Tools/Localization/LocDeleteEntryTool.cs:49`

```csharp
// 現況
collection.SharedData.RemoveKey(key);
EditorUtility.SetDirty(collection.SharedData);
foreach (var t in collection.StringTables) {
    if (t != null) EditorUtility.SetDirty(t);
}
AssetDatabase.SaveAssets();
```

**問題**：`SharedTableData.RemoveKey(key)` 只從 SharedData 的 entry dict 移除 key，但**不會清掉每個 `StringTable.Values` 裡對應的 `StringTableEntry`**。結果：

1. `loc_get_entries` 看不到該 key（因為 `sharedEntry == null`）✅ 表面正確
2. 但每個 locale 的 `.asset` 檔案裡仍殘留 orphan `StringTableEntry`（綁在已被移除的 keyId 上）❌
3. 在 Unity Editor 重 import / 切換 branch 後，這些 orphan 不會被 GC，會永遠變成 dead weight

**修正**：在 `RemoveKey` 之前，**先 iterate 每個 StringTable 呼叫 `RemoveEntry(keyId)`**。

```csharp
// 修正版
var sharedEntry = collection.SharedData.GetEntry(key);
if (sharedEntry == null) {
    return /* deleted=false 軟失敗 */;
}

long keyId = sharedEntry.Id;

// 1. 先清掉每個 locale 的 entry，避免 orphan
foreach (var t in collection.StringTables) {
    if (t == null) continue;
    if (t.GetEntry(keyId) != null) {
        t.RemoveEntry(keyId);
    }
    EditorUtility.SetDirty(t);
}

// 2. 再從 SharedData 移除 key
collection.SharedData.RemoveKey(key);
EditorUtility.SetDirty(collection.SharedData);

AssetDatabase.SaveAssets();
```

**驗證方式**：建一個 table、set 3 個 locale 的 value、delete key、用 YAML editor 直接打開 `.asset` 檔，確認 `m_TableEntries` 不再有對應 entry。

---

#### **B2. `LocSetEntriesTool` 重新包裝 ValidateKey 錯誤訊息，丟失原始細節**

**位置**：`Editor/Tools/Localization/LocSetEntriesTool.cs:73-78`

```csharp
// 現況
if (!LocTableHelper.ValidateKey(key, out error))
{
    return McpUnitySocketHandler.CreateErrorResponse(
        $"entries[{i}]: invalid key",  // ⚠️ 丟失了 "Key 'foo ' has leading/trailing whitespace"
        "validation_error");
}
```

**問題**：`ValidateKey` 已經 fill 了一個訊息明確的 `error` JObject（例如 `"Key 'foo ' has leading/trailing whitespace"`），但這裡用一個更模糊的 `entries[i]: invalid key` 把它覆蓋掉。AI agent 看到 `entries[3]: invalid key` 不知道是 null、空字串、還是前後空白。

**修正**：保留 inner error 的 message，只附加 index 前綴。

```csharp
// 修正版
if (!LocTableHelper.ValidateKey(key, out var innerError))
{
    string innerMessage = innerError?["error"]?["message"]?.ToString() ?? "invalid key";
    return McpUnitySocketHandler.CreateErrorResponse(
        $"entries[{i}]: {innerMessage}",
        "validation_error");
}
```

> **配套**：請先檢查 `McpUnitySocketHandler.CreateErrorResponse` 的 JSON 結構，把 `["error"]?["message"]` 換成實際的 path（可能是 `["message"]`）。

---

#### **B3. `LocSetEntriesTool` 部分失敗時 in-memory 狀態被污染**

**位置**：`Editor/Tools/Localization/LocSetEntriesTool.cs:60-83`

**問題**：批量 100 筆中第 50 筆 key invalid，前面 49 筆已經呼叫過 `LocSetEntryTool.SetEntry` 修改了 `collection.SharedData`（呼叫過 `AddKey`）和 `table` 的 in-memory state。但因為直接 `return error`，**`MarkDirtyAndSave` 沒被呼叫**。

後果：
- ✅ 磁碟上的 `.asset` 沒被改 — 表面安全
- ❌ Unity Editor session 內 `LocalizationEditorSettings` 的 in-memory state 被污染 — 後續同 session 內的 `loc_get_entries` 會看到「幽靈 keys」
- ❌ 使用者若手動點 `Ctrl+S` 或之後其他 tool 觸發 `SaveAssets`，這些 partial change 會被持久化

**修正策略**（兩選一）：

**選項 A — 預先驗證所有 entries（推薦，行為最可預測）**：

```csharp
// 在進迴圈前先 validate 全部
for (int i = 0; i < entriesArray.Count; i++) {
    var entry = entriesArray[i] as JObject;
    if (entry == null) return /* validation_error */;

    string k = entry["key"]?.ToString();
    if (!LocTableHelper.ValidateKey(k, out var innerError)) {
        return /* entries[i]: invalid key */;
    }
}

// 全部通過才進實際的 SetEntry 迴圈
int created = 0, updated = 0;
for (int i = 0; i < entriesArray.Count; i++) {
    /* 原本的 SetEntry 邏輯 */
}
```

**選項 B — 保留 partial-apply 但記錄 success/failure list**：

回傳 `{ created, updated, failed: [{index, key, reason}] }`，讓 AI agent 知道哪幾筆沒進。但這會偏離 spec §93-100 的回傳結構。

**建議採用選項 A**，符合 spec 「all-or-nothing」的隱含預期。

---

### 🟡 Tier B — 強烈建議（一致性 / 可維護性）

#### **C1. `loc_create_table` 的 directory 風格與既有 tool 不一致**

**位置**：`Editor/Tools/Localization/LocCreateTableTool.cs:93-97` 與 `LocAddLocaleTool.cs:74-78`

```csharp
// 現況
if (!AssetDatabase.IsValidFolder(dir))
{
    Directory.CreateDirectory(dir);
    AssetDatabase.Refresh();
}
```

**對比**：`Editor/Tools/CreateScriptableObjectTool.cs:226-241` 已有 `CreateFolderRecursively`，全程透過 `AssetDatabase.CreateFolder`：

```csharp
private void CreateFolderRecursively(string folderPath)
{
    string[] parts = folderPath.Split('/');
    string currentPath = parts[0]; // "Assets"
    for (int i = 1; i < parts.Length; i++) {
        string parentPath = currentPath;
        currentPath = currentPath + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(currentPath)) {
            AssetDatabase.CreateFolder(parentPath, parts[i]);
        }
    }
}
```

**為什麼重要**：
- `Directory.CreateDirectory` 是 .NET 層的 raw filesystem call，**繞過 Unity AssetDatabase**。雖然之後 `AssetDatabase.Refresh()` 會掃進來，但這是兩步驟，期間 `.meta` 檔尚未生成
- `AssetDatabase.CreateFolder` 會原子性地建 folder + 對應 `.meta`，並寫入 GUID database
- 不一致會增加維護成本，後人來修這段時不知道該選哪個

**修正**：把 `LocTableHelper` 加一個共用 helper：

```csharp
// LocTableHelper.cs 新增
public static void EnsureFolderExists(string folderPath)
{
    if (AssetDatabase.IsValidFolder(folderPath)) return;

    string[] parts = folderPath.Split('/');
    string currentPath = parts[0]; // "Assets"
    for (int i = 1; i < parts.Length; i++) {
        string parentPath = currentPath;
        currentPath = currentPath + "/" + parts[i];
        if (!AssetDatabase.IsValidFolder(currentPath)) {
            AssetDatabase.CreateFolder(parentPath, parts[i]);
        }
    }
}
```

然後 `LocCreateTableTool` 與 `LocAddLocaleTool` 都改用：

```csharp
LocTableHelper.EnsureFolderExists(dir);
```

順便消滅 `using System.IO` 對 raw `Directory.CreateDirectory` 的依賴。

---

#### **C2. `directory` 參數沒檢查 `Assets/` 前綴**

**位置**：`LocCreateTableTool.cs:92`、`LocAddLocaleTool.cs:73`

**問題**：使用者若傳 `directory: "/tmp/foo"` 或 `directory: "../OutsideAssets"`：
- `Directory.CreateDirectory` 會建在 filesystem 上（跳出 project root）
- `AssetDatabase.Refresh` 找不到 → `CreateStringTableCollection(name, dir, locales)` 行為未定義
- AI agent 完全不知道發生了什麼

**修正**：在 helper 加 guard。

```csharp
// LocTableHelper.cs 新增
public static bool ValidateAssetPath(string dir, out JObject error)
{
    error = null;
    if (string.IsNullOrWhiteSpace(dir)) return true; // 預設值會處理

    if (!dir.StartsWith("Assets/") && dir != "Assets")
    {
        error = McpUnitySocketHandler.CreateErrorResponse(
            $"Directory '{dir}' must be inside the Assets folder",
            "validation_error");
        return false;
    }
    return true;
}
```

呼叫端：

```csharp
if (!LocTableHelper.ValidateAssetPath(directory, out var pathError)) return pathError;
```

---

#### **C3. `LocAddLocaleTool` 對無效 locale code 沒有預檢**

**位置**：`Editor/Tools/Localization/LocAddLocaleTool.cs:65-71`

Request §4.10 已自承這點。`Locale.CreateLocale(new LocaleIdentifier("xx-NOSUCH"))` 不會 return null，會建出一個 fallback locale。使用者打錯字 → 沒 warning → 一個垃圾 locale 被加入 settings。

**修正**：用 `CultureInfo.GetCultureInfo` 預檢。

```csharp
using System.Globalization;

// 在 CreateLocale 之前
try {
    CultureInfo.GetCultureInfo(code);
} catch (CultureNotFoundException) {
    return McpUnitySocketHandler.CreateErrorResponse(
        $"Locale code '{code}' is not a valid culture identifier (e.g. 'zh-TW', 'en', 'ja')",
        "invalid_locale_code");
}

var locale = Locale.CreateLocale(identifier);
```

> ⚠️ 注意：某些非標準但合法的 Unity locale code（如 `zh-Hant`）可能不在 `CultureInfo` 列表中。若預檢過嚴會擋掉合法 case。建議用 `try/catch` + 如果想保留彈性，也可改成「失敗時記 warning 但繼續建」。

---

#### **C4. `LocCreateTableTool` 的 locale 比對方式與其他地方不一致**

**位置**：`LocCreateTableTool.cs:73` vs `LocAddLocaleTool.cs:50`

```csharp
// LocCreateTableTool: string 比較
var locale = availableLocales.FirstOrDefault(l => l.Identifier.Code == code);

// LocAddLocaleTool: identifier 比較
var existing = LocalizationEditorSettings.GetLocales()
    .FirstOrDefault(l => l.Identifier == identifier);
```

`LocaleIdentifier` 是個 struct，包含 `Code` + `CultureInfo`。`.Equals` 預設比 `Code`，但兩種寫法在 edge case（如 `zh-TW` vs `zh-Hant-TW`）行為可能不同。

**修正**：統一用 `Identifier.Code` 比對（更明確、不依賴 struct equality 細節）。建議在 `LocTableHelper` 加：

```csharp
public static Locale FindLocale(string code)
{
    return LocalizationEditorSettings.GetLocales()
        .FirstOrDefault(l => l.Identifier.Code == code);
}
```

兩處都改用 `LocTableHelper.FindLocale(code)`。

---

#### **C5. `loc_set_entry` description 沒勸退單筆迴圈濫用**

Request §4.6 自承。AI agent 在 spec 不熟時容易寫 100 次 single-entry call，比 batch 慢 100 倍。

**修正**：改 description 一句話。

```csharp
// LocSetEntryTool.cs:14
Description = "Sets a Unity Localization StringTable entry value. Creates the key if it does not exist. Supports TMP RichText. For batches of >5 entries, prefer loc_set_entries (single SaveAssets at the end).";
```

對應 TS wrapper `localizationTools.ts:105` 同步修正。

---

### 🟢 Tier C — 可選（防禦性 / 文檔）

#### **D1. HandleListTools 的 `McpUnity.*` prefix 排除應加 attribute-based 替代**

Request §4.2 自承。目前是 prefix match，雖然有註解但仍是個約定。

**建議**：加一個 marker attribute。

```csharp
// Editor/Tools/McpUnityFirstPartyAttribute.cs（新檔）
[AttributeUsage(AttributeTargets.Class)]
public class McpUnityFirstPartyAttribute : Attribute { }
```

```csharp
// 7 支 Loc tool 各加標記
[McpUnityFirstParty]
public class LocCreateTableTool : McpToolBase { ... }
```

```csharp
// McpUnitySocketHandler.cs HandleListTools
var type = kvp.Value.GetType();
if (type.Assembly == mcpAssembly ||
    type.GetCustomAttribute<McpUnityFirstPartyAttribute>() != null)
    continue;
```

**好處**：不依賴 assembly 命名約定，未來若有人寫 plugin 叫 `McpUnity.SomethingExternal` 也不會被誤殺。

**權衡**：但 prefix match 也可接受，只要在 README 的 "External Tool Plugin System" 段落明確聲明 `McpUnity.*` 是保留命名。**至少必須加上文檔聲明**。

---

#### **D2. `LocCreateTableTool` 回傳 path 的補充欄位**

Request §4.3 自評過度自我批評了 — **回傳 SharedData path 其實符合 spec §139** 的範例（spec 也是回 `... Shared Data.asset`）。

但若想更完整，可選擇追加 `tablePaths` array：

```csharp
var tablePaths = new JArray();
foreach (var t in collection.StringTables) {
    if (t != null) tablePaths.Add(AssetDatabase.GetAssetPath(t));
}

result["tablePaths"] = tablePaths;
```

不破壞既有契約，純加欄位。**這個是 nice-to-have，不是必修**。

---

#### **D3. 補 `loc_add_locale` 的獨立 test cases**

```csharp
// Editor/Tests/Localization/LocTests.cs 新增

[Test]
public void AddLocale_AlreadyExists_ReturnsAlreadyExistsAction()
{
    var tool = new LocAddLocaleTool();
    var result = tool.Execute(new JObject
    {
        ["code"] = TestLocaleCode,
        ["directory"] = TestDir
    });

    AssertSuccess(result);
    Assert.AreEqual("already_exists", result.Value<string>("action"));
}

[Test]
public void AddLocale_InvalidCode_ReturnsValidationError()
{
    var result = new LocAddLocaleTool().Execute(new JObject
    {
        ["code"] = "xx-NOSUCH-LOCALE",
        ["directory"] = TestDir
    });

    // 配合 §C3 修正後預期回 invalid_locale_code
    AssertError(result, "invalid_locale_code");
}

[Test]
public void AddLocale_MissingCode_ReturnsValidationError()
{
    var result = new LocAddLocaleTool().Execute(new JObject());
    AssertError(result, "validation_error");
}
```

---

#### **D4. 補 `loc_delete_table` follow-up tool**

Request §4.8 自承。實務上 AI agent 會需要清理。建議在 v1.10.0 補上：

```csharp
public class LocDeleteTableTool : McpToolBase
{
    public LocDeleteTableTool()
    {
        Name = "loc_delete_table";
        Description = "Deletes a Unity Localization StringTable collection (removes SharedTableData and all locale tables)";
    }

    public override JObject Execute(JObject parameters)
    {
        var collection = LocTableHelper.ResolveCollection(
            parameters["table_name"]?.ToString(), out var error);
        if (collection == null) return error;

        LocalizationEditorSettings.RemoveCollection(collection);
        AssetDatabase.SaveAssets();

        return new JObject {
            ["success"] = true,
            ["type"] = "text",
            ["message"] = $"Deleted StringTable collection '{collection.TableCollectionName}'",
            ["deleted"] = true
        };
    }
}
```

對應補上 `loc_remove_locale` 對稱。

---

#### **D5. CLAUDE.md 補上 sub-assembly 章節**

Request §6 自承。建議在 CLAUDE.md 的 "Adding an External Tool" 之後新增 "Adding a First-Party Optional Package Tool"：

```markdown
## Adding a First-Party Optional Package Tool

When integrating with an optional Unity package (e.g. com.unity.localization, com.unity.cinemachine), use a sub-assembly with version-gated compilation rather than conditional code in the main assembly.

### 1. Create sub-assembly directory
`Editor/Tools/{Feature}/McpUnity.{Feature}.asmdef`:

\`\`\`json
{
  "name": "McpUnity.{Feature}",
  "rootNamespace": "McpUnity.Tools.{Feature}",
  "references": ["McpUnity.Editor", "{Package.Assembly}", "{Package.Editor.Assembly}"],
  "includePlatforms": ["Editor"],
  "defineConstraints": ["MCP_UNITY_{FEATURE}"],
  "versionDefines": [
    { "name": "{com.unity.package}", "expression": "1.0.0", "define": "MCP_UNITY_{FEATURE}" }
  ]
}
\`\`\`

### 2. Tools auto-register via DiscoverExternalTools
No need to modify `RegisterTools()` — sub-assembly tools are picked up by reflection.

### 3. HandleListTools excludes `McpUnity.*` assemblies
First-party sub-assemblies must ship hand-written TS wrappers in `Server~/src/tools/`. They are NOT exposed via dynamic registration.

### 4. Test assembly mirrors the same gate
`Editor/Tests/{Feature}/McpUnity.{Feature}.Tests.asmdef` uses identical `defineConstraints` + `versionDefines`.
```

---

#### **D6. `doc/lessons/unity-mcp-lessons.md` 補三條教訓**

Request §6 自承：
1. `Locale.CreateLocale` 必須走 factory 不能用 `create_scriptable_object` 直接寫
2. `recompile_scripts` 不會主動 `AssetDatabase.Refresh`，新檔案必須先 `Assets/Refresh`
3. `run_tests` 在某些環境會回 0/0 — 環境問題待解

---

## 5. 改善建議 — 優先級總覽

| # | 級別 | 項目 | 工作量 | 影響 |
|---|------|------|--------|------|
| B1 | 🔴 必修 | DeleteEntry 的 orphan entry leak | S | 資料正確性 |
| B2 | 🔴 必修 | SetEntries 錯誤訊息覆蓋 | XS | 可觀測性 |
| B3 | 🔴 必修 | SetEntries 部分失敗污染 in-memory | S | 一致性 |
| C1 | 🟡 強建議 | folder 建立風格不一致 | S | 一致性 |
| C2 | 🟡 強建議 | directory `Assets/` 前綴檢查 | XS | 安全 |
| C3 | 🟡 強建議 | locale code 預檢 | XS | 防呆 |
| C4 | 🟡 強建議 | locale 比對統一 | XS | 一致性 |
| C5 | 🟡 強建議 | set_entry description 勸退 | XS | 文檔 |
| D1 | 🟢 可選 | first-party attribute 替代 prefix | M | 可維護性 |
| D2 | 🟢 可選 | 回傳 tablePaths 補充 | XS | 完整性 |
| D3 | 🟢 可選 | add_locale test cases | S | 測試 |
| D4 | 🟢 可選 | loc_delete_table follow-up | M | 功能 |
| D5 | 🟢 可選 | CLAUDE.md sub-assembly 章節 | S | 文檔 |
| D6 | 🟢 可選 | unity-mcp-lessons 三條 | XS | 文檔 |

---

## 6. 對 Request 各審查請求的逐項回應

| Request 章節 | 我的判斷 |
|--------------|----------|
| §4.1 `loc_add_locale` 命名 / 範圍擴充 | ✅ 同意擴充。命名 `loc_add_locale` OK，無需改 `register/init`。建議補對稱的 `loc_remove_locale`（§D4）|
| §4.2 `McpUnity.*` prefix 排除 | 🟡 可接受但**必須加 README 聲明**。長期建議改 attribute（§D1）|
| §4.3 `path` 回傳結構 | ✅ 已符合 spec 範例，無需改。可選擇加 `tablePaths`（§D2）|
| §4.4 Addressables 整合 | 🟢 接受目前狀態，加 known-issues 註記即可 |
| §4.5 Test Runner 問題 | ✅ 不阻擋合併。但 §B1-B3 的修正應該補完 test 並嘗試手動跑一次 |
| §4.6 set_entry vs set_entries description | ✅ 必修文檔（§C5）|
| §4.7 directory 風格 | ✅ 必修一致性（§C1）|
| §4.8 `loc_delete_table` | ✅ 同意 follow-up（§D4）|
| §4.9 測試 asmdef 隱性耦合 | 🟢 可接受。風險低，註解已說明 |
| §4.10 invalid locale code | ✅ 必修（§C3）|

---

## 7. Refactor Prompt（請複製給實作者）

> **以下是給 Claude / 實作者執行的 prompt，覆蓋本次審查的全部必修項與強建議項。**

---

請依照 `doc/codeReview/Response_20260413_LocalizationTools.md` 修正 v1.9.0 Localization Tool Suite 的 7 個問題。請按優先順序執行：

### 🔴 Phase 1 — 必修（v1.9.1 patch，必須全部完成）

1. **修正 `Editor/Tools/Localization/LocDeleteEntryTool.cs:49` 的 orphan entry leak**
   - 在 `SharedData.RemoveKey(key)` 之前，先 iterate `collection.StringTables`，對每個非 null table 呼叫 `t.RemoveEntry(sharedEntry.Id)` 並 `SetDirty(t)`
   - 參考 §B1 的修正版 code

2. **修正 `Editor/Tools/Localization/LocSetEntriesTool.cs:73-78` 的錯誤訊息覆蓋**
   - 從 `ValidateKey` 的 inner error 取出 message，附加 `entries[i]:` 前綴後回傳
   - 參考 §B2

3. **修正 `Editor/Tools/Localization/LocSetEntriesTool.cs` 的部分失敗污染**
   - 採用 §B3 選項 A：先全量 validate（pre-flight check），再進實際的 SetEntry 迴圈
   - 確保 batch 是 all-or-nothing semantic

### 🟡 Phase 2 — 強建議（v1.9.1 patch，盡量完成）

4. **在 `LocTableHelper.cs` 加兩個 helper**：
   - `EnsureFolderExists(string folderPath)` — 走 `AssetDatabase.CreateFolder` 而非 `Directory.CreateDirectory`，參考 §C1
   - `ValidateAssetPath(string dir, out JObject error)` — 拒絕非 `Assets/` 前綴的 path，參考 §C2
   - `FindLocale(string code)` — 統一 locale 比對方式，參考 §C4
   - 把 `LocCreateTableTool` 與 `LocAddLocaleTool` 改用這三個 helper，刪掉 `using System.IO`

5. **修正 `LocAddLocaleTool.cs:65-71` 的 invalid locale code 預檢**
   - 在 `Locale.CreateLocale` 之前用 `CultureInfo.GetCultureInfo(code)` try/catch
   - 失敗時回 `invalid_locale_code` error
   - 參考 §C3

6. **改 `LocSetEntryTool.cs:14` 與 `Server~/src/tools/localizationTools.ts:105` 的 description**
   - 加上 `For batches of >5 entries, prefer loc_set_entries`
   - 參考 §C5

### 🟢 Phase 3 — 可選（v1.10.0 follow-up）

7. **補 `Editor/Tests/Localization/LocTests.cs` 的 `loc_add_locale` 三個 test case**（參考 §D3）
8. **新增 `Editor/Tools/Localization/LocDeleteTableTool.cs` 與 `LocRemoveLocaleTool.cs`**（參考 §D4）
9. **加 `[McpUnityFirstParty]` attribute 取代 prefix match**（參考 §D1）
10. **更新 `CLAUDE.md` 加 "Adding a First-Party Optional Package Tool" 章節**（參考 §D5）
11. **更新 `doc/lessons/unity-mcp-lessons.md` 三條教訓**（參考 §D6）
12. **更新 `README.md` 的 External Tool Plugin System 段落聲明 `McpUnity.*` 是保留命名空間**

### 驗證要求

每完成一階段：
1. `cd Server~ && npm run build` — TS 必須 clean
2. `recompile_scripts` — C# 0 warning, 0 error
3. **必須**手動跑一次 batch_execute 等價驗證（cover Phase 1 三個必修項的修正點）：
   - DeleteEntry：建 table → set 1 entry → delete → 直接讀 `.asset` YAML 確認 `m_TableEntries` 已清空
   - SetEntries error message：故意傳前後空白 key，確認錯誤 message 包含 `"has leading/trailing whitespace"`
   - SetEntries pre-flight：故意傳 `[valid, invalid, valid]`，確認**沒有**任何 entry 被 partial apply（用 `loc_get_entries` 確認）
4. **完成後執行 `verification-loop` skill** 確保整體仍然 green

### 其他

- 修正後請更新 `CHANGELOG.md` 加 v1.9.1 條目
- 若有完成 Phase 3，bump version 到 1.10.0
- 修正完請更新 `doc/implementation-tracker/` 對應 tracker（如果有的話），標記哪些審查項已完成
- 不要動 `e46ab7f` 的歷史，直接在 main 上開新 commit

---

## 8. 結論

這是一次**架構優秀、實作完成度高**的 PR。sub-assembly + versionDefines 模式值得寫進 CLAUDE.md 成為 mcp-unity 處理 optional package 的官方範式。

**主要 risk 集中在 3 個資料層 bug**（B1/B2/B3），都不是設計問題，是邊界處理疏漏。修完後 v1.9.1 可達 production-ready。

需求覆蓋率 100%，spec 之外的 `loc_add_locale` 是合理擴充。Test Runner 環境問題不阻擋合併，但 §B1-B3 修完後請務必手動驗證一次。
