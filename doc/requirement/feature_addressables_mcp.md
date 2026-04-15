# Feature: Addressables MCP Tools

> **狀態**: v1 已實作、v1.1 已實作（Group Schema + Profile 管理）
> **前置**: Unity Addressables package (`com.unity.addressables` ≥ 1.19.0) 已安裝
> **建立日期**: 2026-04-13
> **v1.1 擴充日期**: 2026-04-15

---

## 需求描述

建立一組 MCP 工具，讓 AI Agent 可以直接管理 Unity Addressables 系統的 Group、Entry、Label 與 Settings，省卻在 Addressables Groups Window 內手動拉 asset、改 address、設 label 的繁瑣操作。

跟 `Localization` 工具同樣模式：放在獨立 asmdef，靠 `MCP_UNITY_ADDRESSABLES` version define 守門，未安裝 Addressables package 嘅 project 唔會 break build。Node 端永遠 register，Unity 端冇對應 tool 時 fall through 到 `unknown method`。

---

## 範圍 (v1)

只做以下 4 類，總共 **15 個 tool**。Profiles / Build / Analyze / 詳細 Schema 設定 留 v2。

| 類別 | 工具數 |
|---|---|
| Settings / Bootstrap | 2 |
| Group 管理 | 4 |
| Entry 管理 | 5 |
| Label 管理 | 3 |
| Query | 1 |

---

## 工具清單

### Settings / Bootstrap

#### 1. `addr_get_settings` — 查詢 Addressables 設定狀態

**用途**: 讓 agent 知道 Addressables 是否已 init、預設 group 與 active profile 是什麼。所有後續操作都應該先確認呢個。

**參數**: 無

**回傳**:
```json
{
  "initialized": true,
  "defaultGroup": "Default Local Group",
  "activeProfile": "Default",
  "profileVariables": {
    "BuildPath": "[UnityEngine.AddressableAssets.Addressables.BuildPath]",
    "LoadPath": "[UnityEngine.AddressableAssets.Addressables.RuntimePath]"
  },
  "groupCount": 3,
  "labels": ["preload", "ui", "audio"],
  "version": "1.21.20"
}
```

`initialized: false` 時其他欄位省略，agent 應該提示用戶或自己 call `addr_init_settings`。

---

#### 2. `addr_init_settings` — 初始化 Addressables 設定

**用途**: 對應 Addressables Groups Window 嘅 "Create Addressables Settings" 按鈕。新 project 第一次用必須先做。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `folder` | string | ❌ | Settings 資料夾路徑（預設 `Assets/AddressableAssetsData`） |

**回傳**:
```json
{
  "created": true,
  "settingsPath": "Assets/AddressableAssetsData/AddressableAssetSettings.asset",
  "defaultGroup": "Default Local Group"
}
```

已存在時 return `created: false` 但唔報錯。

---

### Group 管理

#### 3. `addr_list_groups` — 列出所有 Group

**回傳**:
```json
{
  "groups": [
    {
      "name": "Default Local Group",
      "isDefault": true,
      "entryCount": 12,
      "schemas": ["BundledAssetGroupSchema", "ContentUpdateGroupSchema"]
    }
  ]
}
```

---

#### 4. `addr_create_group` — 建立新 Group

**用途**: 建立一個帶有預設 `BundledAssetGroupSchema` + `ContentUpdateGroupSchema` 嘅 group（等同 Editor "New > Packed Assets Group"）。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `name` | string | ✅ | Group 名稱（不可重複） |
| `set_as_default` | bool | ❌ | 是否設為 Default Group（預設 `false`） |
| `packed_mode` | string | ❌ | `PackTogether` / `PackSeparately` / `PackTogetherByLabel`（預設 `PackTogether`） |
| `include_in_build` | bool | ❌ | 是否包含進 build（預設 `true`） |

**回傳**:
```json
{
  "created": true,
  "name": "RemoteContent",
  "isDefault": false
}
```

**錯誤**: `duplicate` (同名已存在)、`validation_error` (空名)。

---

#### 5. `addr_remove_group` — 刪除 Group

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `name` | string | ✅ | Group 名稱 |
| `force` | bool | ❌ | 為 `true` 時強制刪除（即使有 entries）；預設 `false` |

