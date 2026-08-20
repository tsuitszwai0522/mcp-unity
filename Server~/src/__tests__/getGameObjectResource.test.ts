import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { McpServer as RuntimeMcpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { registerGetGameObjectResource } from '../resources/getGameObjectResource.js';
import { ErrorType, McpUnityError } from '../utils/errors.js';
import { Logger, LogLevel } from '../utils/logger.js';
import { ConnectionState, McpUnity } from '../unity/mcpUnity.js';

describe('get_gameobject resource error contract', () => {
  const sendRequest = jest.fn<any>();
  const resource = jest.fn<any>();
  const server = { resource } as any;
  const mcpUnity = { sendRequest } as any;
  const logger = {
    info: jest.fn(),
    debug: jest.fn(),
    warn: jest.fn(),
    error: jest.fn()
  } as any;

  beforeEach(() => {
    jest.clearAllMocks();
  });

  function registeredHandler() {
    registerGetGameObjectResource(server, mcpUnity, logger);
    return resource.mock.calls[0][3] as (
      uri: URL,
      variables: Record<string, string>
    ) => Promise<unknown>;
  }

  it('maps JSON-RPC Unity errors to RESOURCE_FETCH while preserving the Unity type', async () => {
    sendRequest.mockRejectedValueOnce(new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      'The Prefab editing session was lost',
      { unityErrorType: 'prefab_session_lost_error' }
    ));
    const handler = registeredHandler();

    await expect(handler(
      new URL('unity://gameobject/Root'),
      { idOrName: 'Root' }
    )).rejects.toMatchObject({
      type: ErrorType.RESOURCE_FETCH,
      message: 'The Prefab editing session was lost',
      details: {
        unityErrorType: 'prefab_session_lost_error',
        upstreamErrorType: ErrorType.TOOL_EXECUTION,
      }
    });
  });

  it('retains RESOURCE_FETCH classification for a legacy success=false response', async () => {
    sendRequest.mockResolvedValueOnce({
      success: false,
      message: 'GameObject not found'
    });
    const handler = registeredHandler();

    await expect(handler(
      new URL('unity://gameobject/Missing'),
      { idOrName: 'Missing' }
    )).rejects.toMatchObject({
      type: ErrorType.RESOURCE_FETCH,
      message: 'GameObject not found'
    });
  });

  it('delivers a raw typed Unity failure through the MCP resource protocol', async () => {
    const unityErrorType = 'prefab_context_miss_error';
    const unityErrorMessage = 'GameObject is outside the active Prefab contents';
    const unity = new McpUnity(new Logger('ResourceProtocolTest', LogLevel.ERROR), {
      queueingEnabled: false
    });
    const connection = {
      isConnected: true,
      isConnecting: false,
      connectionState: ConnectionState.Connected,
      send: jest.fn((message: string) => {
        const request = JSON.parse(message) as { id: string };
        queueMicrotask(() => {
          (unity as any).handleMessage(JSON.stringify({
            jsonrpc: '2.0',
            id: request.id,
            error: {
              type: unityErrorType,
              message: unityErrorMessage,
              details: { prefabPath: 'Assets/Prefabs/Card.prefab' }
            }
          }));
        });
      }),
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

    const runtimeServer = new RuntimeMcpServer(
      { name: 'resource-error-test-server', version: '1.0.0' },
      { capabilities: { resources: {} } }
    );
    registerGetGameObjectResource(
      runtimeServer,
      unity,
      new Logger('ResourceProtocolTest', LogLevel.ERROR)
    );
    const client = new Client(
      { name: 'resource-error-test-client', version: '1.0.0' },
      { capabilities: {} }
    );
    const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
    await runtimeServer.connect(serverTransport);
    await client.connect(clientTransport);

    try {
      await expect(client.readResource({
        uri: 'unity://gameobject/S6SceneOnly'
      })).rejects.toMatchObject({
        message: expect.stringContaining(unityErrorMessage)
      });
      expect(connection.send).toHaveBeenCalledTimes(1);
      const request = JSON.parse(connection.send.mock.calls[0][0]) as {
        method: string;
        params: { idOrName: string };
      };
      expect(request).toMatchObject({
        method: 'get_gameobject',
        params: { idOrName: 'S6SceneOnly' }
      });
    } finally {
      await unity.stop();
      await client.close();
      await runtimeServer.close();
    }
  });
});
