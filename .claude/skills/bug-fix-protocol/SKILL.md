---
name: bug-fix-protocol
description: Execute bug fix analysis and documentation. Use when user reports a bug, pastes error logs, asks why code isn't working, or mentions strange behavior.
---

# Bug Fix Protocol for Claude Code

此規則為 Claude Code 執行 Bug 修復的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：所有溝通、文件撰寫必須使用 **zh-TW**。
2. **禁止搶跑**：在使用者明確表示「批准」之前，**禁止修改專案代碼**。
3. **檔案優先**：報告必須存檔，不在對話視窗輸出全文。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 「我發現一個 Bug...」
- 貼上 Error Log 並問原因
- 「為什麼這個功能的行為怪怪的？」
- 「這段代碼跑不動」

## 執行流程 (Workflow)

### 第一階段：分析

1. **建立報告**：
   - 在 `doc/bugFix/` 建立 `Fix_{YYYYMMDD}_{Issue}.md`。
   - 包含：問題描述、根源分析、建議方案、副作用評估。

2. **等待批准**：
   - 告知使用者報告路徑，**必須暫停**等待確認。

### 第二階段：執行

1. **依批准方案修復**：根據報告中的方案修改代碼。
2. **建議驗證**：建議執行 `verification-loop` 驗證修復結果，或至少執行 `test-engineer` 確保無 Regression。

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `verification-loop` | 銜接：修復後建議執行驗證迴圈 |
| `test-engineer` | 引用：修復後可委派補充測試 |
| `code-review-generator` | 下游：修復並驗證後可生成 Review Request |

## 輸出規範

| 項目 | 路徑 |
|------|------|
| Bug 分析報告 | `doc/bugFix/Fix_{Date}_{Issue}.md` |

## 禁止事項 (Don'ts)

1. ❌ 未經批准就修改代碼
2. ❌ 跳過根源分析
3. ❌ 在對話視窗輸出完整報告
