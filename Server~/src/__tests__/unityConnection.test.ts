import { jest, describe, it, expect, beforeEach, afterEach } from '@jest/globals';

// Mock WebSocket before importing modules that use it
const mockWebSocketInstances: any[] = [];

const createMockWebSocket = (overrides: Record<string, any> = {}) => ({
  readyState: 1,
  onopen: null,
  onclose: null,
  onerror: null,
  onmessage: null,
  send: jest.fn(),
  close: jest.fn(),
  terminate: jest.fn(),
  ping: jest.fn(),
  on: jest.fn(),
  removeAllListeners: jest.fn(),
  ...overrides
});

const mockWebSocketConstructor = jest.fn(() => {
  const socket = createMockWebSocket();
  mockWebSocketInstances.push(socket);
  return socket;
});

const mockWebSocketModule = Object.assign(mockWebSocketConstructor, {
  CONNECTING: 0,
  OPEN: 1,
  CLOSING: 2,
  CLOSED: 3
});

jest.unstable_mockModule('ws', () => ({
  default: mockWebSocketModule,
  WebSocket: mockWebSocketModule
}));

// Dynamic imports after mocking
const { UnityConnection, ConnectionState } = await import('../unity/unityConnection');
const { Logger, LogLevel } = await import('../utils/logger');
const { McpUnityError, ErrorType } = await import('../utils/errors');

// Type imports
import type { ConnectionStateChange } from '../unity/unityConnection';

// Create a logger that doesn't output anything (for testing)
const createTestLogger = () => {
  process.env.LOGGING = 'false';
  process.env.LOGGING_FILE = 'false';
  return new Logger('Test', LogLevel.ERROR);
};

describe('UnityConnection', () => {
  let connection: InstanceType<typeof UnityConnection>;
  let testLogger: InstanceType<typeof Logger>;

  beforeEach(() => {
    testLogger = createTestLogger();
    mockWebSocketConstructor.mockImplementation(() => {
      const socket = createMockWebSocket();
      mockWebSocketInstances.push(socket);
      return socket;
    });
    mockWebSocketConstructor.mockClear();
    mockWebSocketInstances.length = 0;

    connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      clientName: 'TestClient',
      minReconnectDelay: 100,
      maxReconnectDelay: 1000,
      heartbeatInterval: 0
    });
  });

  afterEach(() => {
    connection.disconnect();
    jest.clearAllMocks();
  });

  describe('Initial State', () => {
    it('should start in disconnected state', () => {
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });

    it('should not be connected initially', () => {
      expect(connection.isConnected).toBe(false);
    });

    it('should not be connecting initially', () => {
      expect(connection.isConnecting).toBe(false);
    });

    it('should have -1 for timeSinceLastPong before any connection', () => {
      expect(connection.timeSinceLastPong).toBe(-1);
    });
  });

  describe('State Change Events', () => {
    it('should emit stateChange event when connect is called', (done) => {
      let firstEvent = true;
      connection.on('stateChange', (change: ConnectionStateChange) => {
        // Only check the first state change event
        if (firstEvent && change.currentState === ConnectionState.Connecting) {
          firstEvent = false;
          expect(change.previousState).toBe(ConnectionState.Disconnected);
          expect(change.currentState).toBe(ConnectionState.Connecting);
          done();
        }
      });

      connection.connect().catch(() => {});
    });

    it('should include reason in state change', (done) => {
      let eventReceived = false;
      connection.on('stateChange', (change: ConnectionStateChange) => {
        if (!eventReceived && change.currentState === ConnectionState.Connecting) {
          eventReceived = true;
          expect(change.reason).toBeDefined();
          done();
        }
      });

      connection.connect().catch(() => {});
    });
  });

  describe('Configuration', () => {
    it('should update configuration dynamically', () => {
      connection.updateConfig({ heartbeatInterval: 60000 });
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });
  });

  describe('getStats', () => {
    it('should return correct stats in initial state', () => {
      const stats = connection.getStats();
      expect(stats.state).toBe(ConnectionState.Disconnected);
      expect(stats.reconnectAttempt).toBe(0);
      expect(stats.timeSinceLastPong).toBe(-1);
      expect(stats.isAwaitingPong).toBe(false);
    });
  });

  describe('Disconnect', () => {
    it('should set state to disconnected on manual disconnect', () => {
      connection.disconnect('Test disconnect');
      expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    });

    it('should emit stateChange event when disconnecting from connecting state', (done) => {
      // First start connecting, then disconnect
      connection.on('stateChange', (change: ConnectionStateChange) => {
        if (change.currentState === ConnectionState.Disconnected &&
            change.previousState !== ConnectionState.Disconnected) {
          done();
        }
      });

      // Start connection then immediately disconnect
      connection.connect().catch(() => {});
      // Give time for the connecting state to be set
      setTimeout(() => {
        connection.disconnect('Test disconnect');
      }, 10);
    });
  });

  describe('Send', () => {
    it('should throw error when not connected', () => {
      expect(() => connection.send('test')).toThrow(McpUnityError);
    });
  });

  describe('forceReconnect', () => {
    it('should trigger connecting state', () => {
      connection.forceReconnect();
      expect(connection.isConnecting).toBe(true);
    });
  });
});

