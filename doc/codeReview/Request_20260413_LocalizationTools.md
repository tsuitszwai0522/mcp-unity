# Code Review Request — Unity Localization MCP Tool Suite

- **日期**: 2026-04-13
- **Commit**: `e46ab7f`
- **版本**: v1.8.2 → v1.9.0
- **規模**: 32 files changed, +1828 / −3
- **Branch**: `main`（已 push）
- **需求文件**: `doc/requirement/feature_localization_mcp.md`

---

## 1. 背景與目標

需求文件原本只描述 6 支 tools 給 ProjectT 下游專案使用（plugin 路徑），但討論後決定**升級為本體 first-party 功能**，理由是 Unity Localization 是官方解決方案，採用率高，AI workflow（寫 tooltip / 文案 / 敘事）受益巨大。

關鍵約束：
- **不可硬依賴** `com.unity.localization` package — 沒裝該 package 的使用者必須零影響
- `loc_create_table` 遇到未配置的 locale **只警告不自動建立**（顯式優於魔法）
- 維持與既有 tool 註冊 / list_tools / 動態註冊路徑的一致性

---

## 2. 設計決策摘要

### 2.1 子 assembly 條件編譯

在 `Editor/Tools/Localization/` 建立獨立的 `McpUnity.Localization.asmdef`：

```json
{
  "references": ["McpUnity.Editor", "Unity.Localization", "Unity.Localization.Editor"],
  "defineConstraints": ["MCP_UNITY_LOCALIZATION"],
  "versionDefines": [
    { "name": "com.unity.localization", "expression": "1.0.0", "define": "MCP_UNITY_LOCALIZATION" }
  ]
}
```

效果：當 `com.unity.localization >= 1.0.0` 存在時，`MCP_UNITY_LOCALIZATION` symbol 被定義，這個 assembly 才會編譯；否則整個目錄被 Unity 跳過，主 `McpUnity.Editor` asmdef 完全不需要動。

### 2.2 註冊路徑：`HandleListTools` 的 `McpUnity.*` 排除

子 assembly 的 tools 是透過既有的 `DiscoverExternalTools()` 反射機制**自動撿起**的，不用改 `RegisterTools()`。但這帶來一個問題：`HandleListTools` 原本會把所有非 `McpUnity.Editor` assembly 的 tools 回傳給 Node side 做 dynamic registration，導致跟手寫的 TS wrapper 撞名衝突。

解法（`Editor/UnityBridge/McpUnitySocketHandler.cs:64-70`）：
```csharp
var asmName = kvp.Value.GetType().Assembly.GetName().Name;
if (kvp.Value.GetType().Assembly == mcpAssembly || asmName.StartsWith("McpUnity."))
    continue;
```

**保留 `McpUnity.*` 命名空間給 first-party 子 assembly。**

### 2.3 共用 helper

`LocTableHelper`（`Editor/Tools/Localization/LocTableHelper.cs`）統一處理：
- `ResolveCollection(name)` — 找不到時錯誤訊息會列出所有 available tables
- `ResolveTable(collection, locale)` — 預設 `zh-TW`；找不到 locale 列出該 collection 的所有 locales
- `ValidateKey` — 拒絕空字串、純空白、前後空白
- `MarkDirtyAndSave(table)` — SetDirty `table` + `SharedData`，再 SaveAssets

### 2.4 Tool 清單（共 7 支，比 spec 多 1 支）

| Tool | C# | TS Wrapper | 備註 |
|------|----|-----------|------|
| `loc_list_tables` | `LocListTablesTool.cs` | ✅ | 無參數 |
| `loc_get_entries` | `LocGetEntriesTool.cs` | ✅ | 支援 prefix filter |
| `loc_set_entry` | `LocSetEntryTool.cs` | ✅ | 自動分辨 created/updated |
| `loc_set_entries` | `LocSetEntriesTool.cs` | ✅ | 一次 SaveAssets |
| `loc_delete_entry` | `LocDeleteEntryTool.cs` | ✅ | key 不存在回 `deleted:false`（軟失敗） |
| `loc_create_table` | `LocCreateTableTool.cs` | ✅ | 無效 locale → warning array |
| `loc_add_locale` | `LocAddLocaleTool.cs` | ✅ | **超出原 spec**，見 §4.1 |

