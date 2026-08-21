import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import {
  registerGetGameObjectsByComponentTool,
  registerGetGameObjectsByNameTool,
} from '../tools/getGameObjectTool.js';

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
    const schema = mockServerTool.mock.calls[0][2];
    expect(schema.componentFilter.description).toContain('short or full');
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

describe('get_gameobjects_by_component', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers with a bounded schema and component ambiguity guidance', () => {
    registerGetGameObjectsByComponentTool(mockServer, mockMcpUnity, mockLogger);

    expect(mockServerTool).toHaveBeenCalledTimes(1);
    const [name, description, schema] = mockServerTool.mock.calls[0];
    expect(name).toBe('get_gameobjects_by_component');
    expect(description).toContain('derived');
    expect(schema.componentType.description).toContain('assembly-qualified');
    expect(schema.componentType.description).toContain('Ambiguous');
    expect(schema.limit.safeParse(1).success).toBe(true);
    expect(schema.limit.safeParse(1000).success).toBe(true);
    expect(schema.limit.safeParse(0).success).toBe(false);
    expect(schema.limit.safeParse(1001).success).toBe(false);
    expect(schema.maxDepth.safeParse(-1).success).toBe(true);
    expect(schema.maxDepth.safeParse(-2).success).toBe(false);
    expect(schema.compact.description).toContain('Default: true');
    expect(schema.compact.description).toContain('unless componentFilter is provided');
    expect(schema.componentFilter.description).toContain('short or full');
    expect(schema.componentFilter.description).toContain('automatically enables filtered detail');
  });

  it('forwards component query params to Unity using the tool name as the method', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      componentType: 'UnityEngine.Collider',
      count: 1,
      total: 1,
      truncated: false,
      gameObjects: [],
    });
    registerGetGameObjectsByComponentTool(mockServer, mockMcpUnity, mockLogger);
    const handler = mockServerTool.mock.calls[0][3] as Function;
    const params = {
      componentType: 'Collider',
      includeInactive: false,
      maxDepth: 1,
      includeChildren: true,
      limit: 50,
      compact: false,
      componentFilter: ['BoxCollider'],
    };

    await handler(params);

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'get_gameobjects_by_component',
      params,
    });
  });

  it('throws TOOL_EXECUTION error when Unity reports ambiguity', async () => {
    const unityError = new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      "Ambiguous component name 'Probe' matched 2 types",
      { unityErrorType: 'component_ambiguity_error' },
    );
    (mockSendRequest as any).mockRejectedValue(unityError);
    registerGetGameObjectsByComponentTool(mockServer, mockMcpUnity, mockLogger);
    const handler = mockServerTool.mock.calls[0][3] as Function;

    await expect(handler({ componentType: 'Probe' })).rejects.toBe(unityError);
  });
});
