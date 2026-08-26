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

  it('documents the context-specific camera no-fallback rules', () => {
    registerScreenshotTools(mockServer, mockMcpUnity, mockLogger);

    const cameraCall = mockServerTool.mock.calls.find((c) => c[0] === 'screenshot_camera');
    expect(cameraCall?.[1]).toContain('Camera.main when no Prefab session is active');
    expect(cameraCall?.[1]).toContain('never falls back to loaded scene cameras');

    const gameViewCall = mockServerTool.mock.calls.find((c) => c[0] === 'screenshot_game_view');
    expect(gameViewCall?.[1]).toContain(
      'Only frameFresh=verified means the pixels reflect the current scene',
    );
    expect(gameViewCall?.[1]).toContain('repaint_immediately_unavailable:');
    expect(gameViewCall?.[1]).toContain('retry with force_focus=true');
    expect(gameViewCall?.[1]).toContain('active tab and rerenders before capture');
    expect(gameViewCall?.[1]).toContain('only when isolatedCameraCount=0');
    expect(gameViewCall?.[1]).toContain(
      'focus cannot repair the post-isolation frame while isolated cameras exist',
    );
    expect(gameViewCall?.[1]).toContain('no_camera_render has no force-focus remediation');
    expect(gameViewCall?.[1]).toContain('Prefab contents are open');
    expect(gameViewCall?.[1]).toContain('never falls back to a loaded scene Main Camera');
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
      message: 'Game View screenshot captured [capturePath=render_view degraded=false]',
      capturePath: 'render_view',
      degraded: false,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });

    expect(result.content[0].type).toBe('text');
    expect(result.content[0].text).toContain('capturePath=render_view');
    expect(result.content[0].text).toContain('degraded=false');
    expect(result.content[0].text.match(/capturePath=render_view/g)).toHaveLength(1);
    expect(result.content[0].text.match(/degraded=false/g)).toHaveLength(1);
    expect(result.content[1].type).toBe('image');
    expect(result.content[1].mimeType).toBe('image/png');
    expect(result.content[1].data).toBe('iVBORw0KGgo=');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('carries freshness and camera-isolation metadata into text before the image', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=render_view degraded=false]',
      capturePath: 'render_view',
      degraded: false,
      frameFresh: 'verified',
      cameraRenders: 2,
      frameFreshReason: 'camera_render_observed',
      isolatedCameras: [
        { name: 'OrphanPreviewCamera', scenePath: 'Assets/Orphan.prefab' },
      ],
      isolatedCameraCount: 9,
      contextCameras: [
        { name: 'SessionCamera', scenePath: 'Assets/Session.prefab' },
      ],
      contextCameraCount: 1,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    expect(result.content).toHaveLength(2);
    expect(text).toContain('frameFresh=verified');
    expect(text).toContain('cameraRenders=2');
    expect(text).toContain('frameFreshReason=camera_render_observed');
    expect(text).toContain(
      'isolatedCameras=[{"name":"OrphanPreviewCamera","scenePath":"Assets/Orphan.prefab"}]',
    );
    expect(text).toContain(
      'contextCameras=[{"name":"SessionCamera","scenePath":"Assets/Session.prefab"}]',
    );
    expect(text).toContain('isolatedCameraCount=9');
    expect(text).toContain('contextCameraCount=1');
    expect(text).not.toContain('metadata absent');
    expect(result.content[1].type).toBe('image');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('does not duplicate the five S8-b scalar tokens already emitted by Unity', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=render_view degraded=false frameFresh=verified cameraRenders=2 frameFreshReason=camera_render_observed isolatedCameraCount=1 contextCameraCount=0]',
      capturePath: 'render_view',
      degraded: false,
      frameFresh: 'verified',
      cameraRenders: 2,
      frameFreshReason: 'camera_render_observed',
      isolatedCameras: [
        { name: 'OrphanPreviewCamera', scenePath: 'Assets/Orphan.prefab' },
      ],
      isolatedCameraCount: 1,
      contextCameras: [],
      contextCameraCount: 0,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    for (const token of [
      'frameFresh=verified',
      'cameraRenders=2',
      'frameFreshReason=camera_render_observed',
      'isolatedCameraCount=1',
      'contextCameraCount=0',
    ]) {
      expect(text.split(token)).toHaveLength(2);
    }
    expect(text).toContain(
      'isolatedCameras=[{"name":"OrphanPreviewCamera","scenePath":"Assets/Orphan.prefab"}]',
    );
    expect(text).toContain('contextCameras=[]');
    expect(result.content).toHaveLength(2);
    expect(result.content[0].type).toBe('text');
    expect(result.content[1].type).toBe('image');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('does not apply Game View S8-b enrichment to other screenshot tools', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Scene View screenshot captured [capturePath=scene_view_camera degraded=false]',
      capturePath: 'scene_view_camera',
      degraded: false,
      frameFresh: 'verified',
      cameraRenders: 3,
      frameFreshReason: 'camera_render_observed',
      isolatedCameras: [],
      isolatedCameraCount: 0,
      contextCameras: [],
      contextCameraCount: 0,
    });
    const handler = getHandler('screenshot_scene_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    expect(text).not.toContain('frameFresh=');
    expect(text).not.toContain('cameraRenders=');
    expect(text).not.toContain('frameFreshReason=');
    expect(text).not.toContain('isolatedCameras=');
    expect(text).not.toContain('isolatedCameraCount=');
    expect(text).not.toContain('contextCameras=');
    expect(text).not.toContain('contextCameraCount=');
    expect(result.content).toHaveLength(2);
    expect(result.content[0].type).toBe('text');
    expect(result.content[1].type).toBe('image');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('includes degraded diagnostics in machine-readable text before the image', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=main_camera_fallback degraded=true degradedReason=render_view_unavailable:method_missing;screen_capture_returned_null gameViewWindowCreated=true]',
      capturePath: 'main_camera_fallback',
      degraded: true,
      degradedReason: 'render_view_unavailable:method_missing;screen_capture_returned_null',
      gameViewWindowCreated: true,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });

    expect(result.content).toHaveLength(2);
    expect(result.content[0].text).toContain('capturePath=main_camera_fallback');
    expect(result.content[0].text).toContain('degraded=true');
    expect(result.content[0].text).toContain(
      'degradedReason=render_view_unavailable:method_missing;screen_capture_returned_null',
    );
    expect(result.content[0].text).toContain('gameViewWindowCreated=true');
    expect(result.content[0].text.match(/capturePath=main_camera_fallback/g)).toHaveLength(1);
    expect(result.content[0].text.match(/degradedReason=/g)).toHaveLength(1);
    expect(result.content[0].text.match(/gameViewWindowCreated=true/g)).toHaveLength(1);
    expect(result.content[1].type).toBe('image');
  });

  it('makes missing Unity-side observability metadata explicit for mixed versions', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Screenshot captured by an older Unity package',
    });
    const handler = getHandler('screenshot_camera');

    const result = await handler({ width: 320, height: 180 });

    expect(result.content[0].text).toContain('capturePath=unknown');
    expect(result.content[0].text).toContain('degraded=unknown');
    expect(result.content[0].text).toContain('core unity-side metadata absent');
    expect(result.content[1].type).toBe('image');
  });

  it('marks missing S8-b Game View metadata without changing the content shape', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=render_view degraded=false]',
      capturePath: 'render_view',
      degraded: false,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    expect(text).toContain('frameFresh=unknown');
    expect(text).toContain('cameraRenders=unknown');
    expect(text).toContain('frameFreshReason=unknown');
    expect(text).toContain('isolatedCameras=unknown');
    expect(text).toContain('isolatedCameraCount=unknown');
    expect(text).toContain('contextCameras=unknown');
    expect(text).toContain('contextCameraCount=unknown');
    expect(text).toContain('S8-b unity-side metadata absent');
    expect(text).not.toContain('core unity-side metadata absent');
    expect(result.content).toHaveLength(2);
    expect(result.content[1].type).toBe('image');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('marks S8-b metadata absent independently of S8-a sibling fields', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=render_view degraded=false]',
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    expect(text.match(/capturePath=render_view/g)).toHaveLength(1);
    expect(text.match(/degraded=false/g)).toHaveLength(1);
    expect(text).toContain('frameFresh=unknown');
    expect(text).toContain('cameraRenders=unknown');
    expect(text).toContain('frameFreshReason=unknown');
    expect(text).toContain('isolatedCameras=unknown');
    expect(text).toContain('isolatedCameraCount=unknown');
    expect(text).toContain('contextCameras=unknown');
    expect(text).toContain('contextCameraCount=unknown');
    expect(text).toContain('S8-b unity-side metadata absent');
    expect(text).not.toContain('core unity-side metadata absent');
    expect(result.content).toHaveLength(2);
    expect(result.content[1].type).toBe('image');
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('fills only the missing message metadata token without duplicating the existing one', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: 'iVBORw0KGgo=',
      message: 'Game View screenshot captured [capturePath=render_view]',
      capturePath: 'render_view',
      degraded: false,
    });
    const handler = getHandler('screenshot_game_view');

    const result = await handler({ width: 320, height: 180 });
    const text = result.content[0].text;

    expect(text.match(/capturePath=render_view/g)).toHaveLength(1);
    expect(text.match(/degraded=false/g)).toHaveLength(1);
  });

  it('fails loudly when Unity returns non-string image data', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      mimeType: 'image/png',
      data: { unexpected: true },
      message: 'Malformed image result',
    });
    const handler = getHandler('screenshot_scene_view');

    await expect(handler({ width: 320, height: 180 })).rejects.toMatchObject({
      name: 'McpUnityError',
      message: expect.stringContaining('expected image data to be a string'),
    });
  });

  it('bounds width and height to 1-4096 for all screenshot schemas', () => {
    registerScreenshotTools(mockServer, mockMcpUnity, mockLogger);

    for (const toolName of [
      'screenshot_game_view',
      'screenshot_scene_view',
      'screenshot_camera',
    ]) {
      const call = mockServerTool.mock.calls.find((candidate) => candidate[0] === toolName);
      const schema = call?.[2] as Record<string, any>;

      for (const field of ['width', 'height']) {
        expect(schema[field].safeParse(1).success).toBe(true);
        expect(schema[field].safeParse(4096).success).toBe(true);

        for (const invalidValue of [0, 4097]) {
          const parsed = schema[field].safeParse(invalidValue);
          expect(parsed.success).toBe(false);
          if (!parsed.success) {
            expect(parsed.error.issues[0].message).toContain('4096');
          }
        }
      }
    }
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
