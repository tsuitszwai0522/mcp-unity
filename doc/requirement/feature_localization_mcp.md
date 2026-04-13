# Feature: Localization MCP Tools

> **狀態**: ✅ 已實作
> - **v1.9.0** (2026-04-13): 7 個工具 — 本文件 1~6 號 spec 工具 + `loc_add_locale`（spec 之外，bootstrap helper）
> - **v1.11.0** (2026-04-13): 補完 symmetric counterparts — `loc_delete_table` + `loc_remove_locale`；整體 8 個工具
> - **前置**: Unity Localization 已安裝、CB_Tooltip StringTable 已建立
> - **建立日期**: 2026-04-10
> - **參考**: `doc/codeReview/Request_20260413_LocalizationTools.md` + `Response_20260413_LocalizationTools.md`

---

## 需求描述

建立一組 MCP 工具，讓 AI Agent 可以直接操作 Unity Localization StringTable，不需要手動在 Unity Editor 操作或跑 Setup Script。

---

## 工具清單

### 1. `loc_list_tables` — 列出所有 StringTable

**用途**: 查看專案中有哪些 StringTable Collection。

**參數**: 無

**回傳**:
```json
{
  "tables": [
    { "name": "CB_Tooltip", "locales": ["zh-TW"], "entryCount": 48 }
  ]
}
```

---

### 2. `loc_get_entries` — 讀取 StringTable entries

**用途**: 查看指定 StringTable 的 key/value，支援搜尋過濾。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | StringTable Collection 名稱（如 `CB_Tooltip`） |
| `locale` | string | ❌ | Locale code（預設 `zh-TW`） |
| `filter` | string | ❌ | Key 前綴過濾（如 `cb_ext_` 只列 L3 entries） |

**回傳**:
```json
{
  "table": "CB_Tooltip",
  "locale": "zh-TW",
  "entries": [
    { "key": "cb_buff_power_up", "value": "力量上升" },
    { "key": "cb_ext_crit_preview_l3", "value": "暴擊傷害：{critDamage}..." }
  ]
}
```

---

### 3. `loc_set_entry` — 新增或修改單個 entry

**用途**: 設定指定 key 的 value。key 不存在時自動新增。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | StringTable Collection 名稱 |
| `locale` | string | ❌ | Locale code（預設 `zh-TW`） |
| `key` | string | ✅ | Entry key |
| `value` | string | ✅ | Entry value（支援 TMP RichText 色碼） |

**回傳**:
```json
{
  "action": "updated",  // or "created"
  "key": "cb_cond_progress_unmet",
  "value": "<color=#88CCFF>{current}</color> 層（差 <color=#FF6666>{gap}</color> 層達成條件）"
}
```

---

### 4. `loc_set_entries` — 批量新增或修改多個 entries

**用途**: 一次設定多個 key/value，適合批量更新。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | StringTable Collection 名稱 |
| `locale` | string | ❌ | Locale code（預設 `zh-TW`） |
| `entries` | array | ✅ | `[{ "key": "...", "value": "..." }, ...]` |

**回傳**:
```json
{
  "created": 3,
  "updated": 5,
  "total": 8
}
```

---

### 5. `loc_delete_entry` — 刪除 entry

**用途**: 移除不再使用的 key。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | StringTable Collection 名稱 |
| `key` | string | ✅ | 要刪除的 entry key |

**回傳**:
```json
{
  "deleted": true,
  "key": "cb_old_unused_key"
}
```

---

### 6. `loc_create_table` — 建立新 StringTable Collection

**用途**: 新系統需要新 StringTable 時使用。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | 新 Collection 名稱（如 `UI_Common`） |
| `locales` | array | ❌ | Locale codes（預設 `["zh-TW"]`） |

**回傳**:
```json
{
  "created": true,
  "name": "UI_Common",
  "path": "Assets/ProjectT/Localization/Tables/UI_Common Shared Data.asset"
}
```

---

### 7. `loc_add_locale` — 註冊 Locale（v1.9.0 spec 之外補完）

**用途**: Bootstrap helper。`loc_create_table` 故意不會自動建 Locale，所以 fresh project 在第一次跑前需要先 register 至少一個 Locale。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `code` | string | ✅ | Locale identifier code（如 `zh-TW`、`en`、`ja`） |
| `directory` | string | ❌ | Locale asset 路徑（預設 `Assets/Localization/Locales`） |

**回傳**: `{ action: "created" \| "already_exists", code, path, warnings? }`

**備註**: 若 `code` 不在 .NET CultureInfo / IETF tag 列表（例如 `zh-Hant`），會在回傳裡附 `warnings` 但仍會建立 Locale —— Unity Localization 接受比 .NET 更寬的 identifier。

---

### 8. `loc_remove_locale` — 反註冊 Locale（v1.11.0 補完）

**用途**: `loc_add_locale` 的 symmetric counterpart。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `code` | string | ✅ | 要移除的 Locale code |
| `delete_asset` | boolean | ❌ | 是否同時刪除 `.asset` 檔（預設 `true`） |

**回傳**: `{ action: "removed" \| "not_registered", code, path }`

---

### 9. `loc_delete_table` — 刪除 StringTable Collection（v1.11.0 補完）

**用途**: `loc_create_table` 的 symmetric counterpart。原子地刪除 Collection、SharedTableData、與每個 per-locale StringTable。透過 `AssetDatabase.DeleteAsset` 走 `LocalizationAssetModificationProcessor` hook，是 Unity Localization 1.x 唯一支援的 collection 刪除路徑（沒有 public `RemoveCollection` API）。

**參數**:
| 參數 | 類型 | 必填 | 說明 |
|------|------|------|------|
| `table_name` | string | ✅ | 要刪除的 Collection 名稱 |

**回傳**:
```json
{
  "deleted": true,
  "name": "UI_Common",
  "path": "Assets/Localization/Tables/UI_Common Shared Data.asset",
  "entryCount": 12,
  "locales": ["zh-TW", "en"]
}
```

---

## 實作規範

- 繼承 `McpToolBase`，放在 `Assets/ProjectT/Editor/AIQAMCP/` 目錄
- 命名前綴 `loc_`，與現有 `cb_` / `gf_` 區分
- 每次修改後自動呼叫 `EditorUtility.SetDirty()` + `AssetDatabase.SaveAssets()`
- 錯誤處理：table 不存在、locale 不存在、key 格式不合法等

## 優先順序

| 優先 | 工具 | 理由 |
|------|------|------|
| P0 | `loc_get_entries` | 最常用，確認現有值 |
| P0 | `loc_set_entry` | 最常用，改單個 entry |
| P1 | `loc_set_entries` | 批量操作 |
| P1 | `loc_list_tables` | 查看可用 tables |
| P2 | `loc_delete_entry` | 偶爾用 |
| P2 | `loc_create_table` | 新系統時才用 |
