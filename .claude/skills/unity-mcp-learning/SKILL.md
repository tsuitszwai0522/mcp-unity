---
name: unity-mcp-learning
description: Unity MCP active learning protocol. Auto-evaluate and record lessons after Unity MCP operations or Unity API code changes. Not invoked directly — referenced by unity-mcp-workflow, unity-ui-builder, unity-test-debug.
---

# Unity MCP Active Learning Protocol for Claude Code

此協議定義 Claude Code 在 Unity MCP 操作或 Unity API 代碼修改後，主動判斷並累積經驗的行為。

> **此 Skill 不直接呼叫**，由 `unity-mcp-workflow`、`unity-ui-builder`、`unity-test-debug` 在操作前後自動引用。

## 知識檔

`doc/lessons/unity-mcp-lessons.md`

## 操作前

讀取知識檔，了解已知陷阱與最佳實踐。檔案不存在則跳過。

## 操作後

每次完成 Unity MCP 工具呼叫或 Unity API 代碼修改後，判斷是否有值得記錄的經驗：

| 記錄 | 不記錄 |
|------|--------|
| 踩坑並解決 (Pitfall) | 知識檔已有的相同經驗 |
| API 行為與預期不同 (Undocumented) | Skill 文件已明確記載的行為 |
| 找到更好做法 (Better Way) | 一次性操作失誤（打錯路徑等） |
| 確認不確定的做法可行 (Confirmed) | 與 Unity MCP/API 無關的邏輯 |
| 工具參數邊界條件 (Edge Case) | |

判斷「是」→ 讀取知識檔確認不重複後追加；判斷「否」→ 不做任何事。

若既有條目已過時，直接更新該條目。

## 記錄格式

```markdown
### [分類] 簡短標題
- **日期**: YYYY-MM-DD
- **情境**: 在做什麼時遇到的
- **問題/發現**: 具體描述
- **解法/結論**: 正確做法或結論
```
