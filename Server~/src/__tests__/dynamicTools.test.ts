import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerDynamicTools } from '../tools/dynamicTools.js';

const mockSendRequest = jest.fn();
const mockServerTool = jest.fn();
const mockSendToolListChanged = jest.fn();
const mockMcpUnity = { sendRequest: mockSendRequest } as any;
const mockServer = {
  tool: mockServerTool,
  server: { sendToolListChanged: mockSendToolListChanged },
} as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;

describe('dynamic tool payload preservation', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('keeps the message and exposes the complete external payload in MCP content', async () => {
    mockSendRequest.mockResolvedValueOnce({
      success: true,
      tools: [{
        name: 'cb_list_equipment',
        description: 'Lists equipment',
        parameterSchema: { type: 'object', properties: {} },
      }],
    });
    await registerDynamicTools(mockServer, mockMcpUnity, mockLogger);

    const response = {
      success: true,
      message: '找到 15 個裝備',
      equipment: [{ id: 'sword', attack: 10 }],
      total: 15,
    };
    mockSendRequest.mockResolvedValueOnce(response);
    const handler = mockServerTool.mock.calls[0][3] as Function;

    const result = await handler({});

    expect(result.content).toHaveLength(2);
    expect(result.content[0]).toEqual({ type: 'text', text: response.message });
    expect(JSON.parse(result.content[1].text)).toEqual(response);
    expect(result).not.toHaveProperty('data');
  });
});