**回傳**:
```json
{
  "deleted": true,
  "name": "OldGroup",
  "removedEntryCount": 5
}
```

**錯誤**: `not_found`、`in_use` (有 entries 且 `force=false`)、`validation_error` (試圖刪除 default group)。

---

#### 6. `addr_set_default_group` — 切換 Default Group

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `name` | string | ✅ | 新 default group 名稱 |

**回傳**:
```json
{
  "defaultGroup": "RemoteContent",
  "previousDefault": "Default Local Group"
}
```

---

### Entry 管理

#### 7. `addr_list_entries` — 列出 Entries

**用途**: 列出符合條件嘅 entries。所有 filter 都 optional；無 filter 時用 `limit` 防止結果爆炸。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `group` | string | ❌ | 只列指定 group 內嘅 entries |
| `label_filter` | string | ❌ | 只列含此 label 嘅 entries |
| `address_pattern` | string | ❌ | Address 通配符（支援 `*`） |
| `asset_path_prefix` | string | ❌ | Asset path 前綴過濾（如 `Assets/Prefabs/UI/`） |
| `limit` | int | ❌ | 最大回傳數（預設 200） |

**回傳**:
```json
{
  "total": 12,
  "truncated": false,
  "entries": [
    {
      "guid": "abc123...",
      "assetPath": "Assets/Prefabs/UI/MainMenu.prefab",
      "address": "ui/main_menu",
      "labels": ["ui", "preload"],
      "group": "Default Local Group"
    }
  ]
}
```

---

#### 8. `addr_add_entries` — 批量新增 Entries

**用途**: 將一批 asset 加入指定 group。同個 round-trip 處理多個 asset，避免逐個 call。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `group` | string | ✅ | 目標 group 名稱（必須已存在） |
| `assets` | array | ✅ | `[{ "asset_path": "...", "address": "...", "labels": [...] }]` |
| `fail_on_missing_asset` | boolean | ❌ | 預設 `true`：任何 `asset_path` 解析唔到整批會以 `not_found` 中止。設 `false` 切換到 best-effort 模式：略過缺失 asset 並返 `missingAssets` 清單。 |

`address` 預設為 asset path；`labels` optional，未存在嘅 label 會自動建立並產生 warning。

**回傳（嚴格模式 — 預設）**:
```json
{
  "added": 3,
  "skipped": 0,
  "warnings": ["Label 'newlabel' was created automatically"],
  "entries": [
    { "guid": "...", "assetPath": "...", "address": "...", "group": "..." }
  ]
}
```

**回傳（`fail_on_missing_asset:false` 寬鬆模式）**:
```json
{
  "added": 3,
  "skipped": 1,
  "warnings": ["Asset 'Assets/Missing.prefab' not found, skipped"],
  "missingAssets": ["Assets/Missing.prefab"],
  "entries": [
    { "guid": "...", "assetPath": "...", "address": "...", "group": "..." }
  ]
}
```

**錯誤**:
- `not_found` — group 不存在；或嚴格模式下（預設）任何 `asset_path` 解析不到
- `validation_error` — `assets` 係空 array，或嚴格模式下某個 entry 嘅 `asset_path` 係空字串

---

#### 9. `addr_remove_entries` — 批量移除 Entries

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `entries` | array | ✅ | `[{ "guid": "..." } 或 { "asset_path": "..." }]` |

**回傳**:
```json
{
  "removed": 2,
  "notFound": 1
}
```

---

#### 10. `addr_move_entries` — 跨 Group 搬移 Entries

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `target_group` | string | ✅ | 目標 group |
| `entries` | array | ✅ | `[{ "guid" 或 "asset_path" }]` |

**回傳**:
```json
{
  "moved": 3,
  "targetGroup": "RemoteContent",
  "notFound": 0
}
```

---

#### 11. `addr_set_entry` — 修改單個 Entry

**用途**: 改 address 或 add/remove labels。Partial update — 未提供嘅欄位不變。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `guid` | string | △ | 與 `asset_path` 二選一 |
| `asset_path` | string | △ | 與 `guid` 二選一 |
| `new_address` | string | ❌ | 新 address |
| `add_labels` | array | ❌ | 要加上嘅 labels |
| `remove_labels` | array | ❌ | 要移除嘅 labels |

