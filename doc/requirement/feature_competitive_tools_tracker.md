# Implementation Tracker: 競品功能移植

> 設計文件：[feature_competitive_tools.md](./feature_competitive_tools.md)
> 建立日期：2026-03-06

## Phase 總覽

| Phase | 內容 | 狀態 |
|-------|------|------|
| Phase 1 | 基礎設施 + 核心工具 | ✅ 已完成（已測試） |
| Phase 2 | 資產與腳本管理 | ⬜ 待開始 |
| Phase 3 | 動態代碼執行 (Roslyn) | ⬜ 待開始 |

---

## Phase 1：基礎設施 + 核心工具

**狀態**：✅ 已完成（已測試）
**開始日期**：2026-03-06
**完成日期**：2026-03-06

### 任務清單

- [x] Play Mode Server 持續連線（功能六）
  - [x] 修改 `McpUnityServer.cs` 的 `OnPlayModeStateChanged`
  - [x] 新增 `IsDomainReloadDisabled()` helper
- [x] `screenshot_game_view` (C# + TS)
- [x] `screenshot_scene_view` (C# + TS)
- [x] `screenshot_camera` (C# + TS)
- [x] `get_editor_state` (C# + TS)
- [x] `set_editor_state` (C# + TS)
- [x] `get_selection` (C# + TS)
- [x] 註冊所有新 tool（McpUnityServer.cs + index.ts）
- [x] npm run build 確認編譯通過
- [x] Unity 編譯通過（0 errors）
- [x] 所有 tool 功能測試通過（via batch_execute）

### 完成事項

- Play Mode Server 持續連線：支援兩條路徑（Domain Reload 關閉 → 零斷線；Domain Reload 開啟 → EnteredPlayMode 自動重啟）
- 3 個 Screenshot Tools：Game View（ScreenCapture + Camera.main fallback）、Scene View（Scene camera render）、Camera（指定 camera render）
- 2 個 Editor State Tools：get_editor_state（查詢）、set_editor_state（play/pause/unpause/stop）
- 1 個 Get Selection Tool：讀取 hierarchy 和 project window 選擇
- TypeScript 側 screenshot 使用 MCP SDK image content type（非 text）
- 所有 C# tool 繼承 McpToolBase，sync 執行（IsAsync = false）

### 關鍵決策

1. **Screenshot 3 個 tool 合併一個 .cs 檔案**：`ScreenshotTools.cs` 包含 3 個 class + 1 個 helper class `ScreenshotHelper`
2. **Game View 截圖用 `ScreenCapture.CaptureScreenshotAsTexture()`**，Scene View 和 Camera 用 RenderTexture 方案
3. **Scene View 和 Camera 截圖有完整 cleanup**：還原 camera.targetTexture 和 RenderTexture.active，用 try-finally 確保
4. **Editor State + Get Selection 合併一個 .cs 檔案**：`EditorStateTool.cs` 包含 3 個 class（Unity 新檔案匯入有 caching 問題，合併解決）
5. **所有 screenshot tool 回傳 image type**，Node.js 側轉換為 MCP image content（`type: 'image'`）
6. [Test Fix] **Game View 截圖加入 Camera.main fallback**：`ScreenCapture.CaptureScreenshotAsTexture()` 在 Edit Mode 返回 null，改為 fallback 到 Camera.main render

### 修改清單

| 檔案 | 操作 | 說明 |
|------|------|------|
| `Editor/UnityBridge/McpUnityServer.cs` | 修改 | Play Mode 持續連線 + 新增 `IsDomainReloadDisabled()` + 註冊 6 個新 tool |
| `Editor/Tools/ScreenshotTools.cs` | 新增 | 3 個截圖 tool + ScreenshotHelper（含 Game View Camera.main fallback） |
| `Editor/Tools/EditorStateTool.cs` | 新增 | get_editor_state + set_editor_state + get_selection |
| `Server~/src/tools/screenshotTools.ts` | 新增 | 3 個截圖 tool（image content type） |
| `Server~/src/tools/editorStateTools.ts` | 新增 | get_editor_state + set_editor_state |
| `Server~/src/tools/getSelectionTool.ts` | 新增 | get_selection |
| `Server~/src/index.ts` | 修改 | 新增 3 個 import + 3 個 register 呼叫 |

### 測試結果

#### Edit Mode 測試

| Tool | 測試場景 | 結果 |
|------|---------|------|
| `get_editor_state` | 基本呼叫 | ✅ |
| `get_selection` | 無選擇 | ✅ |
| `get_selection` | 選中 Main Camera | ✅ |
| `set_editor_state` | 無效 action → error | ✅ |
| `set_editor_state` | pause / unpause | ✅ |
| `screenshot_game_view` | 320x240（Camera.main fallback） | ✅ |
| `screenshot_scene_view` | 320x240 | ✅ |
| `screenshot_camera` | By path | ✅ |
| `screenshot_camera` | By instance ID | ✅ |
| `screenshot_camera` | 無效 path → error | ✅ |

#### Play Mode 測試

| Tool | 測試場景 | 結果 |
|------|---------|------|
| `get_editor_state` | Play Mode 中查詢 | ✅ isPlaying=true |
| `screenshot_game_view` | Play Mode 960x540 | ✅ 捕獲實際遊戲畫面 |
| `screenshot_scene_view` | Play Mode 960x540 | ✅ |
| `screenshot_camera` | Play Mode 960x540 | ✅ |
| `set_editor_state` | Play → Pause | ✅ |
| `get_editor_state` | Paused 狀態查詢 | ✅ isPaused=true |
| `screenshot_game_view` | Paused 狀態截圖 | ✅ |
| `set_editor_state` | Unpause | ✅ |
| `set_editor_state` | Stop（回到 Edit Mode） | ✅ |
| `get_editor_state` | Stop 後查詢 | ✅ isPlaying=false |

#### Play Mode Server 持續連線測試

| 場景 | 結果 |
|------|------|
| Edit → Play Mode 期間 server 連線 | ✅ 零斷線 |
| Play Mode 期間所有 tool 可用 | ✅ |
| Play → Pause → Unpause 全程連線 | ✅ |
| Play → Stop 回到 Edit Mode | ✅ server 持續在線 |
| 完整 Edit → Play → Stop 循環 | ✅ |

### 注意事項給下一階段

1. **Phase 2 的 `manage_asset` 獨立於 Play Mode 功能**，可以直接實作
2. **Phase 2 的 `read_script` / `write_script` 也是 sync tool**，模式同 Phase 1
3. **Unity 新檔案匯入 caching 問題**：新建的 `.cs` 檔案可能不被 Unity 編譯器發現，解法是合併到已存在的檔案中，或刪除 Library 資料夾重新匯入
4. **`ScreenCapture.CaptureScreenshotAsTexture()` 在 Edit Mode 返回 null**：已加 Camera.main fallback，Play Mode 下正常使用 ScreenCapture（已驗證）
5. **Domain Reload 開啟路徑未獨立測試**：目前測試環境可能已關閉 Domain Reload，需另外開啟 Domain Reload 再測一次 Play/Stop 循環確認自動重啟機制
