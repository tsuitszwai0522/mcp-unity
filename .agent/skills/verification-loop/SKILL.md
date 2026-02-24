---
name: verification-loop
description: 變更驗證迴圈：在代碼修改後執行六階段自動化檢查（Build → Type Check → Lint → Test → Security Scan → Diff Review），確保交付品質。
---

# Verification Loop

此 Skill 在代碼修改完成後執行系統化的驗證流程，涵蓋六個階段。結果直接在對話中輸出（chat-first），需要時可匯出至 `doc/codeReview/`。

## 核心規則 (Core Rules)

1. **語言要求**：所有報告與溝通必須使用 **zh-TW**。
2. **Chat-first 輸出**：驗證結果直接在對話中輸出。僅在使用者要求「匯出」或「存檔」時，才寫入 `doc/codeReview/Verify_{YYYYMMDD}_{Scope}.md`。
3. **快速失敗 (Fail Fast)**：任一階段失敗時，立即停止後續階段，報告錯誤並協助修正。修正後從失敗的階段重新開始。
4. **技術棧自動偵測**：根據專案檔案自動判斷技術棧，選用對應的指令集。
5. **僅驗證變更範圍**：以 `git diff` 為基準，不對未變更的代碼進行驗證。
6. **迭代上限**：連續 3 次完整迴圈仍失敗時，暫停並向使用者報告分析結果，不可繼續盲目重試。

## 技術棧偵測 (Stack Detection)

| 偵測依據 | 技術棧 | Build 指令 | Type Check | Lint |
|----------|--------|-----------|------------|------|
| `*.csproj` + Unity MCP 可用 | Unity C# | `recompile_scripts` (MCP) | 含於 Build | Roslyn analyzers（若可用）|
| `*.csproj` / `*.sln`（無 Unity） | .NET | `dotnet build` | 含於 Build | `dotnet format --verify-no-changes` |
| `package.json` + `tsconfig.json` | Node.js (TS) / React (TS) | `npm run build` | `npx tsc --noEmit` | `npm run lint` |
| `package.json`（無 tsconfig） | Node.js (JS) / React (JS) | `npm run build` | N/A | `npm run lint` |
| 以上皆不符合 | **Unknown** | 見下方 Fallback 流程 | | |

> **混合專案**：若偵測到多個技術棧，依序對各棧執行驗證。

### Unknown Stack Fallback

當專案不符合上方任何已知技術棧時，執行以下兩階段 fallback：

**第一階段：通用偵測**

嘗試從專案中的建構描述檔推斷指令：

| 檔案 | 推斷方式 |
|------|---------|
| `Makefile` | Build: `make`；Test: `make test`；Lint: `make lint`（若 target 存在） |
| `Dockerfile` | 僅標記為容器化專案，不自動執行 Docker build |
| `build.gradle` / `pom.xml` | Java — Build: `./gradlew build` / `mvn compile`；Test: `./gradlew test` / `mvn test` |
| `Cargo.toml` | Rust — Build: `cargo build`；Test: `cargo test`；Lint: `cargo clippy` |
| `go.mod` | Go — Build: `go build ./...`；Test: `go test ./...`；Lint: `golangci-lint run`（若可用） |
| `pyproject.toml` / `setup.py` / `requirements.txt` | Python — Test: `pytest`；Lint: `ruff check` 或 `flake8`（若可用） |

- 對每個推斷出的指令，先確認工具已安裝（`which` / `command -v`），不可用則標記 SKIP。
- 成功推斷至少一個指令 → 使用推斷結果繼續執行六階段流程。

**第二階段：詢問使用者**

若通用偵測也無法推斷任何指令，**必須**暫停並詢問使用者：

> 「無法自動偵測此專案的技術棧。請提供以下指令（可留空跳過）：」
> 1. Build 指令
> 2. Type Check 指令
> 3. Lint 指令
> 4. Test 指令

收到回覆後，使用使用者提供的指令繼續執行。Phase 5（Security Scan）和 Phase 6（Diff Review）始終可執行，不受技術棧影響。

## 執行流程 (Workflow)

### Phase 1: Build

驗證代碼可成功編譯/建構。

| 技術棧 | 指令 |
|--------|------|
| Unity C# | `recompile_scripts` (MCP)；若 MCP 不可用則 `dotnet build` |
| .NET | `dotnet build` |
| Node.js / React | `npm run build`（若 `package.json` 有 `build` script） |

