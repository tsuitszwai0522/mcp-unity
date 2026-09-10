import { jest, describe, it, expect, beforeEach, afterEach } from '@jest/globals';
import { Logger, LogLevel } from '../utils/logger.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { McpUnity, ConnectionState } from '../unity/mcpUnity.js';
import { UnityConnection } from '../unity/unityConnection.js';
import { registerTransformTools } from '../tools/transformTools.js';
import path from 'path';
import { z } from 'zod';
// Intentional SDK-internal import: an SDK upgrade must fail loudly if production's converter moves.
import { toJsonSchemaCompat } from '@modelcontextprotocol/sdk/server/zod-json-schema-compat.js';

describe('McpUnityError integration', () => {
  it('should create proper error for connection issues', () => {
    const error = new McpUnityError(ErrorType.CONNECTION, 'Failed to connect to Unity');

    expect(error.type).toBe('connection_error');
    expect(error.message).toBe('Failed to connect to Unity');
  });

  it('should create proper error for timeout', () => {
    const error = new McpUnityError(ErrorType.TIMEOUT, 'Request timed out');

    expect(error.type).toBe('timeout_error');
  });

  it.each([
    'prefab_session_lost_error',
    'prefab_context_miss_error',
    'validation_error'
  ])('maps Unity error type %s into McpUnityError details', async (unityErrorType) => {
    const logger = new Logger('Test', LogLevel.ERROR);
    const unity = new McpUnity(logger, { queueingEnabled: false });
    const requestId = `typed-${unityErrorType}`;
    const responsePromise = new Promise((resolve, reject) => {
      (unity as any).pendingRequests.set(requestId, {
        resolve,
        reject,
        timeout: setTimeout(() => reject(new Error('unexpected timeout')), 1000)
      });
    });
    const rejection = expect(responsePromise).rejects.toMatchObject({
      type: ErrorType.TOOL_EXECUTION,
      message: `Unity reported ${unityErrorType}`,
      details: { unityErrorType }
    });

    (unity as any).handleMessage(JSON.stringify({
      jsonrpc: '2.0',
      id: requestId,
      error: {
        type: unityErrorType,
        message: `Unity reported ${unityErrorType}`
      }
    }));

    await rejection;
    await unity.stop();
  });
});

describe('Path handling in configuration', () => {
  it('should handle paths with spaces in config file path', () => {
    // The config path uses path.resolve which handles spaces correctly
    const pathWithSpaces = '/Users/John Doe/My Project/ProjectSettings/McpUnitySettings.json';

    // Verify path module handles spaces
    const resolved = path.resolve(pathWithSpaces);

    expect(resolved).toContain('John Doe');
    expect(resolved).toContain('My Project');
  });

  it('should handle Windows-style paths with spaces', () => {
    const windowsPath = 'C:\\Users\\John Doe\\My Project\\ProjectSettings';

    // path.normalize handles both styles
    const normalized = path.normalize(windowsPath);

    expect(normalized).toContain('John Doe');
  });

  it('should properly construct WebSocket URL', () => {
    // WebSocket URLs don't need special encoding for host/port
    const host = 'localhost';
    const port = 8090;
    const wsUrl = `ws://${host}:${port}/McpUnity`;

    expect(wsUrl).toBe('ws://localhost:8090/McpUnity');
  });

  it('should handle path.join with spaces', () => {
    const basePath = '/Users/John Doe/Projects';
    const subPath = 'My Unity Game';
    const fileName = 'settings.json';

    const fullPath = path.join(basePath, subPath, fileName);

    expect(fullPath).toContain('John Doe');
    expect(fullPath).toContain('My Unity Game');
    expect(fullPath).toContain('settings.json');
  });

  it('should handle path.resolve with relative paths containing spaces', () => {
    const cwd = '/Users/Test User/Current Dir';
    const relativePath = '../Other Project/file.txt';

    // path.resolve will work correctly with spaces
    const resolved = path.resolve(cwd, relativePath);

    expect(resolved).toContain('Test User');
  });
});

