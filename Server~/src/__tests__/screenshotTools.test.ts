import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { registerScreenshotTools } from '../tools/screenshotTools.js';

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

function getHandler(toolName: string): Function {
  registerScreenshotTools(mockServer, mockMcpUnity, mockLogger);
  const call = mockServerTool.mock.calls.find((c) => c[0] === toolName);
  if (!call) throw new Error(`Tool ${toolName} was not registered`);
  return call[3] as Function;
}

describe('screenshot_game_view', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers screenshot_game_view, screenshot_scene_view, and screenshot_camera', () => {
    registerScreenshotTools(mockServer, mockMcpUnity, mockLogger);

    const names = mockServerTool.mock.calls.map((c) => c[0]);
    expect(names).toEqual(
      expect.arrayContaining([
        'screenshot_game_view',
        'screenshot_scene_view',
        'screenshot_camera',
      ]),
    );
  });

  it('forwards force_focus to Unity for screenshot_game_view', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
    });
    const handler = getHandler('screenshot_game_view');

    await handler({ width: 320, height: 180, force_focus: true });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'screenshot_game_view',
      params: { width: 320, height: 180, force_focus: true },
    });
  });

  it('returns image content from screenshot_game_view', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });

    expect(result.content[0].type).toBe('image');
    expect(result.content[0].mimeType).toBe('image/png');
    expect(result.content[0].data).toBe('iVBORw0KGgo=');
  });

  it('does not require force_focus to be set', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
    });
    const handler = getHandler('screenshot_game_view');

    await handler({ width: 960, height: 540 });

    const sentParams = (mockSendRequest as any).mock.calls[0][0].params;
    expect(sentParams.force_focus).toBeUndefined();
  });
});