---

## 3. 驗證情況

### 3.1 編譯
- C# 端 `recompile_scripts` 多次，**0 warning, 0 error**
- Node 端 `npm run build`，**clean**

### 3.2 端對端功能驗證（透過 `batch_execute`）
跑了 **18 個 batch operations**：15 個 happy path + 3 個預期錯誤路徑，**全部結果符合設計**。涵蓋：
- Locale bootstrap（zh-TW + en）
- Table 建立 / 列表
- Entry create / update / RichText / 批量
- 全量 / filter 讀取
- Delete 既有 / 不存在
- 跨 locale 讀取（en locale 回傳 SharedData keys + 空 value，符合 Unity Localization 設計）
- 錯誤路徑：missing table、duplicate table、invalid locale

### 3.3 NUnit 測試 ⚠️
- 寫了 `Editor/Tests/Localization/LocTests.cs`（398 行，20 個 test cases，涵蓋 ordered scenario + 獨立 error paths）
- **問題**：在當前 Unity 環境中，`run_tests` 對所有 filter（包括既有的 `MaterialToolsTests`）都回 `0/0 passed`。**這不是新測試的問題**，是 Test Runner 環境/狀態問題
- 因此 NUnit 測試碼**未實際被 Test Runner 執行過**，僅靠編譯與 §3.2 的 batch_execute 等價驗證

---

## 4. 自我評估：脆弱點與 Edge Cases

### 4.1 ⚠️ 偏離原 spec：新增 `loc_add_locale`

- 原 spec 是 6 支 tools，這次交付了 7 支
- `loc_add_locale` 是必要的 bootstrap 工具：`loc_create_table` 故意不自動建 locale（依用戶要求），但新專案 / 測試環境必須有方法把 Locale 註冊到 `LocalizationEditorSettings`，否則整套無法初次使用
- **判斷**：這不是「自動建 locale 的魔法」，而是「使用者顯式要求建 locale 的明確 tool」，符合 spec 精神
- **審查者請評估**：
  - 是否同意這個範圍擴充？
  - tool 名稱 `loc_add_locale` 是否清楚？或要叫 `loc_register_locale` / `loc_init_locale`？
  - 是否該也補一個 `loc_remove_locale` 對稱？

### 4.2 ⚠️ `HandleListTools` 行為改動的影響面

```csharp
if (... || asmName.StartsWith("McpUnity."))
```

- **正面**：解決 first-party 子 assembly 雙註冊的問題
- **風險**：如果有任何使用者把自己的 plugin assembly 命名為 `McpUnity.X`（雖然命名上很奇怪），他們的 tools 會突然從 `list_tools` 消失，導致 dynamic registration 失敗
- **緩解**：`McpUnity.` 命名空間在語意上已經是 first-party 保留，使用者沒有理由用這個前綴。但這是個 breaking convention，沒有寫進文檔
- **審查者請評估**：
  - 要不要在 README 的 "External Tool Plugin System" 段落明確聲明 `McpUnity.*` 是保留命名？
  - 或改用更明確的標記（例如 `[McpUnityFirstParty]` attribute）取代 prefix match？

### 4.3 ⚠️ `LocCreateTableTool.path` 回傳不完整

```csharp
string assetPath = AssetDatabase.GetAssetPath(collection.SharedData);
```

- Unity Localization 的一個 collection 實際上會產生**多個 asset**：1 個 SharedTableData + N 個 StringTable（每個 locale 一個）
- 我們只回 SharedData 的路徑，使用者看到 `path` 會誤以為「只有一個檔案」
- **緩解**：訊息文字裡有列出 locales，但結構化 `path` 欄位是誤導性的
- **審查者請評估**：要不要改成回傳 array，或加 `tablePaths` 欄位？

