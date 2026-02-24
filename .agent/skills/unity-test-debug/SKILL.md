---
name: unity-test-debug
description: Unity 測試執行與除錯的迭代循環引導。覆蓋「編譯 → 測試 → 讀 log → 定位問題 → 修正」的完整流程。
---

# Unity Test & Debug Workflow

此 Skill 引導 AI Agent 在 Unity 專案中執行測試與除錯的迭代循環。

> **前置知識**：MCP 工具通用規範（錯誤處理表、工具注意事項）請參考 `unity-mcp-workflow`。

## 觸發條件 (When to Activate)

當使用者說出以下類型的請求時，啟動此流程：

- 「跑測試」「執行 EditMode 測試」
- 「為什麼編譯失敗」「編譯錯誤」
- 「看 Console 錯誤」「有什麼 warning」
- 「除錯這個問題」「為什麼 runtime 報錯」
- 「幫這個 class 寫測試」（Unity 專案內）

## 涵蓋的 MCP 工具與資源

| 工具/資源 | 用途 |
|-----------|------|
| `recompile_scripts` | 觸發編譯 + 取得編譯結果 |
| `run_tests` | 執行 EditMode / PlayMode 測試 |
| `get_console_logs` | 讀取 error / warning / info log |
| `send_console_log` | 發送標記訊息（debug 輔助） |
| `unity://tests/EditMode` | 列出 EditMode 測試清單 |
| `unity://tests/PlayMode` | 列出 PlayMode 測試清單 |
| `unity://logs/*` | 快速瀏覽 Console log |

## 核心規則 (Core Rules)

1. **編譯優先**：修改 C# 後必須先 `recompile_scripts`，確認無編譯錯誤才能執行測試。
2. **EditMode 優先原則**：預設使用 EditMode 測試。僅在下方列出的 4 種場景才考慮 PlayMode，且必須通過 Pre-flight Checklist。
3. **架構引導**：建議使用者將可測試邏輯與 MonoBehaviour 分離，讓 EditMode 測試覆蓋更多場景。
4. **測試篩選**：使用 `testFilter` 縮小範圍，避免跑全部測試浪費時間。
5. **Log 閱讀策略**：預設 `includeStackTrace: false` 節省 token；先看 error，再看 warning；必要時才開啟 stack trace。
6. **迭代上限**：連續 3 次 compile→test 循環仍失敗時，暫停並向使用者報告分析結果，不可繼續盲目重試。

## 核心流程

```
修改 C# 代碼
    │
    ▼
recompile_scripts ──→ 有編譯錯誤？──→ 讀取錯誤訊息 → 修正 → 重新編譯
    │
    ▼ (編譯成功)
run_tests (預設 EditMode；僅例外用 PlayMode)
    │
    ▼
有失敗測試？──→ 分析失敗原因 → 修正 → 回到 recompile
    │
    ▼ (全部通過)
get_console_logs ──→ 有 warning/error？──→ 分析 → 修正
    │
    ▼
完成 ✓
```

## EditMode vs PlayMode 差異

| | EditMode | PlayMode |
|--|----------|----------|
| `.asmdef` 的 `includePlatforms` | `["Editor"]` | `[]`（空陣列） |
| `UnityEditor.*` API | 可用 | **不可用（compile error）** |
| 可引用的 assembly | Editor + Runtime | **僅 Runtime** |
| 測試 attribute | `[Test]` | `[Test]` + `[UnityTest]` (return `IEnumerator`) |

### AI Agent 常見 PlayMode 編譯失敗原因

1. **在 PlayMode 測試中 `using UnityEditor`** — player 環境無此命名空間
2. **`.asmdef` 引用了 `*.Editor` assembly** — PlayMode 無法解析 Editor-only 引用
3. **混用 `async/await` 與 `[UnityTest]` 的 `IEnumerator`** — coroutine 模式不相容
4. **目標 runtime code 在 `Assembly-CSharp`（無 `.asmdef`）** — PlayMode test `.asmdef` 無法明確引用

## 架構建議：讓更多邏輯可 EditMode 測試

將可測試邏輯從 MonoBehaviour 分離，降低 PlayMode 依賴：

