import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { registerGetGameObjectsByNameTool } from '../tools/getGameObjectTool.js';

const mockSendRequest = jest.fn();
const mockMcpUnity = { sendRequest: mockSendRequest } as any;

const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;

const mockServerTool = jest.fn();
const mockServer = { tool: mockServerTool } as any;

describe('get_gameobjects_by_name', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers under the get_gameobjects_by_name name', () => {
    registerGetGameObjectsByNameTool(mockServer, mockMcpUnity, mockLogger);

    expect(mockServerTool).toHaveBeenCalledTimes(1);
    expect(mockServerTool).toHaveBeenCalledWith(
      'get_gameobjects_by_name',
      expect.any(String),
      expect.any(Object),
      expect.any(Function),
    );
    expect(mockLogger.info).toHaveBeenCalledWith(
      'Registering tool: get_gameobjects_by_name',
    );
  });

  it('forwards glob params to Unity using the tool name as the method', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      pattern: '*Card*',
      count: 0,
      truncated: false,
      gameObjects: [],
    });
    registerGetGameObjectsByNameTool(mockServer, mockMcpUnity, mockLogger);
    const handler = mockServerTool.mock.calls[0][3] as Function;

    await handler({
      name: '*Card*',
      includeInactive: true,
      maxDepth: 1,
      includeChildren: false,
      limit: 50,
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'get_gameobjects_by_name',
      params: {
        name: '*Card*',
        includeInactive: true,
        maxDepth: 1,
        includeChildren: false,
        limit: 50,
      },
    });
  });

  it('serializes the Unity response as JSON text content', async () => {
    const unityResponse = {
      success: true,
      pattern: 'Main Camera',
      count: 1,
      truncated: false,
      gameObjects: [{ name: 'Main Camera', path: 'Main Camera' }],
    };
    (mockSendRequest as any).mockResolvedValue(unityResponse);
    registerGetGameObjectsByNameTool(mockServer, mockMcpUnity, mockLogger);
    const handler = mockServerTool.mock.calls[0][3] as Function;

    const result = await handler({ name: 'Main Camera' });

    expect(result.content[0].type).toBe('text');
    const parsed = JSON.parse(result.content[0].text as string);
    expect(parsed).toEqual(unityResponse);
  });

  it('throws TOOL_EXECUTION error when Unity reports failure', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      message: "Parameter 'limit' must be between 1 and 1000",
    });
    registerGetGameObjectsByNameTool(mockServer, mockMcpUnity, mockLogger);
    const handler = mockServerTool.mock.calls[0][3] as Function;

    await expect(handler({ name: '*' })).rejects.toThrow(McpUnityError);
    await expect(handler({ name: '*' })).rejects.toMatchObject({
      type: ErrorType.TOOL_EXECUTION,
      message: expect.stringContaining('limit'),
    });
  });
});