- 若無 build script，跳過此階段。

### Phase 2: Type Check

靜態型別驗證。

| 技術棧 | 指令 |
|--------|------|
| Unity C# / .NET | N/A（已含於 Phase 1 Build） |
| TypeScript | `npx tsc --noEmit` |
| JavaScript | N/A（跳過） |

### Phase 3: Lint

程式碼風格與品質檢查。

| 技術棧 | 指令 |
|--------|------|
| Unity C# / .NET | `dotnet format --verify-no-changes`（若可用）；否則跳過 |
| Node.js / React | `npm run lint`（若 `package.json` 有 `lint` script） |

- 若無 lint 指令，跳過此階段並在報告中標註。

### Phase 4: Test

路由至 `{{config.testStrategies}}` 執行測試。

| 技術棧 | 指令 |
|--------|------|
| Unity C# | `run_tests` (MCP)，預設 EditMode，遵循 `unity-test-debug` 的 EditMode 優先原則 |
| .NET | `dotnet test` |
| Node.js / React | `npm test`（若 `package.json` 有 `test` script） |

- 使用測試篩選 (filter) 僅執行與變更相關的測試。
- 若無測試可執行，在報告中標註並建議補充測試。

### Phase 5: Security Scan

僅掃描 `git diff` 範圍內的變更，檢查常見安全問題。

**通用檢查項**：
- 硬編碼的密鑰、Token、密碼（regex 掃描）
- 新增的 `TODO`/`HACK`/`FIXME` 標記

**技術棧專屬檢查**：

| 技術棧 | 檢查項 |
|--------|--------|
| Unity C# / .NET | 不安全的反序列化、SQL 拼接、`Process.Start` 無驗證 |
| Node.js / React | `eval()`、`innerHTML` 賦值、未驗證的 `req.body` 直接使用、`child_process.exec` 拼接 |

### Phase 6: Diff Review

最終差異審閱。

1. **執行 `git diff --stat`**：確認變更範圍符合預期。
2. **意外變更偵測**：是否有不屬於本次任務的檔案被修改？
3. **TODO/FIXME 審計**：新增的 TODO 是否有對應的追蹤？
4. **大型變更警告**：單一檔案變更超過 200 行時發出警告。
5. **console.log / Debug.Log 殘留**：檢查是否有 debug 輸出未清理。

## 驗證報告格式

```markdown
## Verification Report — {Scope}

| Phase | Status | Detail |
|-------|--------|--------|
| Build | PASS / FAIL / SKIP | ... |
| Type Check | PASS / FAIL / SKIP / N/A | ... |
| Lint | PASS / FAIL / SKIP | ... |
| Test | PASS / FAIL / SKIP | X passed, Y failed |
| Security | PASS / WARN | ... |
| Diff Review | PASS / WARN | ... |

### Issues Found
- [ ] {issue description}

### Stack Detected
- {stack name} ({detection basis})
```

## 觸發時機 (When to use)

- 使用者說：「驗證一下」「跑一下驗證」「check 一下」
- 使用者說：「確認沒問題再提交」「準備 commit 前檢查」
- 完成一輪代碼修改後，主動建議執行驗證
- 使用者說：「跑完整流程」「全部檢查一遍」

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `test-engineer` | **路由**：Phase 4 Test 路由至 `{{config.testStrategies}}`，與 test-engineer 共用測試策略 |
| `unity-test-debug` | **委派**：Unity 專案的 Phase 1 Build + Phase 4 Test 委派其處理（recompile → run_tests 流程） |
| `code-review-generator` | **銜接**：驗證全部通過後，建議使用者執行 code-review-generator 準備 PR |
| `bug-fix-protocol` | **前置**：bug-fix-protocol 修復完成後，建議執行 verification-loop 驗證修復結果 |

## 禁止事項 (Don'ts)

1. ❌ 跳過 Phase 1 Build 直接執行測試
2. ❌ 對未變更的代碼執行安全掃描（浪費時間且產生噪音）
3. ❌ 某一階段失敗後繼續執行後續階段（必須 Fail Fast）
4. ❌ 未經使用者要求就將報告寫入檔案（預設 chat-first）
5. ❌ 連續 3 次完整迴圈失敗後繼續盲目重試
6. ❌ 在無 build/lint/test script 時硬執行指令導致錯誤