### 4.4 ⚠️ `loc_add_locale` 沒處理 Addressables 整合

- `LocalizationEditorSettings.AddLocale` 在某些版本會自動把 Locale 加入 Addressables group
- 我沒測試過 Addressables 是否乾淨配置；只測了「addLocale 完成 → loc_create_table 看得到」這條路徑
- **風險**：使用者裝了 Addressables package 時，可能會遇到非預期的 group 變動或建置警告
- **緩解**：使用者目前用的是 Unity Localization 1.5.0，預設應該 OK。但這是未驗證假設
- **審查者請評估**：要不要加 known-issues 註記？

### 4.5 ⚠️ Test Runner 環境問題未解決

- `run_tests` 在當前環境回 0 tests，連既有 `MaterialToolsTests` 都跑不出來
- 這跟我這次的改動無關，但代表 **CI / 自動化驗證對 Localization tools 是失效的**
- 我用 `batch_execute` 跑等價端對端驗證做替代，但這不是 reproducible artifact
- **審查者請評估**：要不要先 debug Test Runner 環境問題，再合併這個 PR？或接受目前狀態先合併？

### 4.6 ⚠️ `loc_set_entry` 與 `loc_set_entries` 的 Save 對稱性

- `loc_set_entry`（單筆）：每次呼叫都 `MarkDirtyAndSave`
- `loc_set_entries`（批量）：迴圈內只 `SetEntry`，迴圈外才 `MarkDirtyAndSave`
- **正確**：批量版本明顯快很多
- **隱性對比**：使用者連續 call 100 次 `loc_set_entry` vs 一次 `loc_set_entries` 100 筆，前者會慢 100 倍以上（每次 `SaveAssets` 都會觸發 reimport）
- 文檔 / description 沒有強調這點
- **審查者請評估**：是否在 `loc_set_entry` 的 description 加一句「For >5 entries, prefer loc_set_entries」？

### 4.7 ⚠️ `loc_create_table` 的 directory 處理

```csharp
if (!AssetDatabase.IsValidFolder(dir)) {
    Directory.CreateDirectory(dir);
    AssetDatabase.Refresh();
}
```

- 用 `System.IO.Directory.CreateDirectory` + `AssetDatabase.Refresh`，跟 `CreateSceneTool` 用的 `AssetDatabase.CreateFolder` 風格不一致
- **後果**：`AssetDatabase.CreateFolder` 會處理 `.meta` 生成，原生 `Directory.CreateDirectory` 不會，需要 Refresh 才會被 Unity 掃進來。我有 Refresh 所以理論上一致，但這是個踩雷點
- **審查者請評估**：建議改用 `CreateFolderRecursively` 模式？

### 4.8 ⚠️ 沒有 `loc_delete_table`

- 一旦建立 StringTable collection，目前**只能用 Unity Editor 手動刪**
- 我自己測試時就遺留了 `McpLocTestTable`、Locale_zh-TW、Locale_en 三個 asset 在 repo 裡（已徵得用戶同意保留作為 seed）
- spec 沒列這個 tool，但實務上 AI agent 會需要
- **審查者請評估**：要不要當作 follow-up 補上？

### 4.9 NUnit 測試的隱性耦合

`McpUnity.Localization.Tests.asmdef` 同時 reference `McpUnity.Localization`，且兩者都 gated by `MCP_UNITY_LOCALIZATION`。如果有人改了主 asmdef 的 versionDefines symbol 名而忘記改測試 asmdef，會無聲地讓測試永遠不被編譯。

**緩解**：兩個 asmdef 的 symbol 名相同 (`MCP_UNITY_LOCALIZATION`)，且寫在 CHANGELOG / 註解裡，但沒有 enforced linkage。

### 4.10 `Locale.CreateLocale` 對未知 locale code 的行為