describe('Logger with path-related messages', () => {
  it('should log messages containing paths with spaces', () => {
    const logger = new Logger('Test', LogLevel.ERROR);
    const pathWithSpaces = '/Users/John Doe/My Project/file.txt';

    // Logger should handle any string including paths with spaces
    // This is a smoke test to ensure no exceptions are thrown
    expect(() => {
      logger.error(`Failed to read file: ${pathWithSpaces}`);
    }).not.toThrow();
  });
});

describe('Request timeout handling', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('rejects timed out requests without forcing a reconnect', async () => {
    const logger = new Logger('Test', LogLevel.ERROR);
    const unity = new McpUnity(logger, { queueingEnabled: false });
    const connection = {
      isConnected: true,
      isConnecting: false,
      connectionState: ConnectionState.Connected,
      send: jest.fn(),
      connect: jest.fn(),
      disconnect: jest.fn(),
      removeAllListeners: jest.fn(),
      forceReconnect: jest.fn(),
      getStats: jest.fn(() => ({
        state: ConnectionState.Connected,
        reconnectAttempt: 0,
        timeSinceLastPong: 0
      }))
    };

    (unity as any).connection = connection;

    const request = {
      id: 'request-timeout',
      method: 'run_tests',
      params: { mode: 'edit' }
    };

    const promise = unity.sendRequest(request, { timeout: 50 });
    const timeoutResult = expect(promise).rejects.toMatchObject({
      type: ErrorType.TIMEOUT,
      message: 'Request timed out'
    });

    expect(connection.send).toHaveBeenCalledWith(JSON.stringify(request));

    await jest.advanceTimersByTimeAsync(50);
    await timeoutResult;

    expect(connection.forceReconnect).not.toHaveBeenCalled();
    expect(unity.getConnectionStats().pendingRequests).toBe(0);
    expect(unity.connectionState).toBe(ConnectionState.Connected);

    await unity.stop();
  });

  it('rejects an in-flight request immediately when its connected socket is lost', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR), {
      queueingEnabled: false
    });
    const connection = {
      isConnected: true,
      isConnecting: false,
      connectionState: ConnectionState.Connected,
      send: jest.fn(),
      connect: jest.fn(),
      disconnect: jest.fn(),
      removeAllListeners: jest.fn(),
      forceReconnect: jest.fn(),
      getStats: jest.fn(() => ({
        state: ConnectionState.Connected,
        reconnectAttempt: 0,
        timeSinceLastPong: 0
      }))
    };
    (unity as any).connection = connection;

    const pending = unity.sendRequest({
      id: 'lost-in-flight',
      method: 'set_editor_state',
      params: { action: 'play' }
    }, { timeout: 180000, queueIfDisconnected: false });
    const rejection = expect(pending).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      details: {
        connectionErrorType: 'connection_lost_during_request',
        reason: 'Unity entered play mode'
      }
    });

    (unity as any).handleStateChange({
      previousState: ConnectionState.Connected,
      currentState: ConnectionState.Reconnecting,
      reason: 'Unity entered play mode'
    });

    await rejection;
    expect(unity.getConnectionStats().pendingRequests).toBe(0);
    await unity.stop();
  });
});

