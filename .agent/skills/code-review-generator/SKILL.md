---
name: code-review-generator
description: 當使用者要求生成 Code Review Request、PR 描述或代碼審查報告時使用。
---

# Code Review Request Generator

此 Skill 用於將目前的實作成果整理成一份標準化的「代碼審查請求」。

## 核心規則 (Core Rules)
1.  **語言要求**：報告內容必須使用 **zh-TW**。
2.  **檔案優先 (File Only)**：
    * **必須**將完整的報告存檔至 `doc/codeReview/`。
    * **禁止**在對話視窗中輸出報告全文。
    * 僅需告知使用者：「分析報告已建立，請查看：[檔案路徑]」。
3.  **誠實自我評估**：在「自我評估」章節，必須展現批判性思考，主動揭露代碼的弱點，不可過度自信。

## 檔案管理 (File Management)
1.  檢查是否存在 `doc/codeReview/` 目錄，若無則建立。
2.  檔案命名規則：`Request_{YYYYMMDD}_{FeatureName}.md` (例如：`Request_20240320_LoginSystem.md`)。

## 報告結構要求 (Report Structure)

請依照以下章節撰寫報告：

### 1. 背景與目標 (Context)
* 簡述此功能解決了什麼問題。
* 說明核心的實作思路或架構決策。

### 2. 關鍵代碼 (Implementation)
* 附上最終版本的完整代碼（或是本次修改的核心片段）。
* 若有多個檔案，請清楚標示檔案路徑。

### 3. 自我評估與疑慮 (Self-Assessment)
* **脆弱點**：指出你覺得最沒有把握、邏輯最複雜、或最容易出錯的地方。
* **Edge Cases**：誠實列出尚未處理的邊緣情況或潛在 Bug。
* *提示：不要只說好話，Reviewer 需要知道風險。*

### 4. 審查重點 (Review Focus)
* 引導 Reviewer 特別檢查哪些面向（例如：效能優化、安全性漏洞、變數命名可讀性、擴充性）。

### 5. 文檔一致性檢查 (Documentation Check)
* **檢查對象**：讀取當初在 `doc/requirement/` 建立的對應需求文件。
* **差異分析**：目前的實作 (Implementation) 是否偏離了原始設計？
    * 如果有偏離（例如改了架構、砍了功能），**必須**在此報告中列出，並詢問使用者是否需要更新 Requirement 文件。

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `verification-loop` | **前置**：生成 Review Request 前，建議先執行 verification-loop 確保代碼通過所有驗證 |
| `code-reviewer` | **下游**：生成的 Review Request 由 code-reviewer 審查 |
| `feature-design-protocol` | **引用**：檢查實作是否偏離 `doc/requirement/` 中的原始設計 |

## 觸發時機 (When to use)
* 使用者說：「幫我生成 Code Review Request」
* 使用者說：「準備提交 PR」
* 使用者說：「總結一下這次的修改，我要給別人看」

## 禁止事項 (Don'ts)
1. ❌ 在對話視窗輸出完整報告
2. ❌ 自我評估過度樂觀，隱藏弱點
3. ❌ 跳過文檔一致性檢查
