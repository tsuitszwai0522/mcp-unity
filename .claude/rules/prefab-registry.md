# Prefab Registry 規則

## 核心原則
在處理任何 prefab 相關的問題、Bug 修復、或功能開發前，**必須先確認 prefab 的實際狀態**，禁止憑記憶或假設提出建議。

## 強制流程

### 1. 查閱 Prefab 文檔
處理 prefab 相關問題時，先檢查 `doc/prefab/` 下是否有對應文檔：
- 有 → 讀取文檔，了解階層結構、組件、接線狀態
- 無 → 進入步驟 2

### 2. 即時檢查（文檔不存在或可能過時時）
使用以下方式確認 prefab 實際狀態：
- **優先**: MCP 工具 `open_prefab_contents` → `get_gameobject` 檢查組件與接線
- **備選**: 直接讀取 `.prefab` YAML 檔案

### 3. 禁止事項
- ❌ 在未確認 prefab 狀態下，建議「你需要接線」或「你需要加 Component」
- ❌ 假設 SerializeField 未綁定（很可能已在 Inspector 中綁定）
- ❌ 建議重新建立已存在的階層結構

### 4. 修改後更新
若修改了 prefab 的結構（新增/刪除 GameObject、Component、或改變接線），**必須同步更新** `doc/prefab/` 對應文檔。若文檔不存在，建立新文檔。

## Prefab 文檔位置
```
doc/prefab/{PrefabName}.md
```

## 文檔格式要求
每份 Prefab 文檔應包含：
1. **基本資訊** — 路徑、用途、實例化方式
2. **階層結構** — 樹狀圖，標註每個節點的組件
3. **SerializeField 接線** — 表格列出每個欄位的接線目標與狀態（✅/❌）
4. **關聯腳本** — 相關 C# 腳本路徑
5. **注意事項** — 預設狀態、runtime 行為等容易誤判的細節
