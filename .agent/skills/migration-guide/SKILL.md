---
name: migration-guide
description: 當使用者需要執行大型遷移、版本升級、框架替換或技術棧轉換時使用。
---

# Migration Guide Protocol (遷移指南規範)

此 Skill 用於規範大型遷移作業的流程，確保系統性地規劃、執行與驗證，降低遷移風險。

## 核心規則 (Core Rules)

1. **語言要求**：所有文件與溝通必須使用 **zh-TW**。
2. **禁止搶跑**：在使用者明確 **Approve (批准)** 遷移計畫之前，**嚴格禁止**開始執行遷移。
3. **檔案優先 (File Only)**：
   - **必須**將完整的遷移計畫存檔至 `doc/migration/`。
   - **禁止**在對話視窗中輸出計畫全文。
4. **Checklist 驅動**：所有遷移步驟必須以可勾選的 Checklist 形式呈現。
5. **回滾優先**：每個階段必須預先定義回滾策略。

## 遷移類型分類 (Migration Types)

| 類型 | 範例 | 風險等級 |
|------|------|---------|
| **版本升級** | Unity 2021 → Unity 6, .NET 6 → .NET 9 | Medium-High |
| **框架替換** | Redux → Zustand, REST → GraphQL | High |
| **資料庫遷移** | MySQL → PostgreSQL, 結構變更 | High |
| **架構重組** | Monolith → Microservices | Very High |
| **依賴替換** | 更換第三方 SDK | Medium |
| **配置遷移** | 環境變數重構、CI/CD 變更 | Low-Medium |

## 執行流程 (Workflow)

### 第一階段：遷移評估 (Migration Assessment)

當使用者提出遷移需求時：

1. **建立檔案**：
   - 在 `doc/migration/` 目錄下建立 Markdown 計畫。
   - 命名規則：`Migration_{YYYYMMDD}_{FromTo}.md`
   - 例如：`Migration_20260115_Unity2021ToUnity6.md`

2. **撰寫計畫內容**：

   #### 1. 遷移概述 (Overview)
   - **來源狀態 (From)**：目前的版本/架構/技術。
   - **目標狀態 (To)**：遷移後的版本/架構/技術。
   - **遷移動機**：為何需要遷移？（End of Support、效能需求、新功能需求）
   - **預估影響範圍**：受影響的模組數量、檔案數量。

   #### 2. 前置條件檢查 (Prerequisites Checklist)
   ```markdown
   - [ ] 備份完成（代碼、資料庫、配置）
   - [ ] 開發環境已準備（新版本 SDK 已安裝）
   - [ ] 團隊已通知（若為協作專案）
   - [ ] 相依套件相容性已確認
   - [ ] 測試環境可用
   ```

   #### 3. 破壞性變更分析 (Breaking Changes Analysis)
   - 列出官方文件中的 Breaking Changes。
   - 標註專案中受影響的具體代碼位置。
   - 分類為：**必須處理** / **建議處理** / **可忽略**。

   #### 4. 遷移步驟 (Migration Steps)
   - 以 **Phase** 為單位組織。
   - 每個 Phase 包含多個 **Task**。
   - 每個 Task 必須是可驗證的原子操作。

   #### 5. 回滾策略 (Rollback Strategy)
   - 每個 Phase 必須定義回滾方式。
   - 標註 **Point of No Return**（若有）。

   #### 6. 驗證計畫 (Validation Plan)
   - 列出遷移後的驗證步驟。
   - 包含自動化測試與手動測試項目。

3. **通知使用者**：「遷移計畫已建立，請查看：[檔案路徑]」。

### 第二階段：計畫審閱 (Plan Review)

1. **逐項確認**：與使用者逐一確認 Breaking Changes 的處理方式。
2. **補充遺漏**：根據使用者回饋補充未識別的影響點。
3. **風險確認**：確保使用者理解 Point of No Return 的影響。

### 第三階段：執行遷移 (Migration Execution)

當使用者明確表示「批准」或「開始遷移」後：

1. **依序執行 Checklist**：
   - 每完成一個 Task，立即在計畫文件中勾選。
   - 若遇到非預期問題，暫停並更新計畫。

2. **階段性驗證**：
   - 每個 Phase 完成後，執行該階段的驗證步驟。
   - 驗證通過才能進入下一個 Phase。

3. **問題記錄**：
   - 所有遇到的問題與解決方案，記錄在計畫文件的「執行日誌」區塊。

### 第四階段：遷移完成 (Migration Completion)

1. **最終驗證**：執行完整的驗證計畫。
2. **更新文件**：
   - 更新專案的 README、CHANGELOG。
   - 若有架構文件，同步更新。
3. **歸檔計畫**：將遷移計畫移至 `archive/` 或標記為已完成。

## 遷移計畫模板結構 (Template Structure)

```markdown
# Migration: {From} → {To}

## Overview
- **From**: {來源版本/架構}
- **To**: {目標版本/架構}
- **Date**: {YYYY-MM-DD}
- **Status**: Planning / In Progress / Completed / Rolled Back

## Prerequisites Checklist
- [ ] Backup completed
- [ ] Dev environment ready
- [ ] Dependencies verified

## Breaking Changes
| ID | Change | Impact | Status |
|----|--------|--------|--------|
| BC-1 | {描述} | {影響} | ⏳ 待處理 |

## Migration Phases

### Phase 1: {階段名稱}
**Rollback Strategy**: {回滾方式}

- [ ] Task 1.1: {任務描述}
- [ ] Task 1.2: {任務描述}
- [ ] Verify: {驗證步驟}

### Phase 2: {階段名稱}
⚠️ **Point of No Return**
...

## Execution Log
| Time | Event | Notes |
|------|-------|-------|
| {HH:MM} | Started Phase 1 | - |

## Post-Migration Validation
- [ ] All tests passing
- [ ] Manual smoke test
- [ ] Performance benchmark
```

## 觸發時機 (When to use)

- 使用者說：「幫我升級 Unity 版本」
- 使用者說：「我要把 .NET 5 升級到 .NET 9」
- 使用者說：「計畫一下資料庫遷移」
- 使用者說：「怎麼從 X 框架換到 Y 框架？」
- 使用者說：「幫我做一個升級 checklist」

## 禁止事項 (Don'ts)

1. ❌ 未建立完整計畫就開始遷移
2. ❌ 跳過前置條件檢查（尤其是備份）
3. ❌ 忽略 Breaking Changes 分析
4. ❌ 不定義回滾策略
5. ❌ 跨過 Point of No Return 而未明確告知使用者
6. ❌ 遷移過程中不記錄執行日誌
