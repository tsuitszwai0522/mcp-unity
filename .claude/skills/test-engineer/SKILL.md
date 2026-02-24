---
name: test-engineer
description: Generate test code and documentation. Use when user asks to write tests, needs unit tests for a class, or wants integration tests.
---

# Test Engineer for Claude Code

此規則為 Claude Code 生成測試代碼與文檔的行為規範。

## 核心規則 (Core Rules)

1. **語言要求**：測試文檔必須使用 **zh-TW**。
2. **策略路由**：根據路徑或配置選擇正確的測試框架。
3. **文檔必生成**：每次生成測試代碼時，必須同步生成說明文檔。

## 觸發條件 (When to Activate)

- 「幫我寫測試」
- 「這個類別需要單元測試」
- 「生成 Integration Test」

## 執行流程 (Workflow)

1. **識別環境**：
   - 根據 `{{config.testStrategies}}` 或路徑特徵判斷測試策略。

2. **生成測試代碼**：
   - 寫入對應的專案目錄。
   - 不在對話視窗顯示完整代碼。

3. **生成說明文檔**：
   - 在 `doc/test/` 建立 `TestDoc_{YYYYMMDD}_{Target}.md`。
   - 包含：目標、位置、覆蓋範圍、執行指引、依賴需求。

4. **通知使用者**：告知文檔路徑。

## 輸出規範

| 項目 | 路徑 |
|------|------|
| 測試文檔 | `doc/test/TestDoc_{Date}_{Target}.md` |

## 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `verification-loop` | 被路由：Phase 4 透過 testStrategies 使用本 Skill 的測試策略 |
| `unity-test-debug` | 委派：Unity 測試由 unity-test-debug 處理 |
| `bug-fix-protocol` | 被引用：修復後可委派補充測試 |

## 禁止事項 (Don'ts)

1. ❌ 在對話視窗顯示完整測試代碼
2. ❌ 不生成測試說明文檔
3. ❌ 混淆存放位置
