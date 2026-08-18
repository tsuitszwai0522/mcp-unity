import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { CallToolResultSchema } from '@modelcontextprotocol/sdk/types.js';
import { registerSendConsoleLogTool } from '../tools/sendConsoleLogTool.js';
import { registerReadSerializedFieldsTool } from '../tools/serializedFieldTools.js';
import { registerUIAutomationTools } from '../tools/uiAutomationTools.js';
import { installStructuredContentSeam } from '../utils/structuredContentSeam.js';

const mockSendRequest = jest.fn();
const mockMcpUnity = { sendRequest: mockSendRequest } as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;

type ToolHandler = (params: Record<string, unknown>) => Promise<Record<string, any>>;

const createServer = (withStructuredContentSeam = false) => {
  const handlers = new Map<string, ToolHandler>();
  const server = {
    tool: jest.fn((name: string, ...args: unknown[]) => {
      handlers.set(name, args.at(-1) as ToolHandler);
    }),
    registerTool: jest.fn(),
  } as any;

  if (withStructuredContentSeam) {
    installStructuredContentSeam(server);
  }

  return { server, handlers };
};

describe('SDK 1.30 CallToolResult validation fallbacks', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('uses text when Unity omits the content type', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Unity console log accepted',
    });
    const { server, handlers } = createServer();
    registerSendConsoleLogTool(server, mockMcpUnity, mockLogger);

    const result = await handlers.get('send_console_log')!({ message: 'hello' });

    expect(result.content[0]).toEqual({
      type: 'text',
      text: 'Unity console log accepted',
    });
    expect(CallToolResultSchema.safeParse(result).success).toBe(true);
  });

  it('uses a meaningful string when Unity omits the response message', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      type: 'text',
    });
    const { server, handlers } = createServer();
    registerSendConsoleLogTool(server, mockMcpUnity, mockLogger);

    const result = await handlers.get('send_console_log')!({ message: 'hello' });

    expect(result.content[0]).toEqual({
      type: 'text',
      text: 'Console log sent',
    });
    expect(CallToolResultSchema.safeParse(result).success).toBe(true);
  });
});

describe('structured payload completeness', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('keeps serialized field values in structuredContent', async () => {
    const fields = {
      m_AnchoredPosition: { x: 12, y: 34 },
      m_SizeDelta: { x: 320, y: 180 },
    };
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read RectTransform fields',
      instanceId: 48180,
      componentName: 'RectTransform',
      fields,
    });
    const { server, handlers } = createServer(true);
    registerReadSerializedFieldsTool(server, mockMcpUnity, mockLogger);

    const result = await handlers.get('read_serialized_fields')!({
      instanceId: 48180,
      componentName: 'RectTransform',
    });

    expect(result.structuredContent).toEqual({
      instanceId: 48180,
      componentName: 'RectTransform',
      fields,
      message: 'Read RectTransform fields',
    });
  });

  it('attaches explicit failure data when wait_for_condition returns isError', async () => {
    const error = { code: 'timeout', message: 'Condition timed out' };
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      condition: 'active',
      objectPath: 'Canvas/PlayButton',
      elapsed: 0.25,
      finalState: { active: false },
      error,
    });
    const { server, handlers } = createServer(true);
    registerUIAutomationTools(server, mockMcpUnity, mockLogger);

    const result = await handlers.get('wait_for_condition')!({
      objectPath: 'Canvas/PlayButton',
      condition: 'active',
      timeout: 0.25,
    });

    expect(result.isError).toBe(true);
    expect(result.structuredContent).toEqual({
      success: false,
      condition: 'active',
      objectPath: 'Canvas/PlayButton',
      elapsed: 0.25,
      finalState: { active: false },
      error,
    });
    expect(CallToolResultSchema.safeParse(result).success).toBe(true);
  });
});