describe('Queued request deadlines', () => {
  beforeEach(() => {
    jest.useFakeTimers();
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  const createConnectedTransport = () => ({
    isConnected: true,
    isConnecting: false,
    connectionState: ConnectionState.Connected,
    send: jest.fn(),
    connect: jest.fn(),
    disconnect: jest.fn(),
    removeAllListeners: jest.fn(),
    forceReconnect: jest.fn(),
    getStats: jest.fn(() => ({
      state: ConnectionState.Connected,
      reconnectAttempt: 0,
      timeSinceLastPong: 0
    }))
  });

  it('uses RequestTimeoutSeconds as the default queue deadline when it exceeds 60 seconds', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    (unity as any).readConfigFileAsJson = jest.fn(async () => ({
      RequestTimeoutSeconds: '180'
    }));

    await (unity as any).parseAndSetConfig();
    (unity as any).commandQueue.enqueue({
      id: 'configured-timeout',
      request: { id: 'configured-timeout', method: 'run_tests', params: {} },
      resolve: jest.fn(),
      reject: jest.fn()
    });

    const queued = (unity as any).commandQueue.peek();
    expect(queued.timeout).toBeUndefined();
    expect(queued.queueTimeout).toBe(180000);
    expect(queued.deadline - queued.queuedAt).toBe(180000);

    await unity.stop();
  });

  it('uses the full per-command transport timeout after queue waiting', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = createConnectedTransport();
    (unity as any).connection = connection;

    let resolveFirst!: (value: unknown) => void;
    let rejectFirst!: (reason: unknown) => void;
    const firstResult = new Promise((resolve, reject) => {
      resolveFirst = resolve;
      rejectFirst = reject;
    });
    let resolveSecond!: (value: unknown) => void;
    let rejectSecond!: (reason: unknown) => void;
    const secondResult = new Promise((resolve, reject) => {
      resolveSecond = resolve;
      rejectSecond = reject;
    });

    (unity as any).commandQueue.enqueue({
      id: 'first',
      request: { id: 'first', method: 'first', params: {} },
      resolve: resolveFirst,
      reject: rejectFirst,
      timeout: 100
    });
    (unity as any).commandQueue.enqueue({
      id: 'second',
      request: { id: 'second', method: 'second', params: {} },
      resolve: resolveSecond,
      reject: rejectSecond,
      timeout: 100
    });

    await jest.advanceTimersByTimeAsync(60);
    const replay = (unity as any).replayQueuedCommands();
    expect(connection.send).toHaveBeenCalledTimes(1);

    await jest.advanceTimersByTimeAsync(30);
    (unity as any).handleMessage(JSON.stringify({
      jsonrpc: '2.0',
      id: 'first',
      result: { success: true }
    }));
    await Promise.resolve();
    expect(connection.send).toHaveBeenCalledTimes(2);

    let secondSettled = false;
    void secondResult.finally(() => { secondSettled = true; });
    await jest.advanceTimersByTimeAsync(11);

    await expect(firstResult).resolves.toEqual({ success: true });
    expect(secondSettled).toBe(false);
    (unity as any).handleMessage(JSON.stringify({
      jsonrpc: '2.0',
      id: 'second',
      result: { success: true }
    }));
    await expect(secondResult).resolves.toEqual({ success: true });
    await replay;
    await unity.stop();
  });

  it('clamps a backward wall-clock jump and still arms a bounded transport timeout', async () => {
    jest.setSystemTime(1000);
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = createConnectedTransport();
    (unity as any).connection = connection;

    const result = new Promise((resolve, reject) => {
      (unity as any).commandQueue.enqueue({
        id: 'clock-went-backward',
        request: { id: 'clock-went-backward', method: 'run_tests', params: {} },
        resolve,
        reject,
        timeout: 100
      });
    });
    const rejection = expect(result).rejects.toMatchObject({
      type: ErrorType.TIMEOUT,
      message: 'Request timed out'
    });

    jest.setSystemTime(0);
    const replay = (unity as any).replayQueuedCommands();
    expect(connection.send).toHaveBeenCalledTimes(1);
    await jest.advanceTimersByTimeAsync(100);

    await rejection;
    await replay;
    await unity.stop();
  });

  it('rechecks the queue deadline after serialization and immediately before send', async () => {
    jest.setSystemTime(0);
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = createConnectedTransport();
    (unity as any).connection = connection;
    const params = {
      toJSON: () => {
        jest.setSystemTime(50);
        return { serialized: true };
      }
    };
    const result = new Promise((resolve, reject) => {
      (unity as any).commandQueue.enqueue({
        id: 'expires-during-serialization',
        request: { id: 'expires-during-serialization', method: 'large_payload', params },
        resolve,
        reject,
        timeout: 50
      });
    });
    const rejection = expect(result).rejects.toMatchObject({
      type: ErrorType.TIMEOUT,
      message: 'Command expired after 50ms in queue'
    });

    await (unity as any).replayQueuedCommands();

    await rejection;
    expect(connection.send).not.toHaveBeenCalled();
    expect(unity.getQueueStats().expiredCount).toBe(1);
    await unity.stop();
  });

  it('does not send a drained command that expires behind an earlier replay', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = createConnectedTransport();
    (unity as any).connection = connection;

    const firstResult = new Promise((resolve, reject) => {
      (unity as any).commandQueue.enqueue({
        id: 'slow-first',
        request: { id: 'slow-first', method: 'slow_first', params: {} },
        resolve,
        reject,
        timeout: 100
      });
    });
    const expiredResult = new Promise((resolve, reject) => {
      (unity as any).commandQueue.enqueue({
        id: 'expires-second',
        request: { id: 'expires-second', method: 'expires_second', params: {} },
        resolve,
        reject,
        timeout: 50
      });
    });
    const expiredRejection = expect(expiredResult).rejects.toMatchObject({
      type: ErrorType.TIMEOUT,
      message: 'Command expired after 50ms in queue'
    });

    const replay = (unity as any).replayQueuedCommands();
    expect(connection.send).toHaveBeenCalledTimes(1);

    await jest.advanceTimersByTimeAsync(60);
    (unity as any).handleMessage(JSON.stringify({
      jsonrpc: '2.0',
      id: 'slow-first',
      result: { success: true }
    }));

    await expect(firstResult).resolves.toEqual({ success: true });
    await expiredRejection;
    await replay;
    expect(connection.send).toHaveBeenCalledTimes(1);

    await unity.stop();
  });

  it('restores the replay guard when a drained command callback throws', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = createConnectedTransport();
    connection.send.mockImplementationOnce(() => {
      throw new Error('transport send failed');
    });
    (unity as any).connection = connection;
    (unity as any).commandQueue.enqueue({
      id: 'throwing-reject',
      request: { id: 'throwing-reject', method: 'test', params: {} },
      resolve: jest.fn(),
      reject: () => { throw new Error('consumer rejection callback failed'); },
      timeout: 100
    });

    await expect((unity as any).replayQueuedCommands()).rejects.toThrow(
      'consumer rejection callback failed'
    );

    expect((unity as any).isReplayingQueue).toBe(false);
    await unity.stop();
  });
});

