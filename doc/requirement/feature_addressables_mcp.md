# Feature: Addressables MCP Tools

> **狀態**: 需求整理
> **前置**: Unity Addressables package (`com.unity.addressables` ≥ 1.19.0) 已安裝
> **建立日期**: 2026-04-13

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

`address` 預設為 asset path；`labels` optional，未存在嘅 label 會自動建立並產生 warning。

**回傳**:
```json
{
  "added": 3,
  "skipped": 1,
  "warnings": ["Label 'newlabel' was created automatically"],
  "entries": [
    { "guid": "...", "assetPath": "...", "address": "...", "group": "..." }
  ]
}
```

**錯誤**: `not_found` (group 或 asset)、`validation_error` (空 array)。

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

## 不在 v1 範圍 (留 v2)

- **Profile 管理** — `addr_list_profiles` / `addr_create_profile` / `addr_set_active_profile` / `addr_set_profile_variable`
- **Build** — `addr_build` (new build / update / clean)，需要 async + long-running
- **Analyze** — `addr_run_analyze_rule` (Check Duplicate Bundle Dependencies 等)
- **Build Report** — bundle size、dependency tree
- **詳細 Schema 設定** — 完整 BundledAssetGroupSchema 各欄位（compression、bundle naming、provider type 等）

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
