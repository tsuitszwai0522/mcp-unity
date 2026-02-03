
# Migration Guide Protocol for Claude Code

此規則為 Claude Code 執行大型遷移作業的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：所有溝通、文件撰寫必須使用 **zh-TW**。
2. **禁止搶跑**：在使用者明確表示「批准」之前，**禁止開始執行遷移**。
3. **Checklist 驅動**：所有步驟必須以可勾選形式呈現。
4. **回滾優先**：每個階段必須預先定義回滾策略。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 「升級 X 版本」
- 「遷移到 Y」
- 「從 A 換到 B」
- 「做一個升級 checklist」

## 執行流程 (Workflow)

### 第一階段：評估與規劃

1. **研究變更**：
   - 查詢官方 Migration Guide / Release Notes。
   - 識別 Breaking Changes。

2. **建立計畫**：
   - 在 `doc/migration/` 建立 `Migration_{Date}_{FromTo}.md`。
   - 包含：概述、前置條件、Breaking Changes、步驟、回滾策略、驗證計畫。

3. **等待批准**：
   - 列出計畫後，**必須暫停**，請使用者確認。

### 第二階段：執行遷移

1. **依序執行**：嚴格按照 Checklist 順序。
2. **階段驗證**：每個 Phase 完成後驗證。
3. **即時記錄**：在計畫文件中記錄執行日誌。

### 第三階段：完成確認

1. **最終驗證**：執行完整驗證計畫。
2. **更新文件**：同步更新 README、CHANGELOG。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| 遷移計畫 | `doc/migration/Migration_{Date}_{FromTo}.md` |

## 禁止事項 (Don'ts)

1. ❌ 未經批准就開始遷移
2. ❌ 跳過備份步驟
3. ❌ 忽略 Breaking Changes
4. ❌ 不定義回滾策略
5. ❌ 跨過 Point of No Return 而未告知
