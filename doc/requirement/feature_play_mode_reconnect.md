# Feature Design: Play Mode Transparent Reconnection

> 狀態：**已實作、已測試**
> 建立日期：2026-03-23
> 實作日期：2026-03-23
> 測試日期：2026-03-23
> 目標版本：v1.7.0

## 1. 需求概述

### 背景

當 AI agent 呼叫 `set_editor_state("play")` 進入 Play Mode 時，Unity 會觸發 Domain Reload，導致 WebSocket server 重啟。目前的行為：

1. `set_editor_state("play")` 送出 → Unity 設定 `isPlaying = true`
2. Unity 觸發 `ExitingEditMode` → `StopServer(code 4001)` → WebSocket 斷線
3. Response 來不及送回 → MCP tool call **timeout**
4. Node.js 偵測 4001 → 進入 3 秒 polling 重連
5. Domain Reload 完成 → Unity 重啟 server → Node.js 重連成功
6. AI agent 需要**額外**等待 + `get_editor_state` 確認連線恢復

這導致每次進入/退出 Play Mode 需要 2-3 次 tool call 才能確認就緒。

### 目標

讓 `set_editor_state("play"/"stop")` 成為**一次性操作**：呼叫一次就能拿到確定結果（Play Mode 就緒 + 連線恢復），無需額外的 wait + check 迴圈。

### 非目標

- 不改變 Domain Reload 的行為（那是 Unity 核心機制）
- 不改變 `set_editor_state("pause"/"unpause")` 的行為（不涉及 Domain Reload）
- 不改變 WebSocket 重連策略（已有 3 秒 play mode polling）

---

## 2. 現有架構分析

### WebSocket 重連機制（已實作）

| 機制 | 位置 | 說明 |
|------|------|------|
| Custom close code 4001 | `McpUnityServer.cs:StopServer()` | 告知 Node.js 是 Play Mode 斷線 |
| Play Mode fast polling | `unityConnection.ts:337` | 偵測 4001 後用 3 秒固定間隔（非指數退避） |
| 無上限重試 | `unityConnection.ts:326-334` | Play Mode 時跳過 maxReconnectAttempts 限制 |
| Command Queue | `mcpUnity.ts:337-353` | 斷線期間的 command 自動排隊 |
| Queue Replay | `mcpUnity.ts:228-256` | 重連後自動重送排隊的 command |
| State listener | `mcpUnity.ts:194-223` | Connection state change 通知機制 |

### 時間線分析

```
T+0.0s  set_editor_state("play") 送出
T+0.1s  Unity 收到，設定 isPlaying = true
T+0.2s  ExitingEditMode → StopServer(4001) → 連線斷開
T+0.3s  Node.js 偵測 4001，進入 play mode polling
T+0.5~2s Domain Reload 進行中
T+2~3s  EnteredPlayMode → StartServer()
T+3~6s  Node.js 下一次 3 秒 poll 成功重連
```

**關鍵觀察**：從呼叫到重連完成約 3-6 秒。MCP tool 的預設 timeout 是 10 秒，時間上綽綽有餘。問題不在 timeout 太短，而是 **handler 沒有等待重連**。

---

## 3. 設計方案

### 3.1 `McpUnity` 新增 `waitForConnection(timeoutMs)` 方法

利用已有的 `stateListeners` 機制，回傳一個 Promise，在 connection state 變成 `Connected` 時 resolve。

#### 介面

```typescript
/**
 * Wait for connection to be established or restored.
 * Resolves immediately if already connected.
 * @param timeoutMs Maximum time to wait in milliseconds (default: 30000)
 * @throws McpUnityError if timeout is reached
 */
public waitForConnection(timeoutMs?: number): Promise<void>
```

#### 實作邏輯

```typescript
public waitForConnection(timeoutMs: number = 30000): Promise<void> {
  // Already connected → resolve immediately
  if (this.isConnected) {
    return Promise.resolve();
  }

  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      cleanup();
      reject(new McpUnityError(
        ErrorType.CONNECTION,
        `Timeout waiting for connection after ${timeoutMs}ms`
      ));
    }, timeoutMs);

    const listener = (change: ConnectionStateChange) => {
      if (change.currentState === ConnectionState.Connected) {
        cleanup();
        resolve();
      } else if (change.currentState === ConnectionState.Disconnected) {
        cleanup();
        reject(new McpUnityError(
          ErrorType.CONNECTION,
          change.reason || 'Connection permanently lost'
        ));
      }
    };

    const cleanup = () => {
      clearTimeout(timeout);
      this.removeStateListener(listener);
    };

    this.addStateListener(listener);
  });
}
```

#### 需要的配套

`McpUnity` 目前有 `addStateListener()`，需確認是否有 `removeStateListener()`。若無，需新增。

### 3.2 `editorStateTools.ts` handler 改造

僅改動 `set_editor_state` handler，對 `play`/`stop` action 加入重連等待邏輯。

#### 行為定義

| Action | 是否觸發 Domain Reload | Handler 行為 |
|--------|----------------------|-------------|
| `play` | 是（Domain Reload enabled 時） | 等待重連後驗證 |
| `stop` | 是（Domain Reload enabled 時） | 等待重連後驗證 |
| `pause` | 否 | 直接回傳（不改動） |
| `unpause` | 否 | 直接回傳（不改動） |

