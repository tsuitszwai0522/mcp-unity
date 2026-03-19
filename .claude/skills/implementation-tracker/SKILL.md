---
name: implementation-tracker
description: Track multi-phase feature implementation progress. Use when user starts implementing a phase, continues previous progress, completes a phase, or wants to update progress tracking.
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
2. **Spec 覆蓋自查** ⚠️ 必填：
   - 讀取設計文件中本 Phase 對應的所有功能點
   - 逐項列出：✅ 已實作 / ❌ 未實作 / 🔶 部分實作
   - 若有「❌ 未實作」，必須說明原因（刻意延後 / 遺漏 / 設計變更）
   - **特別注意**：所有 enum 值、狀態機狀態、switch/match 分支是否完整
   - 若發現遺漏，必須在本次 Phase 中補完，不可直接標記完成
3. 填寫：完成事項、關鍵決策、修改清單、**注意事項給下一階段**。
4. 同步更新設計文件的任務勾選。

### Review 修改後

1. 在「關鍵決策」新增 `[Review Fix]` 條目。
2. 連結對應的 Review Response 文件。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| 追蹤文件 | `doc/requirement/feature_{name}_tracker.md` |

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `feature-design-protocol` | 依據設計文件的任務清單初始化 Tracker |
| `code-review-generator` | Review Request 可引用 Tracker 中的關鍵決策 |
| `code-reviewer` | 執行 Refactor Prompt 後需更新 Tracker |
| `bug-fix-protocol` | 實作中發現 Bug 時觸發 Bug Fix 流程 |
| `verification-loop` | Phase 完成後建議執行驗證迴圈 |

## 禁止事項 (Don'ts)

1. ❌ 跳過「注意事項給下一階段」
2. ❌ 忽略前一階段的備註
3. ❌ 不同步設計文件的任務狀態
