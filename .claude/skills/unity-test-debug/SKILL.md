---
name: unity-test-debug
description: Guide for Unity test execution and debug iteration. Use when user wants to run tests, debug compile errors, read console logs, or write Unity tests.
---

# Unity Test & Debug for Claude Code

此規則為 Claude Code 在 Unity 專案中執行測試與除錯的行為規範。

> 修改後驗證規則、錯誤處理表、MCP 工具注意事項請參考 `unity-mcp-workflow`。

## 觸發條件

- 使用者要求跑測試、查編譯錯誤、看 Console log、除錯問題、寫 Unity 測試

## 涵蓋的 MCP 工具與資源

| 工具/資源 | 用途 |
|-----------|------|
| `recompile_scripts` | 觸發編譯 + 取得結果 |
| `run_tests` | 執行 EditMode / PlayMode 測試 |
| `get_console_logs` | 讀取 log |
| `send_console_log` | 發送 debug 標記 |
| `unity://tests/*` | 列出測試清單 |
| `unity://logs/*` | 瀏覽 Console log |

## 核心規則

1. **編譯優先**：改 C# → `recompile_scripts` → 確認通過 → 才能跑測試。
2. **EditMode 優先**：預設 EditMode。僅 4 種場景用 PlayMode（生命週期、Coroutine、Physics、場景載入），且須通過 Pre-flight Checklist。
3. **架構引導**：建議將邏輯從 MonoBehaviour 分離為純 C# 類，讓 EditMode 測試覆蓋更多場景。
4. **測試篩選**：用 `testFilter` 縮小範圍，避免跑全部測試。
5. **Log 策略**：`includeStackTrace: false`，先看 error 再看 warning，必要時才開 stack trace。
6. **迭代上限**：連續 3 次 compile→test 循環失敗 → 暫停報告，不盲目重試。

## 核心流程

```
改 C# → recompile_scripts → 編譯錯誤？→ 修正 → 重新編譯
                           ↓ 成功
                     run_tests (EditMode)
                           ↓
                     失敗？→ 修正 → 回到 recompile
                           ↓ 通過
                     get_console_logs → warning/error？→ 修正
                           ↓
                         完成 ✓

PlayMode 分支（MCP 會斷線）：
  run_tests (PlayMode) → 預期斷線 → 等待重連
    → get_console_logs 確認完成
    → 讀取 TestResults.xml 驗證結果
```

## EditMode vs PlayMode 差異

| | EditMode | PlayMode |
|--|----------|----------|
| `.asmdef` `includePlatforms` | `["Editor"]` | `[]`（空） |
| `UnityEditor.*` API | 可用 | **不可用** |
| 可引用 assembly | Editor + Runtime | **僅 Runtime** |
| 測試 attribute | `[Test]` | `[Test]` + `[UnityTest]`（`IEnumerator`） |

**常見 PlayMode 編譯失敗**：(1) `using UnityEditor` (2) 引用 `*.Editor` assembly (3) 混用 async/await 與 IEnumerator (4) 目標 code 無 `.asmdef` 無法引用

## PlayMode 適用場景

| 場景 | 原因 |
|------|------|
| MonoBehaviour 生命週期 | 需 Play Mode 觸發回呼 |
| Coroutine | 需 MonoBehaviour 驅動 |
| Physics | 需 Physics engine loop |
| 場景載入 + UI 互動 | 需完整 runtime |

## PlayMode MCP 斷線問題

**已知限制**：透過 MCP `run_tests` 執行 PlayMode 測試時，Unity 進入 Play Mode 會導致 MCP WebSocket 斷線，`run_tests` 回傳 `Connection failed: Unknown error`。

**正確處理流程**：

1. 呼叫 `run_tests(testMode: "PlayMode", ...)`，預期會收到連線錯誤（這是正常的）。
2. **不要重試** `run_tests`——測試已經在 Unity 內執行中。
3. 等待 MCP 重連（Unity 退出 Play Mode 後自動恢復，通常 10~30 秒）。
4. 重連後用 `get_console_logs` 確認測試完成。
5. 讀取測試結果 XML（路徑會出現在 console log 中，格式：`Saving results to: /path/to/TestResults.xml`）。

**注意**：
- CLI batch mode (`-runTests -batchmode`) 在 Unity Editor 已開啟時無法使用（project lock）。
- 不要因為斷線就改用 EditMode——如果測試需要 PlayMode，就用 PlayMode。

## PlayMode Pre-flight Checklist

1. PlayMode test `.asmdef` 存在，`includePlatforms` 為 `[]`
2. `.asmdef` 僅引用 runtime assembly（無 `*.Editor`）
3. `precompiledReferences` 含 `nunit.framework.dll`
4. `defineConstraints` 含 `UNITY_INCLUDE_TESTS`
5. 確認目標 runtime code 的 `.asmdef` 名稱可被引用
6. 代碼中無 `using UnityEditor`
7. `[UnityTest]` return type 為 `IEnumerator`

> 任一項不通過 → 暫停向使用者說明，不自行修正。

## Log 閱讀策略

- 預設 `includeStackTrace: false`（節省 80-90% token）
- 篩選順序：error → warning → info
- 分頁：log 量大時用 `limit` + `offset`
- 定位問題時才開 `includeStackTrace: true`
- 用 `send_console_log` 標記特定時間點

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `unity-mcp-workflow` | 雙向：引用錯誤處理表；擴充修改後驗證規則 |
| `test-engineer` | 引用：測試文件格式（`TestDoc_*.md`） |

## 禁止事項

1. 不要改 C# 後不 `recompile_scripts` 就跑測試
2. 不要未確認編譯通過就 `run_tests`
3. 不要預設用 PlayMode — 除非符合 4 種場景且通過 Checklist
4. 不要在 PlayMode 中 `using UnityEditor`
5. 不要在 PlayMode `.asmdef` 引用 `*.Editor` assembly
6. 不要 `get_console_logs` 預設開 `includeStackTrace`
7. 不要連續 3 次失敗後繼續盲目重試

## 主動學習 (Active Learning)

- **操作前**：讀取 `doc/lessons/unity-mcp-lessons.md`，避免重蹈已知問題。
- **操作後**：判斷本次操作是否產生新經驗（踩坑、發現隱藏行為、確認可行做法、找到更好方法），若「是」→ 依 `unity-mcp-learning` 協議追加記錄；若「否」→ 不做任何事。
