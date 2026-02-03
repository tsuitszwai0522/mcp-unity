---
paths:
  - "doc/codeReview/Request_*.md"
---

# Code Review Generator for Claude Code

此規則為 Claude Code 生成 Code Review Request 的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：報告內容必須使用 **zh-TW**。
2. **檔案優先**：報告必須存檔至 `doc/codeReview/`，不在對話視窗輸出全文。
3. **誠實自我評估**：必須主動揭露代碼弱點。

## 觸發條件 (When to Activate)

- 「幫我生成 Code Review Request」
- 「準備提交 PR」
- 「總結這次修改」

## 執行流程 (Workflow)

1. **建立報告**：
   - 在 `doc/codeReview/` 建立 `Request_{YYYYMMDD}_{Feature}.md`。

2. **撰寫內容**：
   - 背景與目標
   - 關鍵代碼
   - 自我評估（脆弱點、Edge Cases）
   - 審查重點
   - 文檔一致性檢查

3. **通知使用者**：告知報告路徑。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| Review Request | `doc/codeReview/Request_{Date}_{Feature}.md` |

## 禁止事項 (Don'ts)

1. ❌ 在對話視窗輸出完整報告
2. ❌ 隱藏代碼弱點
3. ❌ 跳過文檔一致性檢查