**回傳**:
```json
{
  "guid": "abc123...",
  "assetPath": "Assets/Prefabs/UI/MainMenu.prefab",
  "address": "ui/main_menu_v2",
  "labels": ["ui", "preload", "v2"]
}
```

---

### Label 管理

#### 12. `addr_list_labels` — 列出全部 Labels

**回傳**:
```json
{
  "labels": ["ui", "audio", "preload"]
}
```

---

#### 13. `addr_create_label` — 建立 Label

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `label` | string | ✅ | Label 名稱（不可含空格、特殊字元） |

**回傳**:
```json
{
  "created": true,
  "label": "remote_dlc"
}
```

---

#### 14. `addr_remove_label` — 刪除 Label

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `label` | string | ✅ | Label 名稱 |
| `force` | bool | ❌ | 為 `true` 時即使仍被 entry 使用都刪除；預設 `false` |

**回傳**:
```json
{
  "deleted": true,
  "label": "old_label",
  "affectedEntries": 0
}
```

**錯誤**: `in_use` (仍被 entry 引用且 `force=false`)、`not_found`。

---

### Query

#### 15. `addr_find_asset` — 根據 Asset Path 查 Entry

**用途**: Agent 經常需要由 asset 反查佢喺邊個 group、address、有乜 label。比 `addr_list_entries` filter 更直接。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `asset_path` | string | ✅ | Asset path |

**回傳**:
```json
{
  "found": true,
  "entry": {
    "guid": "abc123...",
    "assetPath": "Assets/Prefabs/UI/MainMenu.prefab",
    "address": "ui/main_menu",
    "labels": ["ui"],
    "group": "Default Local Group"
  }
}
```

`found: false` 時 `entry` 省略。

---

## 實作規範

### 檔案結構

```
Editor/Tools/Addressables/
├── McpUnity.Addressables.asmdef        # 獨立 asmdef + version define
├── AddrHelper.cs                        # 共用工具
├── AddrGetSettingsTool.cs
├── AddrInitSettingsTool.cs
├── AddrListGroupsTool.cs
├── AddrCreateGroupTool.cs
├── AddrRemoveGroupTool.cs
├── AddrSetDefaultGroupTool.cs
├── AddrListEntriesTool.cs
├── AddrAddEntriesTool.cs
├── AddrRemoveEntriesTool.cs
├── AddrMoveEntriesTool.cs
├── AddrSetEntryTool.cs
├── AddrListLabelsTool.cs
├── AddrCreateLabelTool.cs
├── AddrRemoveLabelTool.cs
└── AddrFindAssetTool.cs

Server~/src/tools/addressablesTools.ts   # 全部 register 函數
Server~/src/index.ts                      # 在 Localization 後 import + register
```

