---
name: incident-postmortem
description: Create blameless incident postmortem with root cause analysis and action items. Use when user wants a postmortem, to review an incident, or analyze production issues.
---

# Incident Postmortem Protocol for Claude Code

此規則為 Claude Code 執行 Production 事故復盤的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：所有溝通、文件撰寫必須使用 **zh-TW**。
2. **無責文化 (Blameless)**：**嚴禁**指責個人，聚焦於系統性改善。
3. **行動導向**：每份報告必須產出具體的 Action Items。
4. **時間線精確**：事故時間線必須精確記錄。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：
- 「做一份 Postmortem」
- 「復盤昨天的事故」
- 「分析 Production 問題的根本原因」

## 執行流程 (Workflow)

### 第一階段：資訊收集

1. **詢問關鍵資訊**：
   - 事故發生時間？
   - 影響範圍？
   - 如何發現的？
   - 如何解決的？

2. **建立報告**：
   - 在 `doc/postmortem/` 建立 `Postmortem_{Date}_{Title}.md`。
   - 包含：摘要、時間線、根本原因分析、影響評估、經驗教訓、Action Items。

### 第二階段：根本原因分析

1. **使用 5 Whys**：持續追問直到找到根本原因。
2. **識別貢獻因素**：不只看直接原因。

### 第三階段：產出行動項目

1. **分類**：預防 / 偵測 / 緩解。
2. **指派**：確保每項都有負責人與期限。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| 復盤報告 | `doc/postmortem/Postmortem_{Date}_{Title}.md` |

## 禁止事項 (Don'ts)

1. ❌ 指責個人
2. ❌ 停留在表面原因
3. ❌ 不產出 Action Items
4. ❌ Action Items 沒有負責人/期限