describe('ConnectionState Enum', () => {
  it('should have correct values', () => {
    expect(ConnectionState.Disconnected).toBe('disconnected');
    expect(ConnectionState.Connecting).toBe('connecting');
    expect(ConnectionState.Connected).toBe('connected');
    expect(ConnectionState.Reconnecting).toBe('reconnecting');
  });
});

describe('Exponential Backoff Configuration', () => {
  it('should accept backoff configuration', () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      minReconnectDelay: 1000,
      maxReconnectDelay: 30000,
      reconnectBackoffMultiplier: 2
    });

    expect(connection.connectionState).toBe(ConnectionState.Disconnected);
    connection.disconnect();
  });
});

describe('Connection timeout handling', () => {
  beforeEach(() => {
    jest.useFakeTimers();
    mockWebSocketConstructor.mockImplementation(() => {
      const socket = createMockWebSocket({ readyState: 0 });
      mockWebSocketInstances.push(socket);
      return socket;
    });
    mockWebSocketConstructor.mockClear();
    mockWebSocketInstances.length = 0;
  });

  afterEach(() => {
    jest.useRealTimers();
  });

  it('uses a dedicated connect timeout instead of the request timeout', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 60000,
      connectTimeout: 250,
      minReconnectDelay: 100,
      maxReconnectDelay: 1000,
      heartbeatInterval: 0
    });

    const connectPromise = connection.connect();
    const connectResult = expect(connectPromise).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'Connection timeout'
    });

    await jest.advanceTimersByTimeAsync(250);

    await connectResult;
    expect(mockWebSocketConstructor).toHaveBeenCalledTimes(1);
    expect(connection.connectionState).toBe(ConnectionState.Reconnecting);

    connection.disconnect();
  });

  it('rejects a failed attempt after onclose changes state and keeps background reconnecting', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      minReconnectDelay: 100,
      maxReconnectDelay: 100,
      heartbeatInterval: 0
    });

    const connectPromise = connection.connect();
    const rejection = expect(connectPromise).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'connection refused'
    });

    mockWebSocketInstances[0].onclose({ code: 1006, reason: 'connection refused' });

    await rejection;
    expect(connection.connectionState).toBe(ConnectionState.Reconnecting);

    await jest.advanceTimersByTimeAsync(100);
    expect(mockWebSocketConstructor).toHaveBeenCalledTimes(2);

    connection.disconnect();
  });

  it('rejects the active attempt when disconnect closes its socket', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      heartbeatInterval: 0
    });
    const connectPromise = connection.connect();

    connection.disconnect('shutdown during connect');

    await expect(connectPromise).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'shutdown during connect'
    });
  });

  it('rejects on onerror immediately and preserves that first error through onclose', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      minReconnectDelay: 100,
      maxReconnectDelay: 100,
      heartbeatInterval: 0
    });
    connection.on('error', () => {});
    let observedError: InstanceType<typeof McpUnityError> | undefined;
    const rejected = connection.connect().catch((error) => {
      observedError = error;
    });

    mockWebSocketInstances[0].onerror({ message: 'socket error' });
    await rejected;
    expect(observedError).toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'Connection failed: socket error'
    });

    mockWebSocketInstances[0].onclose({ code: 1006, reason: 'socket closed' });

    expect(observedError).toMatchObject({
      message: 'Connection failed: socket error'
    });
    expect(connection.connectionState).toBe(ConnectionState.Reconnecting);

    connection.disconnect();
  });

  it('rejects the attempt before an unhandled error event can throw', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      heartbeatInterval: 0
    });
    const connectPromise = connection.connect();

    expect(() => {
      mockWebSocketInstances[0].onerror({ message: 'unhandled socket error' });
    }).toThrow('Connection failed: unhandled socket error');

    await expect(connectPromise).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'Connection failed: unhandled socket error'
    });
    connection.disconnect();
  });

  it('rejects a synchronous WebSocket constructor failure and keeps reconnecting', async () => {
    mockWebSocketConstructor.mockImplementationOnce(() => {
      throw new Error('constructor exploded');
    });
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      minReconnectDelay: 100,
      maxReconnectDelay: 100,
      heartbeatInterval: 0
    });

    const rejection = expect(connection.connect()).rejects.toMatchObject({
      type: ErrorType.CONNECTION,
      message: 'Connection failed: constructor exploded'
    });
    await rejection;

    expect(connection.connectionState).toBe(ConnectionState.Reconnecting);
    await jest.advanceTimersByTimeAsync(100);
    expect(mockWebSocketConstructor).toHaveBeenCalledTimes(2);
    connection.disconnect();
  });

  it('settles an opened attempt before a throwing state observer runs', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      heartbeatInterval: 0
    });
    const connectPromise = connection.connect();
    connection.on('stateChange', (change: ConnectionStateChange) => {
      if (change.currentState === ConnectionState.Connected) {
        throw new Error('observer exploded');
      }
    });

    expect(() => mockWebSocketInstances[0].onopen()).toThrow('observer exploded');

    await expect(connectPromise).resolves.toBeUndefined();
    connection.disconnect();
  });

  it('cancels the stable reset when heartbeat marks the connection stale', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      minReconnectDelay: 10000,
      maxReconnectDelay: 10000,
      heartbeatInterval: 0
    });
    (connection as any).reconnectAttempt = 3;
    const connectPromise = connection.connect();
    mockWebSocketInstances[0].onopen();
    await connectPromise;

    (connection as any).handleStaleConnection();
    expect(connection.getStats().reconnectAttempt).toBe(4);
    await jest.advanceTimersByTimeAsync(5000);

    expect(connection.getStats().reconnectAttempt).toBe(4);
    connection.disconnect();
  });

  it('resets reconnect attempts after a stable connection even with heartbeat disabled', async () => {
    const testLogger = createTestLogger();
    const connection = new UnityConnection(testLogger, {
      host: 'localhost',
      port: 8090,
      requestTimeout: 5000,
      connectTimeout: 1000,
      heartbeatInterval: 0
    });
    (connection as any).reconnectAttempt = 3;

    const connectPromise = connection.connect();
    mockWebSocketInstances[0].onopen();
    await connectPromise;

    expect(connection.getStats().reconnectAttempt).toBe(3);
    await jest.advanceTimersByTimeAsync(5000);
    expect(connection.getStats().reconnectAttempt).toBe(0);

    connection.disconnect();
  });
});