### asmdef 設定

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
  "includePlatforms": ["Editor"],
  "defineConstraints": ["MCP_UNITY_ADDRESSABLES"],
  "versionDefines": [{
    "name": "com.unity.addressables",
    "expression": "1.19.0",
    "define": "MCP_UNITY_ADDRESSABLES"
  }]
}
```

### 共用 Helper (`AddrHelper.cs`)

集中以下邏輯，避免 15 個 tool 各自重複：
- `TryGetSettings(out AddressableAssetSettings settings, out JObject error)` — 未 init 時返回 `not_initialized` 錯誤
- `ResolveEntry(string guid, string assetPath)` — 兩種識別方式統一
- `SaveSettings(settings, modificationEvent)` — `EditorUtility.SetDirty` + `AddressableAssetSettings.SetDirty(...)` + `AssetDatabase.SaveAssets`
- `EntryToJson(entry)` — 統一 entry 序列化格式

### 錯誤分類

統一用 `error_type` field（跟 Localization 一致）：
- `not_initialized` — Addressables 未 init
- `not_found` — group/entry/label/asset 不存在
- `duplicate` — 同名已存在
- `in_use` — 仍被引用，需要 `force=true`
- `validation_error` — 參數格式錯

### 自動發現

跟 Localization 一樣，C# tool 唔需要喺 `McpUnityServer.RegisterTools()` 手動 register — `DiscoverExternalTools()` 會自動掃 `McpToolBase` 子類並掛載。

### 命名前綴

統一用 `addr_`，與 `loc_` / `cb_` 區分。

---

## 技術注意點

1. **Settings null check** — `AddressableAssetSettingsDefaultObject.GetSettings(false)` 可能 return null（用戶未 init）。全部 tool 起手都先過 `AddrHelper.TryGetSettings`，未 init 時返回 `not_initialized` 錯誤並提示 call `addr_init_settings`。

2. **Asset path → guid** — 用 `AssetDatabase.AssetPathToGUID()`，空字串 = asset 唔存在。Add entries 前必須驗證。

3. **Label 格式驗證** — Addressables 內部會 reject 含空格嘅 label。Pre-validate 出個清晰 error 比較好。

4. **Save 同步** — 修改 settings 後必須 `EditorUtility.SetDirty(settings)` + `AssetDatabase.SaveAssets()`，並 fire `AddressableAssetSettings.SetDirty(...)` 通知 Groups Window refresh。封喺 `AddrHelper.SaveSettings` 一個地方。

5. **Undo 不支援** — Addressables 自己對 `Undo.RecordObject` 支援有限。v1 不嘗試實作 undo；agent 改錯可以喺 Groups Window 手動 revert。每個 destructive 操作做之前 log 改動內容。

6. **Async 不需要** — v1 全部係輕量 settings 操作，唔需要 `IsAsync`。Build / Analyze（v2）先要。

7. **批量優先** — `add_entries` / `remove_entries` / `move_entries` 都接受 array，避免 N 次 round-trip。

---

## 優先順序

| 優先 | 工具 | 理由 |
|------|------|------|
| P0 | `addr_get_settings`、`addr_init_settings` | 所有後續操作嘅前置 |
| P0 | `addr_list_groups`、`addr_create_group` | 最基本 group 操作 |
| P0 | `addr_add_entries`、`addr_list_entries` | 最常用 entry 操作 |
| P1 | `addr_find_asset`、`addr_set_entry` | 高頻查詢與修改 |
| P1 | `addr_remove_entries`、`addr_move_entries` | 維護用 |
| P1 | `addr_list_labels`、`addr_create_label` | 配合 entry 用 |
| P2 | `addr_remove_group`、`addr_set_default_group` | 偶爾用 |
| P2 | `addr_remove_label` | 偶爾用 |

---

## 不在 v1 範圍

**已於 v1.1 實作（2026-04-15，見下方「v1.1 擴充」）**:
- ✅ 詳細 Schema 設定 — 完整 `BundledAssetGroupSchema` 各欄位（compression、bundle naming、build/load path、retry、timeout 等）
- ✅ Profile 管理 — `addr_list_profiles` / `addr_get_active_profile` / `addr_set_active_profile` / `addr_set_profile_variable`

**仍在 backlog**:
- **Profile 建立** — `addr_create_profile`（v1.1 靠 `create_if_missing` 在 profile-settings 層建變數，但 profile 本身還是要在 Unity GUI 建）
- **Build** — `addr_build` (new build / update / clean)，需要 async + long-running
- **Analyze** — `addr_run_analyze_rule` (Check Duplicate Bundle Dependencies 等)
- **Build Report** — bundle size、dependency tree

---

## 實作步驟

1. 建立 asmdef + `AddrHelper.cs`
2. Settings 兩個 tool（最簡單，先打通 round-trip）
3. Group 4 個 tool
4. Label 3 個 tool（獨立、簡單）
5. Entry 5 個 tool（用到前面 helpers）
6. `addr_find_asset`
7. TS 端 `addressablesTools.ts` + `index.ts` register
8. `cd Server~ && npm run build`
9. 手動測試流程：
   - 開空 project → `addr_get_settings`（`initialized: false`）
   - `addr_init_settings` → `addr_get_settings`（確認 init）
   - `addr_create_group RemoteContent`
   - `addr_add_entries` 加 3 個 prefab
   - `addr_list_entries` 確認
   - `addr_find_asset` 反查
   - `addr_set_entry` 改 address + 加 label
   - `addr_remove_entries` 清理
10. CHANGELOG bump 到 v1.10.0：`feat(tools): add Unity Addressables tool suite`

---

## v1.1 擴充 — Group Schema 調整 + Profile Variables（2026-04-15）

### 背景 / 動機

v1 打通了 entries 層級（create group / add entries / labels），但 **group schema 層級的設定**仍然要回 Unity Addressables Groups Window 手動調。下游 ProjectT（Unity 2022.3.62f3）在用 `addr_*` 工具接線 5 個 TMP 字型 group 時實戰發現三個缺口：

1. **`BundledAssetGroupSchema` 欄位無法更新** — `addr_create_group` 建完之後，compression / include_in_build / bundle naming / packed_mode / runtime 載入行為都改不了。
2. **`BuildPath` / `LoadPath` 切換不了** — 這是 Local vs Remote 的關鍵 switch，對 Small Client Strategy（font / character / UI 走 CDN）完全不夠用。
3. **Profile variables 管理缺失** — CDN URL 其實是 profile variable（例如 `Remote.LoadPath`），冇 `addr_*_profile*` 工具就只能開 Addressables Profiles window 手動改。

對應 v1.1 加入 **6 個工具**，P0 解決 schema 調整、P1 解決 profile 管理。

| 優先 | 類別 | 工具 |
|------|------|------|
| P0 | Group Schema | `addr_set_group_schema`, `addr_get_group_schema` |
| P1 | Profile 管理 | `addr_list_profiles`, `addr_get_active_profile`, `addr_set_active_profile`, `addr_set_profile_variable` |

### 工具清單（v1.1）

#### 16. `addr_set_group_schema` — Partial update 一個 Group 的 BundledAssetGroupSchema

**用途**: 建完 group 之後，用 partial update 改 compression / build path / 其他 schema 欄位。只改傳入的 key，其他欄位不動。支援 `dry_run` 模式回傳 diff 不實際存檔（方便 agent 先預覽）。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `group` | string | ✅ | Group 名稱（必須已存在） |
| `dry_run` | bool | ❌ | `true` 時只回傳 diff 不實際寫入（預設 `false`） |
| `values` | object | ✅ | Partial set of schema fields（下表） |

**`values` 欄位（全部 optional，依照 agent 想改咩就傳咩）**:
| 欄位 | 類型 | 對應 `BundledAssetGroupSchema` 屬性 |
|------|------|----|
| `compression` | `Uncompressed` / `LZ4` / `LZMA` | `Compression` |
| `include_in_build` | bool | `IncludeInBuild` |
| `packed_mode` | `PackTogether` / `PackSeparately` / `PackTogetherByLabel` | `BundleMode` |
| `bundle_naming` | `AppendHash` / `NoHash` / `OnlyHash` / `FileNameHash` | `BundleNaming` |
| `use_asset_bundle_cache` | bool | `UseAssetBundleCache` |
| `use_unitywebrequest_for_local_bundles` | bool | `UseUnityWebRequestForLocalBundles` |
| `retry_count` | int | `RetryCount` |
| `timeout` | int | `Timeout` |
| `build_path` | string | `BuildPath.SetVariableByName(...)` — profile variable 名（如 `Remote.BuildPath`） |
| `load_path` | string | `LoadPath.SetVariableByName(...)` — profile variable 名（如 `Remote.LoadPath`） |

**回傳**:
```json
{
  "group": "TMP_Font_CN",
  "dryRun": false,
  "changed": ["compression", "load_path"],
  "diff": {
    "compression": { "from": "LZ4", "to": "LZMA" },
    "load_path":   { "from": "Local.LoadPath", "to": "Remote.LoadPath" }
  }
}
```

`changed` 是**實際發生改變**的欄位 — 如果傳入的值跟現狀一致，該欄位不會出現在 diff/changed 裡（no-op 語義）。

**錯誤**:
- `not_initialized` — Addressables 未 init
- `not_found` — group 不存在
- `schema_not_found` — group 冇 BundledAssetGroupSchema（理論上 `addr_create_group` 建的 group 都有）
- `validation_error` — 未知 field、enum 值非法、`values` 係空物件
- `variable_not_found` — `build_path` / `load_path` 指到一個冇喺 profile settings 層定義的變數名。回傳時會列 `Available: [...]` 方便除錯。
- `set_failed` — `ProfileValueReference.SetVariableByName` 回 `false`

---

#### 17. `addr_get_group_schema` — 讀取 Group 的 BundledAssetGroupSchema 值

**用途**: `addr_set_group_schema` 的唯讀伴生工具。用來：（1）驗證改動實際持久化；（2）配合 `dry_run` 先讀當前狀態再預覽變更。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `group` | string | ✅ | Group 名稱 |

**回傳**:
```json
{
  "group": "TMP_Font_CN",
  "values": {
    "compression": "LZMA",
    "include_in_build": true,
    "packed_mode": "PackTogether",
    "bundle_naming": "AppendHash",
    "use_asset_bundle_cache": true,
    "use_unitywebrequest_for_local_bundles": false,
    "retry_count": 0,
    "timeout": 0,
    "build_path": "Remote.BuildPath",
    "load_path": "Remote.LoadPath",
    "build_path_value": "ServerData/StandaloneOSX",
    "load_path_value": "https://cdn.example.com/StandaloneOSX"
  }
}
```

除了跟 set 工具對齊的欄位名，額外回傳 `build_path_value` / `load_path_value` —— 即 profile variable 在當前 active profile 展開後的實際字串（token 已替換），方便 agent 一眼看到「真的拉去邊個 URL」。

**錯誤**: 跟 `addr_set_group_schema` 一致（`not_initialized` / `not_found` / `schema_not_found`）。

---

#### 18. `addr_list_profiles` — 列出所有 Profile + 變數值

**用途**: 讓 agent 一次看晒所有 profile 同佢哋嘅 variable map，標記邊個 active。

**參數**: 無

**回傳**:
```json
{
  "activeProfile": "Default",
  "activeProfileId": "abc123",
  "variableNames": ["BuildTarget", "Local.BuildPath", "Local.LoadPath", "Remote.BuildPath", "Remote.LoadPath"],
  "profiles": [
    {
      "id": "abc123",
      "name": "Default",
      "isActive": true,
      "variables": {
        "BuildTarget": "[UnityEditor.EditorUserBuildSettings.activeBuildTarget]",
        "Local.BuildPath": "[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]",
        "Local.LoadPath": "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]",
        "Remote.BuildPath": "ServerData/[BuildTarget]",
        "Remote.LoadPath": "https://cdn.example.com/[BuildTarget]"
      }
    },
    {
      "id": "def456",
      "name": "CDN_Staging",
      "isActive": false,
      "variables": { ... }
    }
  ]
}
```

---

#### 19. `addr_get_active_profile` — 查目前 Active Profile

**用途**: `addr_list_profiles` 的輕量版 — 只回當前 active 那一個，配合 schema 設定時常用。

**參數**: 無

**回傳**:
```json
{
  "id": "abc123",
  "name": "Default",
  "variables": {
    "BuildTarget": "...",
    "Local.BuildPath": "...",
    "Remote.LoadPath": "https://cdn.example.com/[BuildTarget]"
  }
}
```

---

#### 20. `addr_set_active_profile` — 切換 Active Profile

**用途**: 對應 Addressables Profiles window 嘅 "Set Active"。常用於 dev / staging / prod profile 切換。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `profile` | string | ✅ | 目標 profile 名稱（必須已存在） |

**回傳**:
```json
{
  "changed": true,
  "activeProfile": "CDN_Staging",
  "previousProfile": "Default"
}
```

Idempotent — 如果已經係 active，`changed: false` 不報錯。

**錯誤**: `profile_not_found`。

---

#### 21. `addr_set_profile_variable` — 設定 Profile 變數值

**用途**: 改一個 profile 變數嘅值（如 `Remote.LoadPath` → `https://cdn.example.com/[BuildTarget]`）。這是玩家設定 CDN URL 嘅主要路徑。

