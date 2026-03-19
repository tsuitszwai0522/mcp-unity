# Unity MCP Active Learning（Always-On）

每次使用 `mcp__mcp-unity__*` 工具後，主動判斷是否有值得記錄的經驗。

## 知識檔

`doc/lessons/unity-mcp-lessons.md`

## 觸發時機

在以下情況發生後，自動評估是否需要記錄：
- 呼叫任何 `mcp__mcp-unity__*` 工具（成功或失敗皆算）
- 修改 Unity API 相關的 C# 代碼後遇到非預期行為

## 判斷標準

| 記錄 | 不記錄 |
|------|--------|
| 踩坑並解決 (Pitfall) | 知識檔已有的相同經驗 |
| API 行為與預期不同 (Undocumented) | 一次性操作失誤（打錯路徑等） |
| 找到更好做法 (Better Way) | 與 Unity MCP/API 無關的邏輯 |
| 確認不確定的做法可行 (Confirmed) | 順利完成、無新發現的常規操作 |
| 工具參數邊界條件 (Edge Case) | |

## 執行流程

1. MCP 工具呼叫完成後，判斷結果是否符合上表「記錄」欄
2. 若「是」→ 讀取 `doc/lessons/unity-mcp-lessons.md` 確認不重複後追加
3. 若既有條目已過時，直接更新該條目
4. 若「否」→ 不做任何事（不需要告知用戶）

## 記錄格式

```markdown
### [分類] 簡短標題
- **日期**: YYYY-MM-DD
- **情境**: 在做什麼時遇到的
- **問題/發現**: 具體描述
- **解法/結論**: 正確做法或結論
```