#### 實作邏輯

```typescript
async (params: any) => {
  const action = params.action;

  try {
    // 嘗試正常送出（Domain Reload disabled 時會直接成功）
    const response = await mcpUnity.sendRequest({
      method: setStateName,
      params
    });

    if (!response.success) {
      throw new McpUnityError(ErrorType.TOOL_EXECUTION, response.message);
    }

    return {
      content: [{ type: 'text', text: response.message }],
      data: { state: response.state }
    };
  } catch (error) {
    // play/stop: 連線中斷是預期行為（Domain Reload）
    if (action === 'play' || action === 'stop') {
      logger.info(`Expected disconnection during '${action}', waiting for reconnection...`);

      // 等待重連（最多 30 秒）
      await mcpUnity.waitForConnection(30000);

      // 重連後驗證狀態
      const verifyResponse = await mcpUnity.sendRequest({
        method: getStateName,
        params: {}
      });

      if (verifyResponse.success) {
        const state = verifyResponse.state;
        return {
          content: [{
            type: 'text',
            text: `Editor state action '${action}' executed successfully`
          }],
          data: { state }
        };
      }

      throw new McpUnityError(
        ErrorType.TOOL_EXECUTION,
        `Action '${action}' completed but state verification failed`
      );
    }

    // pause/unpause 連線中斷不是預期行為，直接拋錯
    throw error;
  }
}
```

---

## 4. 邊界情境

### 4.1 Domain Reload Disabled

Unity 設定 `Enter Play Mode Options > Disable Domain Reload` 時：
- Server 不會停止 → WebSocket 不斷線 → `sendRequest` 直接成功
- Handler 走 try 正常路徑，不觸發 catch
- **無影響**

### 4.2 Unity Server 啟動失敗

Domain Reload 後 server 未能重啟（例如 port 被佔）：
- `waitForConnection(30000)` 在 30 秒後 timeout
- 回傳明確的 timeout 錯誤
- AI agent 可以決定重試或回報問題

### 4.3 Script Compilation Error

Domain Reload 中遇到 compilation error：
- Unity 停留在 compiling 狀態，不會進入 Play Mode
- Server 最終會重啟（`OnAfterAssemblyReload`），但 `isPlaying` 為 false
- `waitForConnection` 成功 → 驗證狀態時發現不是預期的 playing 狀態
- 回傳帶有實際 state 的結果，讓 AI agent 判斷

### 4.4 `set_editor_state("play")` 重複呼叫

Command Queue 會在重連後 replay 排隊的 command。但 `set_editor_state` 不應被 replay（已執行過），否則會 toggle 回 Edit Mode。

**解法**：`set_editor_state` 的 `sendRequest` 不使用 queue（`queueIfDisconnected: false`）。失敗後由 handler 層級處理重連等待，不走 command queue。

---

## 5. 檔案變更

### 修改檔案

| 檔案 | 變更 | 大小 |
|------|------|------|
| `Server~/src/unity/mcpUnity.ts` | 新增 `waitForConnection()` + `removeStateListener()` | ~30 行 |
| `Server~/src/tools/editorStateTools.ts` | `set_editor_state` handler 加入重連等待 | ~25 行 |

### 不需修改

| 檔案 | 原因 |
|------|------|
| `McpUnityServer.cs` | C# 側已有完整的 Play Mode 處理（4001 close code + auto restart） |
| `unityConnection.ts` | 重連策略不變（3 秒 play mode polling 已足夠） |
| `commandQueue.ts` | Command queue 機制不變 |

---

## 6. 測試計劃

### 6.1 基本測試

| # | 測試 | 預期結果 |
|---|------|---------|
| T1 | `set_editor_state("play")` — Domain Reload enabled | 一次呼叫成功回傳，state.isPlaying=true |
| T2 | `set_editor_state("stop")` — 從 Play Mode 退出 | 一次呼叫成功回傳，state.isPlaying=false |
| T3 | `set_editor_state("pause")` | 直接回傳（行為不變） |
| T4 | `set_editor_state("unpause")` | 直接回傳（行為不變） |

### 6.2 邊界測試

| # | 測試 | 預期結果 |
|---|------|---------|
| T5 | Domain Reload disabled + play | 直接成功（不觸發 catch 路徑） |
| T6 | Play 後立即呼叫其他 tool | Command queue 排隊 → 重連後 replay |
| T7 | 連續快速 play/stop | 每次都正確等待並回傳 |

---

## 7. 已知風險

| 風險 | 嚴重度 | 說明 | 緩解 |
|------|--------|------|------|
| 30 秒 timeout 太長 | 低 | 正常 Domain Reload 3-6 秒，30 秒是安全上限 | 可調整，但寧可長不可短 |
| `waitForConnection` 記憶體洩漏 | 低 | listener 未清理 | cleanup 函數確保 timeout/resolve/reject 都移除 listener |
| `recompile_scripts` 也會斷線 | 中 | Script recompilation 觸發 assembly reload，行為類似 | 未來可用相同模式處理 |