```csharp
// ✅ 純邏輯類 — EditMode 可測試
public class DamageCalculator {
    public int Calculate(int attack, int defense) => Mathf.Max(1, attack - defense);
}

// ✅ MonoBehaviour 做薄 delegate — 不需要對它寫測試
public class Enemy : MonoBehaviour {
    private DamageCalculator _calc = new();
    public void TakeDamage(int attack) => hp -= _calc.Calculate(attack, defense);
}
```

**決策流程**：

```
需要測試
    │
    ▼
能否用 EditMode 測試？──→ 是 ──→ EditMode（預設路線）
    │
    ▼ 否
真的需要 MonoBehaviour 生命週期 / Coroutine / Physics / 場景載入？
    │
    ▼ 是
PlayMode（例外路線，需通過 Pre-flight Checklist）
```

## PlayMode 適用場景（僅限以下情況）

| 場景 | 為什麼必須 PlayMode |
|------|-------------------|
| MonoBehaviour 生命週期（`Start`, `Update`, `OnDestroy`） | 需要 Play Mode 才會觸發回呼 |
| Coroutine（`yield return`） | 需要 MonoBehaviour 驅動 coroutine |
| Physics（碰撞偵測、Raycast） | 需要 Physics engine update loop |
| 實際場景載入 + UI 互動測試 | 需要完整 runtime 環境 |

## PlayMode Pre-flight Checklist

若判斷確實需要 PlayMode 測試，**必須依序驗證**以下項目才能撰寫測試代碼：

1. **確認 PlayMode test `.asmdef` 存在**
   - `includePlatforms` 必須為 `[]`（空陣列）
   - 若不存在，使用 Unity 的「Create Test Assembly Folder」功能建立

2. **確認 `.asmdef` 僅引用 runtime assembly**
   - 不可引用任何 `*.Editor` assembly

3. **確認 `precompiledReferences`**
   - 需含 `nunit.framework.dll`

4. **確認 `defineConstraints`**
   - 需含 `UNITY_INCLUDE_TESTS`

5. **確認目標 runtime code 的 `.asmdef` 名稱**
   - 查詢專案中的 `.asmdef`，確認 PlayMode test `.asmdef` 可引用目標 assembly

6. **代碼中無 `using UnityEditor`**
   - PlayMode 在 player context 編譯，Editor API 不可用

7. **`[UnityTest]` return type 為 `IEnumerator`**
   - 不可用 `void` 或 `Task`

> **重要**：若任一項不通過，應暫停並向使用者說明，而非嘗試自行修正（避免連鎖錯誤）。

## Log 閱讀策略

### 預設參數

- `includeStackTrace: false` — 節省 80-90% token，大多數情況僅需錯誤訊息本身
- `logType: "error"` — 優先查看 error，確認無 error 後再看 warning

### 進階策略

- **分頁**：log 量大時使用 `limit` + `offset` 分頁讀取，避免一次取太多
- **開啟 stack trace**：僅在錯誤訊息無法定位問題時才設 `includeStackTrace: true`
- **debug 標記**：使用 `send_console_log` 發送標記訊息，方便在 log 中定位特定時間點
- **logType 篩選順序**：error → warning → info（逐步擴大範圍）

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `unity-mcp-workflow` | **雙向**：引用其錯誤處理表、MCP 工具注意事項；同時擴充它（修改後驗證規則 + EditMode 優先原則） |
| `test-engineer` | **引用**：測試文件生成格式（`TestDoc_*.md`）、測試策略路由 |
| `unity-ui-builder` | **間接受益**：透過 `unity-mcp-workflow` 的修改後驗證規則，建構涉及 C# 時自動 recompile |

## 禁止事項 (Don'ts)

1. 不要修改 C# 後不執行 `recompile_scripts` 就直接跑測試
2. 不要在未確認編譯通過前執行 `run_tests`
3. 不要預設使用 PlayMode — 除非符合上述 4 種場景且通過 Pre-flight Checklist
4. 不要在 PlayMode 測試中 `using UnityEditor`
5. 不要在 PlayMode test `.asmdef` 引用 `*.Editor` assembly
6. 不要 `get_console_logs` 時預設開啟 `includeStackTrace`（浪費 token）
7. 不要連續 3 次 compile→test 循環失敗後繼續盲目重試 — 暫停並報告
