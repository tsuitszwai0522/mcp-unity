---
name: test-engineer
description: 用於為專案中的不同子系統編寫單元測試或整合測試，並生成測試說明文件。
---

# Test Engineer Protocol

此 Skill 負責為專案生成高品質的測試代碼，並同步產出測試說明文檔。

## 1. 環境識別 (Context Detection)
在生成測試前，**必須**依照配置的測試策略路由 (`{{config.testStrategies}}`) 鎖定測試策略。

若未配置，則依照路徑特徵判斷：
- 包含 `/SharedLib/` 或共用資料夾 → 純 C# 測試
- 包含 `/Unity/` 或繼承 MonoBehaviour → Unity Test Framework
- 包含 `/Server/` 且為 ASP.NET Core → xUnit
- 包含 `/Android/` → JUnit
- 包含 `/iOS/` → XCTest

## 2. 測試策略路由 (Strategy Routing)

### A. 純 C# 專案 (SharedLib / .NET)
* **框架**：xUnit 或 NUnit。
* **關鍵檢查**：
  - Pure C# Check（禁止 Unity/平台特定 API）
  - Serialization Test（確保 DTO 格式正確）

### B. Unity 專案
* **框架**：Unity Test Framework (UTF)。
* **策略選擇**：
  1. **標準模式 (有 asmdef)**：`Assets/Tests/EditMode/` 或 `Assets/Tests/PlayMode/`
  2. **簡易模式 (無 asmdef)**：`Assets/{Project}/Tests/Editor/`
* **Input 策略**：
  - 邏輯測試：使用介面隔離 (Interface Abstraction)
  - 整合測試：使用 InputTestFixture

### C. ASP.NET Core
* **框架**：xUnit
* **類型**：Integration Tests, Unit Tests

### D. Mobile (Android / iOS)
* **框架**：JUnit (Android) / XCTest (iOS)

## 3. 執行流程 (Execution Workflow)

當使用者要求「寫測試」時，請嚴格遵守以下步驟：

### 步驟一：生成測試代碼 (Code Generation)
1.  根據 **策略路由**，將測試代碼寫入對應的專案目錄中。
2.  **禁止**在對話視窗中顯示測試代碼的完整內容。

### 步驟二：生成說明文件 (Documentation)
1.  **建立文件**：在 `doc/test/` 目錄下建立說明檔。
    * 命名規則：`TestDoc_{YYYYMMDD}_{TargetName}.md`
    * 若目錄不存在，請先建立。
2.  **撰寫內容**：
    * **測試目標 (Target)**：說明被測試的類別或功能。
    * **檔案位置 (File Location)**：列出剛剛生成的測試代碼路徑。
    * **測試覆蓋範圍 (Coverage)**：
        * 列出包含的 Test Case
        * 說明覆蓋了哪些 Edge Case
    * **執行指引 (How to Run)**：針對該環境提供執行指令。
    * **依賴需求 (Dependencies)**：是否需要安裝特定的 Mock 套件？

### 步驟三：回報狀態 (Notification)
僅回覆使用者：
> 「測試代碼已生成於專案目錄，詳細說明請參閱：`doc/test/TestDoc_xxxx.md`。」

## 4. 自我檢核 (Self-Correction)
* **環境隔離檢查**：確保測試沒有用到不應依賴的 API。
* **Asmdef 檢查**：Unity 測試資料夾需要有正確的 Assembly Definition。
* **路徑正確性**：確保文件與代碼分別位於正確的目錄。

## 觸發時機 (When to use)
* 使用者說：「幫我寫測試」
* 使用者說：「這個類別需要單元測試」
* 使用者說：「生成 Integration Test」

## 禁止事項 (Don'ts)
1. ❌ 在對話視窗顯示完整測試代碼
2. ❌ 不生成測試說明文檔
3. ❌ 混淆測試文件與代碼的存放位置
4. ❌ 測試中使用不應依賴的平台 API
