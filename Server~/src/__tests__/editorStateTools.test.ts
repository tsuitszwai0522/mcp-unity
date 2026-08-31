import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerEditorStateTools } from '../tools/editorStateTools.js';

const mockSendRequest = jest.fn();
const mockWaitForConnection = jest.fn();
const mockMcpUnity = {
  sendRequest: mockSendRequest,
  waitForConnection: mockWaitForConnection,
} as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;

type ToolHandler = (params: { action: string }) => Promise<any>;

const getSetStateHandler = (): ToolHandler => {
  const handlers = new Map<string, ToolHandler>();
  const server = {
    tool: jest.fn((name: string, ...args: unknown[]) => {
      handlers.set(name, args.at(-1) as ToolHandler);
    }),
  } as any;
  registerEditorStateTools(server, mockMcpUnity, mockLogger);
  return handlers.get('set_editor_state')!;
};

describe('set_editor_state reconnection verification', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockWaitForConnection.mockResolvedValue(undefined);
  });

  it('returns isError when the happy play response is not playing', async () => {
    mockSendRequest.mockResolvedValueOnce({
      success: true,
      message: 'Play action accepted',
      state: {
        isPlaying: false,
        isPaused: false,
        isCompiling: false,
      },
    });

    const result = await getSetStateHandler()({ action: 'play' });

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toContain(
      'after Unity responded: expected isPlaying=true, actual isPlaying=false'
    );
    expect(mockWaitForConnection).not.toHaveBeenCalled();
  });

  it('returns isError when the happy stop response is still playing', async () => {
    mockSendRequest.mockResolvedValueOnce({
      success: true,
      message: 'Stop action accepted',
      state: {
        isPlaying: true,
        isPaused: false,
        isCompiling: false,
      },
    });

    const result = await getSetStateHandler()({ action: 'stop' });

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toContain(
      'after Unity responded: expected isPlaying=false, actual isPlaying=true'
    );
  });

  it('reports happy-path success only when the play target is reached', async () => {
    mockSendRequest.mockResolvedValueOnce({
      success: true,
      message: 'Play action completed',
      state: {
        isPlaying: true,
        isPaused: false,
        isCompiling: false,
      },
    });

    const result = await getSetStateHandler()({ action: 'play' });

    expect(result.isError).toBeUndefined();
    expect(result.content[0].text).toBe('Play action completed');
  });

  it('returns isError when play reconnects but the editor is not playing', async () => {
    mockSendRequest
      .mockRejectedValueOnce(new Error('domain reload disconnected'))
      .mockResolvedValueOnce({
        success: true,
        message: 'Editor state retrieved',
        state: {
          isPlaying: false,
          isPaused: false,
          isCompiling: false,
          currentScene: 'Assets/Test.unity',
          platform: 'StandaloneOSX',
        },
      });

    const result = await getSetStateHandler()({ action: 'play' });

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toContain('expected isPlaying=true, actual isPlaying=false');
    expect(JSON.parse(result.content[1].text)).toEqual(expect.objectContaining({
      expectedIsPlaying: true,
      actualIsPlaying: false,
      state: expect.objectContaining({ isPlaying: false }),
    }));
  });

  it('returns isError when stop reconnects but the editor is still playing', async () => {
    mockSendRequest
      .mockRejectedValueOnce(new Error('domain reload disconnected'))
      .mockResolvedValueOnce({
        success: true,
        message: 'Editor state retrieved',
        state: {
          isPlaying: true,
          isPaused: false,
          isCompiling: false,
          currentScene: 'Assets/Test.unity',
          platform: 'StandaloneOSX',
        },
      });

    const result = await getSetStateHandler()({ action: 'stop' });

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toContain('expected isPlaying=false, actual isPlaying=true');
    expect(JSON.parse(result.content[1].text)).toEqual(expect.objectContaining({
      expectedIsPlaying: false,
      actualIsPlaying: true,
      state: expect.objectContaining({ isPlaying: true }),
    }));
  });

  it('reports success only when the reconnected state matches the play target', async () => {
    mockSendRequest
      .mockRejectedValueOnce(new Error('domain reload disconnected'))
      .mockResolvedValueOnce({
        success: true,
        message: 'Editor state retrieved',
        state: {
          isPlaying: true,
          isPaused: false,
          isCompiling: false,
          currentScene: 'Assets/Test.unity',
          platform: 'StandaloneOSX',
        },
      });

    const result = await getSetStateHandler()({ action: 'play' });

    expect(result.isError).toBeUndefined();
    expect(result.content[0].text).toBe("Editor state action 'play' executed successfully");
    expect(mockWaitForConnection).toHaveBeenCalledWith(30000);
  });
});