describe('Queue configuration precedence', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('preserves an explicit queue default after start reads Unity timeout settings', async () => {
    jest.spyOn(UnityConnection.prototype, 'connect').mockRejectedValue(
      new McpUnityError(ErrorType.CONNECTION, 'offline for test')
    );
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR), {
      queue: { defaultTimeout: 120000 }
    });
    (unity as any).readConfigFileAsJson = jest.fn(async () => ({
      RequestTimeoutSeconds: '180'
    }));

    await unity.start();
    (unity as any).commandQueue.enqueue({
      id: 'explicit-queue-timeout',
      request: { id: 'explicit-queue-timeout', method: 'test', params: {} },
      resolve: jest.fn(),
      reject: jest.fn()
    });

    const queued = (unity as any).commandQueue.peek();
    expect(queued.queueTimeout).toBe(120000);
    expect(queued.deadline - queued.queuedAt).toBe(120000);
    await unity.stop();
  });

  it('uses the 60-second request fallback when settings omit RequestTimeoutSeconds', async () => {
    jest.spyOn(UnityConnection.prototype, 'connect').mockRejectedValue(
      new McpUnityError(ErrorType.CONNECTION, 'offline for test')
    );
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    (unity as any).readConfigFileAsJson = jest.fn(async () => ({}));

    await unity.start();
    expect((unity as any).requestTimeout).toBe(60000);
    await unity.stop();
  });

  it.each([10, 30])(
    'keeps the 60-second queue floor when RequestTimeoutSeconds is %i',
    async (requestTimeoutSeconds) => {
      jest.spyOn(UnityConnection.prototype, 'connect').mockRejectedValue(
        new McpUnityError(ErrorType.CONNECTION, 'offline for test')
      );
      const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
      (unity as any).readConfigFileAsJson = jest.fn(async () => ({
        RequestTimeoutSeconds: String(requestTimeoutSeconds)
      }));

      await unity.start();
      (unity as any).commandQueue.enqueue({
        id: `queue-floor-${requestTimeoutSeconds}`,
        request: {
          id: `queue-floor-${requestTimeoutSeconds}`,
          method: 'test',
          params: {}
        },
        resolve: jest.fn(),
        reject: jest.fn()
      });

      const queued = (unity as any).commandQueue.peek();
      expect((unity as any).requestTimeout).toBe(requestTimeoutSeconds * 1000);
      expect(queued.queueTimeout).toBe(60000);
      await unity.stop();
    }
  );

  it('queues when the connect-first send rejects after connect resolves', async () => {
    const unity = new McpUnity(new Logger('Test', LogLevel.ERROR));
    const connection = {
      isConnected: false,
      isConnecting: false,
      connectionState: ConnectionState.Disconnected,
      send: jest.fn(),
      connect: jest.fn(async () => {}),
      disconnect: jest.fn(),
      removeAllListeners: jest.fn(),
      forceReconnect: jest.fn(),
      getStats: jest.fn(() => ({
        state: ConnectionState.Disconnected,
        reconnectAttempt: 0,
        timeSinceLastPong: 0
      }))
    };
    (unity as any).connection = connection;

    const pending = unity.sendRequest({
      id: 'connect-first-send-failure',
      method: 'test',
      params: {}
    });
    const eventualRejection = expect(pending).rejects.toMatchObject({
      type: ErrorType.CONNECTION
    });
    await Promise.resolve();
    await Promise.resolve();

    expect(unity.queuedCommandCount).toBe(1);
    await unity.stop();
    await eventualRejection;
  });
});

