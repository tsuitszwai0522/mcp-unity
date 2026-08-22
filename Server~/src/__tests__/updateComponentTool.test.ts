import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { registerUpdateComponentTool } from '../tools/updateComponentTool.js';

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

type ToolHandler = (params: {
  instanceId?: number;
  objectPath?: string;
  componentName: string;
  componentData?: Record<string, unknown>;
}) => Promise<any>;

const getToolHandler = (): ToolHandler => {
  registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);
  return (mockServerTool.mock.calls[0] as any)[3] as ToolHandler;
};

describe('update_component field write results', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('discloses enum underlying-value and partial-struct semantics', () => {
    registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('underlying enum value (not an index)');
    expect(description).toContain('invalid values are rejected with the valid names listed');
    expect(description).toContain('Partial struct writes');
    expect(description).toContain("unmentioned components are the type's default");
  });

  it('discloses reader-shaped enum conversion semantics', () => {
    registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('reader-shape {value,index,name}');
    expect(description).toContain('value is authoritative');
    expect(description).toContain('mismatched name or index metadata produces a warning');
  });

  it('discloses resolution order and path-specific array growth semantics', () => {
    registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('reflection fields/properties before SerializedProperty fallback');
    expect(description).toContain('reflection converter rejects a partial struct in a newly grown slot');
    expect(description).toContain('SerializedProperty fallback starts grown elements from type defaults');
    expect(description).toContain('array shrink discards removed elements with a warning');
  });

  it('returns isError with failedFields preserved in payload for field-level failure', async () => {
    const failedFields = [{
      field: 'enabled',
      reason: 'Failed to convert value to Boolean',
    }];
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      type: 'text',
      message: '1 field(s) succeeded, 1 field(s) failed',
      updatedFields: ['isTrigger'],
      failedFields,
      warnings: ['Field missingField was not found'],
    });

    const result = await getToolHandler()({
      instanceId: 42,
      componentName: 'BoxCollider',
      componentData: { enabled: 'yes-please', isTrigger: true },
    });

    expect(result.isError).toBe(true);
    expect(result.content[0]).toEqual({
      type: 'text',
      text: '1 field(s) succeeded, 1 field(s) failed',
    });
    expect(JSON.parse(result.content[1].text)).toEqual({
      message: '1 field(s) succeeded, 1 field(s) failed',
      updatedFields: ['isTrigger'],
      failedFields,
      warnings: ['Field missingField was not found'],
    });
  });

  it('still throws for a hard failure without failedFields', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      message: 'GameObject not found',
    });

    const request = getToolHandler()({
      instanceId: 404,
      componentName: 'BoxCollider',
    });

    await expect(request).rejects.toThrow(McpUnityError);
    await expect(request).rejects.toMatchObject({
      type: ErrorType.TOOL_EXECUTION,
      message: 'GameObject not found',
    });
  });

  it('includes payloadContent on the successful path', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      type: 'text',
      message: '1 field(s) succeeded, 0 field(s) failed',
      updatedFields: ['isTrigger'],
      failedFields: [],
    });

    const result = await getToolHandler()({
      objectPath: 'Probe/Collider',
      componentName: 'BoxCollider',
      componentData: { isTrigger: true },
    });

    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[1].text)).toEqual({
      message: '1 field(s) succeeded, 0 field(s) failed',
      updatedFields: ['isTrigger'],
      failedFields: [],
    });
  });
});
