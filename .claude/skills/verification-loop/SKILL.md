---
name: verification-loop
description: Six-phase verification loop after code changes (Build → Type Check → Lint → Test → Security Scan → Diff Review). Use when user says "verify", "check before commit", or after completing code modifications.
---

# Verification Loop for Claude Code

此規則為 Claude Code 在代碼修改後執行驗證迴圈的行為規範。

## 觸發條件

- 使用者要求驗證、檢查、確認可提交
- 完成一輪代碼修改後主動建議

## 核心規則

1. **Chat-first 輸出**：結果直接在對話輸出。僅在使用者要求匯出時寫入 `doc/codeReview/Verify_{YYYYMMDD}_{Scope}.md`。
2. **Fail Fast**：任一階段失敗立即停止，報告錯誤並協助修正，修正後從失敗階段重新開始。
3. **技術棧自動偵測**：根據專案檔案判斷技術棧，選用對應指令。
4. **僅驗證變更範圍**：以 `git diff` 為基準。
5. **迭代上限**：連續 3 次完整迴圈失敗 → 暫停報告，不盲目重試。

## 技術棧偵測

| 偵測依據 | 技術棧 | Build | Type Check | Lint |
|----------|--------|-------|------------|------|
| `*.csproj` + Unity MCP | Unity C# | `recompile_scripts` | 含於 Build | Roslyn（若可用）|
| `*.csproj` / `*.sln` | .NET | `dotnet build` | 含於 Build | `dotnet format --verify-no-changes` |
| `package.json` + `tsconfig.json` | TS 專案 | `npm run build` | `npx tsc --noEmit` | `npm run lint` |
| `package.json`（無 tsconfig） | JS 專案 | `npm run build` | N/A | `npm run lint` |
| 以上皆不符合 | Unknown | 見 Fallback 流程 | | |

### Unknown Stack Fallback

1. **通用偵測**：從專案檔案推斷指令：
   - `Makefile` → `make` / `make test` / `make lint`（若 target 存在）
   - `build.gradle` / `pom.xml` → Java（gradle/maven 指令）
   - `Cargo.toml` → Rust（`cargo build/test/clippy`）
   - `go.mod` → Go（`go build/test ./...`）
   - `pyproject.toml` / `setup.py` → Python（`pytest` / `ruff check`）
   - 先確認工具已安裝（`command -v`），不可用則 SKIP
2. **詢問使用者**：若通用偵測也無法推斷，暫停詢問 Build / Type Check / Lint / Test 指令
3. Phase 5-6（Security Scan / Diff Review）始終可執行，不受技術棧影響

## 六階段流程

### Phase 1: Build
- Unity: `recompile_scripts` (MCP) / `dotnet build`
- .NET: `dotnet build`
- Node/React: `npm run build`（若有 script）

### Phase 2: Type Check
- C# / .NET: N/A（含於 Build）
- TypeScript: `npx tsc --noEmit`
- JavaScript: N/A

### Phase 3: Lint
- .NET: `dotnet format --verify-no-changes`（若可用）
- Node/React: `npm run lint`（若有 script）

### Phase 4: Test
- 路由至 `{{config.testStrategies}}`
- Unity: `run_tests` (MCP)，遵循 `unity-test-debug` EditMode 優先原則
- .NET: `dotnet test`
- Node/React: `npm test`（若有 script）
- 使用篩選僅跑變更相關測試

### Phase 5: Security Scan
僅掃描 `git diff` 範圍：
- 通用：硬編碼密鑰/Token、新增 TODO/HACK/FIXME
- C# / .NET：不安全反序列化、SQL 拼接、`Process.Start`
- Node/React：`eval()`、`innerHTML`、未驗證 `req.body`、`child_process.exec` 拼接

### Phase 6: Diff Review
- `git diff --stat` 確認變更範圍
- 意外變更偵測
- TODO/FIXME 審計
- 單檔超過 200 行變更警告
- console.log / Debug.Log 殘留檢查

## 報告格式

```
## Verification Report — {Scope}

| Phase | Status | Detail |
|-------|--------|--------|
| Build | PASS/FAIL/SKIP | ... |
| Type Check | PASS/FAIL/SKIP/N/A | ... |
| Lint | PASS/FAIL/SKIP | ... |
| Test | PASS/FAIL/SKIP | X passed, Y failed |
| Security | PASS/WARN | ... |
| Diff Review | PASS/WARN | ... |
```

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `test-engineer` | 路由：共用 `{{config.testStrategies}}` |
| `unity-test-debug` | 委派：Unity 的 Build + Test 流程 |
| `code-review-generator` | 銜接：驗證通過後建議準備 PR |
| `bug-fix-protocol` | 前置：修復完成後建議驗證 |

## 禁止事項

1. 不要跳過 Build 直接跑測試
2. 不要對未變更代碼執行安全掃描
3. 不要某階段失敗後繼續後續階段（Fail Fast）
4. 不要未經使用者要求就將報告寫入檔案
5. 不要連續 3 次失敗後繼續盲目重試
6. 不要在無 build/lint/test script 時硬執行指令