describe('Transform schema compatibility', () => {
  const mockSendRequest = jest.fn();
  const mockMcpUnity = { sendRequest: mockSendRequest };
  const mockLogger = {
    info: jest.fn(),
    debug: jest.fn(),
    warn: jest.fn(),
    error: jest.fn()
  };
  const mockServerTool = jest.fn();
  const mockServer = { tool: mockServerTool };

  function collectLocalPropertyRefs(node: unknown, refs: string[] = []): string[] {
    if (Array.isArray(node)) {
      for (const item of node) {
        collectLocalPropertyRefs(item, refs);
      }
      return refs;
    }

    if (!node || typeof node !== 'object') {
      return refs;
    }

    for (const [key, value] of Object.entries(node)) {
      if (key === '$ref' && typeof value === 'string' && value.startsWith('#/properties/')) {
        refs.push(value);
      }
      collectLocalPropertyRefs(value, refs);
    }

    return refs;
  }

  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers transform tools', () => {
    registerTransformTools(mockServer as any, mockMcpUnity as any, mockLogger as any);

    expect(mockServerTool).toHaveBeenCalledTimes(4);
    expect(mockServerTool).toHaveBeenCalledWith('move_gameobject', expect.any(String), expect.any(Object), expect.any(Function));
    expect(mockServerTool).toHaveBeenCalledWith('rotate_gameobject', expect.any(String), expect.any(Object), expect.any(Function));
    expect(mockServerTool).toHaveBeenCalledWith('scale_gameobject', expect.any(String), expect.any(Object), expect.any(Function));
    expect(mockServerTool).toHaveBeenCalledWith('set_transform', expect.any(String), expect.any(Object), expect.any(Function));
  });

  it('does not emit local #/properties refs for transform tool schemas', () => {
    registerTransformTools(mockServer as any, mockMcpUnity as any, mockLogger as any);

    for (const call of mockServerTool.mock.calls) {
      const paramsShape = call[2];
      const schemaJson = toJsonSchemaCompat(z.object(paramsShape), { strictUnions: true });
      const refs = collectLocalPropertyRefs(schemaJson);

      expect(refs).toEqual([]);
    }
  });
});