預設只修改已存在嘅變數。傳 `create_if_missing: true` 可以在 profile-settings 層建新變數（注意：`CreateValue` 係 global — 新變數會套用到**所有** profile，而非單一 profile）。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `profile` | string | ✅ | Profile 名稱（必須已存在） |
| `variable` | string | ✅ | 變數名稱（例如 `Remote.LoadPath`） |
| `value` | string | ✅ | 新值（可含 `[BuildTarget]` / `[UnityEngine.*]` token） |
| `create_if_missing` | bool | ❌ | 預設 `false`。`true` 時若變數不存在會在 profile-settings 層建立 |

**回傳**:
```json
{
  "profile": "Default",
  "variable": "Remote.LoadPath",
  "previousValue": "http://localhost/[BuildTarget]",
  "value": "https://cdn.example.com/[BuildTarget]",
  "created": false
}
```

`created: true` 代表剛用 `create_if_missing` 新增（此時 `previousValue` 會係 `null`）。

**錯誤**:
- `profile_not_found` — profile 不存在
- `variable_not_found` — 變數不存在且 `create_if_missing=false`
- `validation_error` — `variable` 係空字串、`value` 未傳

---

### v1.1 Acceptance Criteria（驗證重點）

1. `addr_set_group_schema` 可以在不開 Unity window 嘅情況下改 compression / include_in_build / packed_mode / build_path / load_path，並立即持久化（下一次 Unity 重啟仍保留）。
2. `addr_set_profile_variable` 可以改 `Remote.LoadPath`，而 `addr_get_group_schema` 回傳嘅 `load_path_value` 會即時反映新值（token 重新展開）—— 代表 profile variable 真嘅影響到 group 嘅實際行為，而非只改咗一個 detached 字串。
3. Partial update 語義清楚：`values` 只帶要改嘅欄位，其他欄位不動；重複套同一值 = no-op。
4. `dry_run: true` 會回傳會改咩 diff 但**唔實際寫入**，agent 可以先預覽再決定。
5. 錯誤回傳要清晰 —— group 不存在、schema 不存在、欄位名打錯、enum 非法、profile variable 打錯都要有明確 `error_type`，而非 `NullReferenceException`。

