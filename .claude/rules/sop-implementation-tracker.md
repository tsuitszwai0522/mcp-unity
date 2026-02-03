---
paths:
  - "doc/requirement/**/*_tracker.md"
---

# Implementation Tracker for Claude Code

此規則為 Claude Code 追蹤多階段功能實作進度的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：所有追蹤記錄必須使用 **zh-TW**。
2. **關聯設計文件**：Tracker 必須與設計文件配對存在。
3. **即時更新**：每完成一個 Phase 立即更新。
4. **備註必填**：「注意事項給下一階段」為必填欄位。

## 觸發條件 (When to Activate)

- 「開始實作 Phase X」
- 「繼續上次的進度」
- 「這個 Phase 做完了」
- 「更新進度追蹤」
- 「Review 修改完成，更新 Tracker」

## 執行流程 (Workflow)

### 開始新 Phase

1. 檢查 Tracker 是否存在，若無則初始化。
2. 讀取前一階段的「注意事項」。
3. 標記當前 Phase 為 `🔄 進行中`。

### 完成 Phase

1. 標記為 `✅ 已完成`。
2. 填寫：完成事項、關鍵決策、修改清單、**注意事項給下一階段**。
3. 同步更新設計文件的任務勾選。

### Review 修改後

1. 在「關鍵決策」新增 `[Review Fix]` 條目。
2. 連結對應的 Review Response 文件。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| 追蹤文件 | `doc/requirement/feature_{name}_tracker.md` |

## 禁止事項 (Don'ts)

1. ❌ 跳過「注意事項給下一階段」
2. ❌ 忽略前一階段的備註
3. ❌ 不同步設計文件的任務狀態
