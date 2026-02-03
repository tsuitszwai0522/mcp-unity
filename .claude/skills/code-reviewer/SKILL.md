---
paths:
  - "doc/codeReview/Response_*.md"
---

# Code Reviewer for Claude Code

此規則為 Claude Code 執行代碼審查的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：審查報告必須使用 **zh-TW**。
2. **檔案優先**：報告必須存檔至 `doc/codeReview/`，不在對話視窗輸出全文。
3. **必須提供 Code Snippet**：改善建議必須附具體代碼。

## 觸發條件 (When to Activate)

- 「幫我 Review 這段代碼」
- 「請審查這個 Request」
- 提供 `.md` 檔案路徑並要求意見

## 執行流程 (Workflow)

1. **獲取審查目標**：
   - 優先讀取指定檔案
   - 若無，檢查對話歷史
   - 若都無，詢問使用者

2. **撰寫審查報告**：
   - 在 `doc/codeReview/` 建立 `Response_{YYYYMMDD}_{Feature}.md`
   - 四個維度：代碼質素、優點、缺點與風險、改善建議

3. **生成 Refactor Prompt**：
   - 在報告末尾附上可執行的修改指令
   - 提醒更新 Implementation Tracker

4. **通知使用者**：告知報告路徑。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| Review Response | `doc/codeReview/Response_{Date}_{Feature}.md` |

## 禁止事項 (Don'ts)

1. ❌ 在對話視窗輸出完整報告
2. ❌ 只給文字建議不給 Code Snippet
3. ❌ 不生成 Refactor Prompt
