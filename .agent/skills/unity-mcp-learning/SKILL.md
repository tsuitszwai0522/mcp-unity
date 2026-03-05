---
name: unity-mcp-learning
description: Unity MCP 主動學習協議。在 Unity MCP 操作或 Unity API 相關工作後，自動判斷並記錄經驗教訓至知識庫，避免重蹈覆轍。
---

# Unity MCP Active Learning Protocol

此 Skill 定義 AI Agent 在執行 Unity MCP 操作或涉及 Unity API 的代碼修改後，主動判斷並累積經驗的協議。

> **此 Skill 不直接由使用者呼叫**，而是由 `unity-mcp-workflow`、`unity-ui-builder`、`unity-test-debug` 等 Unity 相關 Skill 在操作前後自動引用。

## 知識檔位置

`doc/lessons/unity-mcp-lessons.md`

## 操作前：讀取知識檔

在開始任何 Unity MCP 工具操作或 Unity API 相關代碼修改前，**必須讀取**知識檔，了解已知的陷阱與最佳實踐，避免重蹈已記錄的問題。

若知識檔不存在，跳過此步驟（首次使用時會在操作後自動建立）。

## 操作後：判斷是否記錄

每次完成 Unity MCP 工具呼叫或涉及 Unity API 的代碼修改後，主動評估本次操作是否產生了值得記錄的經驗。

### 值得記錄的情境

| 類型 | 說明 | 範例 |
|------|------|------|
| Pitfall | 踩到坑並找到解決方案 | `set_rect_transform` 的 `anchorPreset` 拼錯不會報錯，只是不生效 |
| Undocumented | 發現 API/工具行為與文件描述不同 | `batch_execute` 超過 50 個操作時效能急遽下降 |
| Better Way | 找到比現有做法更好的方法 | 用 `objectPath` 結構化引用比 `instanceId` 更穩定 |
| Confirmed | 確認某個不確定的做法可行 | `update_component` 可以同時設定多個欄位 |
| Edge Case | 工具參數的邊界條件或隱藏行為 | `create_ui_element` 的 `text` 參數不支援換行符 |

### 不需要記錄的情境

- 已在知識檔中存在的相同經驗
- Skill 文件（`unity-mcp-workflow` 等）已明確記載的行為
- 一次性的操作失誤（如打錯路徑名）
- 與 Unity MCP/API 無關的一般性程式邏輯

## 記錄格式

追加至知識檔時，使用以下格式：

```markdown
### [分類] 簡短標題
- **日期**: YYYY-MM-DD
- **情境**: 在做什麼時遇到的
- **問題/發現**: 具體描述
- **解法/結論**: 正確做法或結論
```

分類使用：`Pitfall` / `Undocumented` / `Better Way` / `Confirmed` / `Edge Case`

## 去重規則

寫入前**必須先讀取**知識檔全文，確認：
1. 不存在描述相同問題的條目
2. 若存在相似條目但有新資訊，**更新**既有條目而非新增

## 更新既有條目

若發現知識檔中的某條記錄已過時（例如 MCP 工具更新後行為改變），應直接修改該條目並標注更新日期。