### v1.1 實作筆記

- **反射 / SerializedProperty 路徑 vs 直接 property setter**: 最初考慮用 `SerializedObject` + `SerializedProperty` 統一處理所有欄位，但因為 `BundledAssetGroupSchema` 每個 property setter 自己會 `SetDirty`（source 印證），直接用 property 路徑更簡單也更穩。Reflection 只留給 `TryParseEnum<TEnum>`。
- **`BuildPath` / `LoadPath` 是 `ProfileValueReference`（getter-only）**，不能直接 assign string。必須經 `reference.SetVariableByName(settings, name)`。工具層會先驗證該變數存在於 `profileSettings.GetVariableNames()`，否則回 `variable_not_found`。
- **`AddressableAssetSettings.kRemoteBuildPath` / `kRemoteLoadPath`** 才是正確的 variable key（`"Remote.BuildPath"` 帶點，唔係 `"RemoteBuildPath"`）。測試裡一律用 constant 避免 typo。
- **持久化**: 修改完 schema 或 profile 後用 `AddrHelper.SaveSettings(settings, ModificationEvent.GroupSchemaModified)` → 內部做 `settings.SetDirty(...)` + `EditorUtility.SetDirty(settings)` + `AssetDatabase.SaveAssets()`。`activeProfileId` 嘅 setter 本身會 fire 事件，但 tool 仍額外 `SaveAssets` 以確保 domain reload 後唔會消失。
- **`AddrProfileHelper.ResolveProfileId`**: 統一處理 profile 名轉 id 與 `profile_not_found` 錯誤，擺喺 `AddrProfileTools.cs` 內部 static class（與 `AddrHelper` 分開，避免污染跨 tool helper 的共用接口）。
- **Test fixture 擴充**: `AddrTests.cs` 加咗 `_originalActiveProfileId` 追蹤 + `RestoreActiveProfile()` helper + `CleanupTestArtifacts()` 掃 `McpAddrTest_*` 前綴嘅 profile 同 profile variables — 確保測試唔會污染 consumer project 嘅真實 profile 狀態。

