---
name: code-reviewer
description: Review code and generate review response with improvement suggestions. Use when user asks to review code, review a request, or wants feedback on code.
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
   - 五個維度：代碼質素、優點、缺點與風險、改善建議、**需求覆蓋率**

3. **生成 Refactor Prompt**：
   - 在報告末尾附上可執行的修改指令
   - 提醒更新 Implementation Tracker

4. **通知使用者**：告知報告路徑。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| Review Response | `doc/codeReview/Response_{Date}_{Feature}.md` |

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `code-review-generator` | 上游：審查其生成的 Request |
| `implementation-tracker` | 銜接：Refactor 完成後更新 Tracker |
| `verification-loop` | 建議：Refactor 後執行驗證迴圈 |

## 需求覆蓋率維度 (Spec Coverage)

此為第 5 維度，**優先級最高**：
- **強制前置動作**：讀取對應的需求文件 (`doc/requirement/`)
- **逐項比對**：需求中每一個功能點、enum 值、狀態、分支，是否都有對應實作？
- **Enum / Switch 全值檢查**：找出所有新增/修改的 enum type，比對 switch/match 是否涵蓋所有值
- **遺漏清單**：列出「需求有描述但代碼未實作」的項目
- **未測試清單**：列出「已實作但無 test case」的項目
- ⚠️ 需求遺漏嚴重程度 > 代碼質素問題，必須在 Refactor Prompt 中列為最優先修正項

## 禁止事項 (Don'ts)

1. ❌ 在對話視窗輸出完整報告
2. ❌ 只給文字建議不給 Code Snippet
3. ❌ 不生成 Refactor Prompt
4. ❌ 跳過需求覆蓋率檢查（即使 code 品質很好，遺漏需求仍是最嚴重的問題）
