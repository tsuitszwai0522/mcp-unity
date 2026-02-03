---
name: incident-postmortem
description: 當發生 Production 事故、服務中斷、重大 Bug 後，用於進行結構化的事故復盤分析。
---

# Incident Postmortem Protocol (事故復盤規範)

此 Skill 用於規範 Production 事故的復盤流程，採用「無責文化 (Blameless Culture)」原則，聚焦於系統性改善而非追究個人責任。

## 核心規則 (Core Rules)

1. **語言要求**：所有文件與溝通必須使用 **zh-TW**。
2. **無責文化 (Blameless)**：
   - **嚴禁**在報告中指責個人。
   - 聚焦於「系統/流程如何讓錯誤發生」，而非「誰犯了錯」。
3. **檔案優先 (File Only)**：
   - **必須**將完整的復盤報告存檔至 `doc/postmortem/`。
   - **禁止**在對話視窗中輸出報告全文。
4. **行動項目必填**：每份報告必須產出具體的改善行動項目 (Action Items)。
5. **時間線精確**：事故時間線必須精確到分鐘（若可能）。

## 事故嚴重度分級 (Severity Levels)

| 等級 | 名稱 | 定義 | 響應時間 |
|------|------|------|---------|
| **SEV1** | Critical | 全面服務中斷、資料遺失、安全漏洞 | 立即 |
| **SEV2** | Major | 核心功能不可用、大量用戶受影響 | < 1 小時 |
| **SEV3** | Minor | 部分功能異常、少量用戶受影響 | < 4 小時 |
| **SEV4** | Low | 非關鍵功能異常、極少用戶受影響 | < 24 小時 |

## 執行流程 (Workflow)

### 第一階段：事故記錄 (Incident Documentation)

事故發生後（或處理告一段落後）：

1. **建立檔案**：
   - 在 `doc/postmortem/` 目錄下建立 Markdown 報告。
   - 命名規則：`Postmortem_{YYYYMMDD}_{IncidentTitle}.md`
   - 例如：`Postmortem_20260115_DatabaseConnectionPoolExhausted.md`

2. **撰寫報告內容**：

   #### 1. 事故摘要 (Incident Summary)
   - **事故標題**：簡潔描述事故。
   - **嚴重度**：SEV1 / SEV2 / SEV3 / SEV4。
   - **影響範圍**：受影響的用戶數、功能、區域。
   - **持續時間**：從偵測到解決的總時長。
   - **狀態**：已解決 / 緩解中 / 監控中。

   #### 2. 時間線 (Timeline) ⚠️ 關鍵
   - 以時間順序記錄所有關鍵事件。
   - 格式：`[YYYY-MM-DD HH:MM] {事件描述}`
   - 包含：首次異常出現、偵測時間、響應時間、各階段處理、解決時間。

   #### 3. 根本原因分析 (Root Cause Analysis)
   - **直接原因 (Immediate Cause)**：觸發事故的直接因素。
   - **根本原因 (Root Cause)**：為何直接原因能夠發生？
   - **使用 5 Whys 分析法**：
     ```
     為什麼 1？→ 因為 A
     為什麼 A？→ 因為 B
     為什麼 B？→ 因為 C
     ...
     ```
   - **貢獻因素 (Contributing Factors)**：其他加劇事故的因素。

   #### 4. 影響評估 (Impact Assessment)
   - **用戶影響**：多少用戶受影響？影響程度？
   - **業務影響**：營收損失、SLA 違約、聲譽損害。
   - **技術影響**：資料完整性、系統穩定性。

   #### 5. 應急處理 (Mitigation & Resolution)
   - **緩解措施**：採取了什麼臨時措施？
   - **根本修復**：最終如何解決問題？
   - **附上相關的 Commit / PR / Config 變更**。

   #### 6. 經驗教訓 (Lessons Learned)
   - **什麼做得好**：哪些響應/機制有效？
   - **什麼需要改善**：哪些流程/工具不足？
   - **意外發現**：復盤過程中發現的其他問題。

   #### 7. 行動項目 (Action Items) ⚠️ 必填
   - 列出具體的改善措施。
   - 每個項目必須有：負責人、優先級、預計完成日期。
   - 分類：**預防 (Prevention)** / **偵測 (Detection)** / **緩解 (Mitigation)**。

3. **通知使用者**：「事故復盤報告已建立，請查看：[檔案路徑]」。

### 第二階段：團隊審閱 (Team Review)

1. **收集補充資訊**：邀請相關人員補充時間線或技術細節。
2. **確認根本原因**：確保分析到位，不只停留在表面。
3. **審核行動項目**：確認每個 Action Item 都是可執行且有意義的。

### 第三階段：追蹤改善 (Follow-up)

1. **定期檢視**：追蹤 Action Items 的完成進度。
2. **更新報告**：當 Action Items 完成時，更新報告狀態。
3. **歸檔**：所有 Action Items 完成後，將報告移至 `archive/`。

## 5 Whys 分析範例 (5 Whys Example)

```
問題：資料庫連線池耗盡，導致服務中斷。

Why 1: 為什麼連線池耗盡？
→ 因為同時有太多查詢在執行。

Why 2: 為什麼有太多查詢？
→ 因為某個 API 端點沒有做查詢結果快取。

Why 3: 為什麼沒有做快取？
→ 因為開發時沒有識別到這是高頻端點。

Why 4: 為什麼沒有識別到？
→ 因為沒有 API 流量監控，也沒有 Code Review 檢查快取策略。

Why 5: 為什麼沒有這些檢查？
→ 因為團隊沒有制定 API 設計規範與 Review Checklist。

→ 根本原因：缺乏 API 設計規範與監控機制。
→ Action Items：建立 API 設計規範、加入快取檢查項目、部署 API 監控。
```

## 觸發時機 (When to use)

- 使用者說：「昨天的事故要做復盤」
- 使用者說：「Production 出了問題，幫我分析一下」
- 使用者說：「寫一份 Postmortem」
- 使用者說：「那個 Bug 的根本原因是什麼？」（若為 Production Bug）
- 事故處理完成後，主動建議進行復盤

## 禁止事項 (Don'ts)

1. ❌ 在報告中指責或歸咎個人
2. ❌ 跳過 5 Whys 分析，停留在表面原因
3. ❌ 不產出具體的 Action Items
4. ❌ Action Items 沒有負責人或期限
5. ❌ 時間線不精確或有遺漏
6. ❌ 只記錄問題，不記錄什麼做得好

## 與其他 Skills 的協作

| 情境 | 協作方式 |
|------|---------|
| 事故由 Bug 引起 | 先用 `incident-postmortem` 完成復盤，再用 `bug-fix-protocol` 處理修復 |
| 需要代碼修改 | Postmortem 的 Action Item 可觸發 `feature-design-protocol` 或直接修復 |
| 需要監控改善 | Action Item 記錄後，可獨立追蹤實作 |