### v1.1 測試計畫

新增 **16 個 NUnit test**（Editor/Tests/Addressables/AddrTests.cs 第 G、H 段）：

- **G1–G9**（group schema）:
  - G1: `addr_get_group_schema` 回傳全部 12 個欄位
  - G2: partial update 只改 provided 欄位、其餘不動
  - G3: `dry_run` 回傳 diff 但 read-back 原值不變
  - G4: 重複套同值 → 0 changed
  - G5: 非法 enum 值 → `validation_error`
  - G6: 未知 field → `validation_error`
  - G7: group 不存在 → `not_found`
  - G8: `Remote.BuildPath` / `Remote.LoadPath` 切換成功
  - G9: 未知 profile variable → `variable_not_found`

- **H1–H7**（profile 管理）:
  - H1: `list_profiles` 至少有一個 profile、active 唯一
  - H2: `get_active_profile` 回 id/name/variables
  - H3: `set_active_profile` 非存在 → `profile_not_found`
  - H4: 建一個 disposable profile → 切過去 → read back → idempotent 不報錯 → finally 切回原 profile
  - H5: 修改現有變數（`Remote.LoadPath`）並 read-back 驗證，finally 還原
  - H6: 修改不存在的變數 → `variable_not_found`
  - H7: `create_if_missing=true` 建新變數並 set

