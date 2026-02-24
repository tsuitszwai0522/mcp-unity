---
name: feature-design-protocol
description: 當使用者想要討論、設計或實作新的功能(Feature)或系統(System)時使用。
---

# Feature Design Protocol (功能設計規範)

此 Skill 用於規範新功能的開發流程，**嚴格禁止**在完成設計文件之前直接編寫代碼或生成 Implementation Plan。

## 核心規則 (Core Rules)
1.  **語言要求**：所有溝通、文件撰寫、計畫生成必須使用 **zh-TW**。
2.  **禁止搶跑**：在使用者明確 **Approve (批准)** 設計文件之前，**不可**修改專案代碼，也不可生成正式的 Implementation Plan。
3.  **主動澄清 (Active Clarification)**：
    * 如果使用者的需求有任何模糊不清的地方，或者你無法完全理解某些術語，**必須立刻提出疑問**，不可自行腦補或假設。
    * 將這些問題與使用者的澄清回覆，一併記錄在文件中。

## 執行流程 (Workflow)

### 第一階段：需求分析與文檔建立
當使用者提出新功能需求時：
1.  **初始化文件**：
    * 在 `doc/requirement/` 目錄下建立一個新的 Markdown 文件（例如：`feature_login_system.md`）。
    * 如果目錄不存在，請先建立。
2.  **判斷複雜度**：如果是大型系統，主動建議使用者將其拆分為多個子任務 (Sub-tasks)，並解釋拆分邏輯。

### 第二階段：方案研擬與迭代 (The Loop)
在討論過程中，請執行以下循環：

1.  **方案比較 (Critical Analysis)**：
    * 如果該需求存在多種技術實作方式（例如：使用 Observer Pattern vs. C# Events），**必須在 Markdown 中列出所有可行方案**。
    * 針對每個方案，列出 **優點 (Pros)** 與 **缺點 (Cons)**。
    * **暫停並詢問**：列出後，請使用者選擇偏好的方案。
    * **記錄決策**：當使用者做出選擇後，將「最終採用的方案」與「選擇理由」明確記錄在文件中。

2.  **文件持續更新**：
    * **需求 (Requirements)**：使用者的原始需求。
    * **技術方案 (Technical Approach)**：上述決策後的架構細節。
    * **待解問題 (Open Questions)**：目前尚不明確的部分。
    * **任務清單 (Task List)**：實作步驟的 Checkbox 列表。

3.  **確認**：每次更新後，詢問使用者是否滿意當前的設計。

### 第三階段：批准與執行
當使用者明確表示「批准」或「可以開始了」 (Approve)：
1.  **最終確認**：確保 `doc/requirement/` 下的文件是最新的。
2.  **生成計畫**：正式生成 Implementation Plan。
3.  **開始實作**：此時才允許進入編碼階段。

## 設計文件模板 (Template)

```markdown
# Feature: {功能名稱}

## 需求描述 (Requirements)
{使用者原始需求與澄清後的完整描述}

## 技術方案 (Technical Approach)

### 方案比較
| 方案 | 優點 | 缺點 |
|------|------|------|
| A | ... | ... |
| B | ... | ... |

### 最終採用方案
{選定的方案描述}

### 決策理由
{為何選擇此方案}

## 待解問題 (Open Questions)
{仍需確認的事項，若無則標註「無」}

## 任務清單 (Task List)
- [ ] Phase 1: ...
- [ ] Phase 2: ...
```

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `implementation-tracker` | **下游**：設計批准後，由 implementation-tracker 追蹤實作進度 |
| `code-review-generator` | **下游**：實作完成後，code-review-generator 會引用設計文件進行一致性檢查 |

## 觸發時機 (When to use)
* 使用者說：「我想做一個...系統」
* 使用者說：「幫我設計...功能」
* 使用者詢問：「如何實作...？」

## 禁止事項 (Don'ts)
1. ❌ 在用戶 Approve 前修改專案代碼
2. ❌ 跳過方案比較直接採用單一方案（除非用戶明確要求）
3. ❌ 假設使用者的意圖（必須主動確認）
4. ❌ 忽略現有架構模式
