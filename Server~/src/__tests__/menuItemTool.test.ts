import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerMenuItemTool } from '../tools/menuItemTool.js';

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

type ToolHandler = (params: { menuPath: string }) => Promise<any>;

const getToolHandler = (): ToolHandler => {
  registerMenuItemTool(mockServer, mockMcpUnity, mockLogger);
  return (mockServerTool.mock.calls[0] as any)[3] as ToolHandler;
};

describe('execute_menu_item result forwarding', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('returns successful dispatch evidence in an uncapped JSON text payload', async () => {
    const response = {
      success: true,
      type: 'text',
      message: 'Successfully executed menu item: Assets/Refresh',
      dispatched: true,
      capturedLogs: [],
    };
    mockSendRequest.mockResolvedValue(response);

    const result = await getToolHandler()({ menuPath: 'Assets/Refresh' });

    expect(result.isError).toBeUndefined();
    expect(result.content[0]).toEqual({ type: 'text', text: response.message });
    expect(JSON.parse(result.content[1].text)).toEqual(response);
    expect(mockLogger.info).toHaveBeenCalledWith('Tool execution successful: execute_menu_item');
  });

  it('discloses the synchronous main-thread capture boundary', () => {
    getToolHandler();

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('synchronous main-thread');
    expect(description).toContain('delayCall');
    expect(description).toContain('background-thread');
    expect(description).toContain('post-return');
  });

  it('returns isError with the complete Unity failure payload instead of throwing', async () => {
    const uncappedLogMessage = `Unrelated asset import failed: ${'x'.repeat(21000)}`;
    const response = {
      success: false,
      type: 'text',
      message: "Menu item 'Assets/Refresh' dispatch was attempted, but 2 error(s) were logged during its execution.",
      error_code: 'menu_item_logged_errors',
      dispatched: true,
      capturedLogs: [
        { type: 'Error', message: uncappedLogMessage },
        { type: 'Assert', message: 'Importer assertion' },
      ],
    };
    mockSendRequest.mockResolvedValue(response);

    const result = await getToolHandler()({ menuPath: 'Assets/Refresh' });

    expect(result.isError).toBe(true);
    expect(result.content[0]).toEqual({ type: 'text', text: response.message });
    expect(JSON.parse(result.content[1].text)).toEqual(response);
    expect(mockLogger.info).not.toHaveBeenCalledWith('Tool execution successful: execute_menu_item');
    expect(mockLogger.error).toHaveBeenCalledWith(
      'Tool execution failed: execute_menu_item',
      { menuPath: 'Assets/Refresh' }
    );
  });
});