Live MCP 測試（2026-04-15，在 TestUnityMcp consumer project）全數通過，包括從 dry_run 一路到 Remote CDN URL 切換、profile variable 修改後 group 的 `load_path_value` 即時反映新值。

### v1.1 實作步驟（按這個順序做的）

1. 看 Addressables 1.22.3 package source 確認 API surface（BundledAssetGroupSchema property 名、ProfileValueReference.SetVariableByName、AddressableAssetProfileSettings 的 GetProfileId/SetValue/CreateValue）
2. `Editor/Tools/Addressables/AddrSetGroupSchemaTool.cs`（P0，核心 partial update + dry_run + diff）
3. `Editor/Tools/Addressables/AddrGetGroupSchemaTool.cs`（唯讀，配 `addr_set_group_schema` 驗證）
4. `Editor/Tools/Addressables/AddrProfileTools.cs`（4 個 profile 工具 + 共用 `AddrProfileHelper`）
5. `Server~/src/tools/addressablesTools.ts` 加 6 個 `registerAddr*` 函數
6. `Server~/src/index.ts` import + register
7. `npm run build`（0 error）
8. `Editor/Tests/Addressables/AddrTests.cs` 加 G/H 測試段 + fixture 擴充
9. `README.md` 加 6 個 tool 文件
10. Unity recompile（發現並修 `AddrGetGroupSchemaTool.cs` 缺 `using McpUnity.Unity;`）
11. Live MCP 端到端驗證 + 跑 G/H NUnit subset
12. CHANGELOG bump 到 v1.13.0：`feat(addressables): add group schema + profile management tools`
    （原計畫是 v1.12.0，但 2026-04-14 已有 usability release 佔用 v1.12.0，改為 v1.13.0）
13. **Review fix（v1.13.0 合併前，2026-04-15）** — 根據 `doc/codeReview/Response_20260415_AddressablesSchemaAndProfile.md`：
    - `AddrSetGroupSchemaTool.Execute` 改為 **validate-all then apply**（`List<SchemaChange>` + 捕獲 `Apply` delegate），杜絕 multi-field payload 後段失敗時前段欄位殘留 in-memory 改動
    - `TryParseEnum` 加 numeric-string rejection + `Enum.IsDefined` 檢查，`TryParseBool` / `TryParseNonNegativeInt` 改用 `JTokenType` 嚴格檢查 + 非負範圍，全部封住 `batch_execute` / 直接 WebSocket 繞 zod 的後門
    - 補 G10–G14 共 5 個 regression NUnit test
    - `AddrSetProfileVariableTool` C# / TS description、README 明確寫出 `create_if_missing=true` 會把變數加到**所有** profile
    - `AGENTS.md` current tools list 補齊 21 個 Addressables + 9 個 Localization tools（既有漂移一併收尾）

### v1.1 不做

- **`addr_create_profile`** — 用戶用得唔多，而且建 profile 常常要人為想個名，agent 難自動化。Profile 本身仍然要喺 Unity GUI 建；`addr_set_profile_variable` 嘅 `create_if_missing` 只會喺 profile-settings 層建新**變數**（affects all profiles），唔係建新 **profile**。
- **快捷 wrapper `addr_set_group_build_remote` / `_local`** — 原本 ProjectT 嘅需求單有呢兩個 P2 shortcut（一次把 build_path + load_path 切到 Remote / Local profile variables），但因為 `addr_set_group_schema` 一個 call 已經可以同時傳 `build_path` + `load_path`，再加 shortcut 係 over-engineering。用 batch_execute 包兩個 call 都得。
- **批量版 `addr_set_group_schemas`** — 同上理由 + `batch_execute` 已經覆蓋。