`loc_add_locale` 把 `Locale.CreateLocale(new LocaleIdentifier(code))` 當成 happy path，沒處理：
- code 是 `xx-NOSUCH` 這種格式正確但沒對應 CultureInfo 的 case — Unity 會建出一個 fallback locale，**不會丟錯**
- 我有檢查 `locale == null` 但實際上幾乎不會 null
- **風險**：使用者打錯字 → 沒有 warning → 一個垃圾 locale 被加入 settings

**緩解建議**：加一個 `CultureInfo.GetCultureInfo(code)` try/catch 預檢，若失敗回 warning。

---

## 5. 審查重點請求

請審查者特別關注以下面向：

### 5.1 架構面
- [ ] **§4.2** `HandleListTools` 的 `McpUnity.*` prefix 排除是否合理？或建議改用 attribute-based 標記？
- [ ] **§4.5** 是否應該先修 Test Runner 環境再合併？
- [ ] **§2.1** sub-assembly + versionDefines 是否是處理 optional package 的長期可維護模式？日後加 Cinemachine / Addressables / TextMeshPro 等都會走這條路嗎？

### 5.2 API 面
- [ ] **§4.1** `loc_add_locale` 命名與是否該補 `loc_remove_locale`
- [ ] **§4.3** `loc_create_table` 回傳 path 結構
- [ ] **§4.6** `loc_set_entry` description 是否該勸退單筆迴圈用法
- [ ] **§4.8** `loc_delete_table` 是否為必要 follow-up
- [ ] **§4.10** `loc_add_locale` 對無效 locale code 的容錯

### 5.3 一致性面
- [ ] **§4.7** `loc_create_table` 的 folder 建立風格與 `CreateSceneTool` 不一致，建議統一
- [ ] 錯誤類型字串（`table_not_found`, `locale_not_found`, `duplicate_table`, `no_valid_locales`, `invalid_locale_code`, `validation_error`）— 命名是否符合既有專案慣例？

### 5.4 文檔面
- [ ] CHANGELOG 1.9.0 條目完整性
- [ ] README "Unity Localization Tools" 段落清楚程度
- [ ] CLAUDE.md 是否需要補上 sub-assembly + versionDefines 的開發指南？

---

## 6. 文檔一致性檢查

| 項目 | 狀態 | 備註 |
|------|------|------|
| `CHANGELOG.md` 1.9.0 entry | ✅ | 列出 7 tools + sub-assembly pattern + tests + HandleListTools 改動 |
| `README.md` Unity Localization Tools 段落 | ✅ | 7 支 tools 各一段帶 example prompt |
| `package.json` version bump | ✅ | 1.8.2 → 1.9.0 |
| `CLAUDE.md` | ⚠️ 未更新 | sub-assembly + versionDefines pattern 沒有寫入「Adding an External Tool」段落，下次想加 optional-package built-in 的人會找不到參考 |
| `doc/requirement/feature_localization_mcp.md` | ✅ 已 commit | 原始 spec 保留為審查參考 |
| `doc/lessons/unity-mcp-lessons.md` | ⚠️ 未更新 | 過程中發現幾個值得記錄的 Unity 知識點：①`Locale.CreateLocale` 必須走 factory 不能用 `create_scriptable_object` 直接寫 ②`recompile_scripts` 不會主動 `AssetDatabase.Refresh`，新檔案必須先 `Assets/Refresh` ③`run_tests` 在某些環境會回 0/0 |

---

## 7. 建議的後續動作（不在此 PR 範圍）

1. 補 `CLAUDE.md` 的 "Optional Package Sub-Assembly" 章節
2. 補 `doc/lessons/unity-mcp-lessons.md` 的三條教訓
3. Debug `run_tests` 環境問題
4. 評估 §4.10 的 locale code 預檢
5. 評估 §4.3 的 path 回傳結構改善
6. 評估 `loc_delete_table` / `loc_remove_locale` 對稱補完
