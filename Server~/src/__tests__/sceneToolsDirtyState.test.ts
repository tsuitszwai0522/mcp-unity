import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerLoadSceneTool } from '../tools/loadSceneTool.js';
import { registerUnloadSceneTool } from '../tools/unloadSceneTool.js';
import { registerDeleteSceneTool } from '../tools/deleteSceneTool.js';

const mockSendRequest = jest.fn();
const mockMcpUnity = { sendRequest: mockSendRequest } as any;
const mockLogger = { info: jest.fn(), debug: jest.fn(), warn: jest.fn(), error: jest.fn() } as any;
const mockServerTool = jest.fn();
const mockServer = { tool: mockServerTool } as any;

type Registered = { description: string; handler: (params?: Record<string, unknown>) => Promise<any> };

const register = (fn: (server: any, mcpUnity: any, logger: any) => void): Registered => {
  fn(mockServer, mockMcpUnity, mockLogger);
  const call = mockServerTool.mock.calls[mockServerTool.mock.calls.length - 1] as any[];
  return { description: call[1] as string, handler: call[3] };
};

describe('scene tools disclose unsaved-change handling', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('load_scene forwards savedScenes and discardedScenes', async () => {
    const { description, handler } = register(registerLoadSceneTool);
    const savedScenes = [{ name: 'A', path: 'Assets/A.unity', untitled: false }];
    const discardedScenes = [{ name: '', path: null, untitled: true }];
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: "Successfully loaded scene at path 'Assets/C.unity' (additive=false); saved 1 dirty scene(s)",
      scenePath: 'Assets/C.unity',
      additive: false,
      savedScenes,
      discardedScenes,
    });

    const result = await handler({ scenePath: 'Assets/C.unity' });
    const payload = JSON.parse(result.content[1].text);

    expect(payload.savedScenes).toEqual(savedScenes);
    expect(payload.discardedScenes).toEqual(discardedScenes);
    expect(description).toContain('savedScenes');
    expect(description).toContain('discardedScenes');
    expect(description).toContain('untitled_dirty_scene');
  });

  it('unload_scene forwards saved and discardedUnsavedChanges', async () => {
    const { description, handler } = register(registerUnloadSceneTool);
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: "Successfully unloaded scene 'A'; its unsaved changes were discarded",
      sceneName: 'A',
      scenePath: 'Assets/A.unity',
      wasDirty: true,
      saved: false,
      discardedUnsavedChanges: true,
      removed: true,
    });

    const result = await handler({ scenePath: 'Assets/A.unity', saveIfDirty: false });
    const payload = JSON.parse(result.content[1].text);

    expect(payload).toMatchObject({ wasDirty: true, saved: false, discardedUnsavedChanges: true, removed: true });
    expect(description).toContain('discardedUnsavedChanges');
  });

  it('delete_scene forwards closedLoadedScene and discardedUnsavedChanges', async () => {
    const { description, handler } = register(registerDeleteSceneTool);
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: "Successfully deleted scene at path 'Assets/B.unity'",
      scenePath: 'Assets/B.unity',
      closedLoadedScene: true,
      discardedUnsavedChanges: true,
    });

    const result = await handler({ scenePath: 'Assets/B.unity' });
    const payload = JSON.parse(result.content[1].text);

    expect(payload).toMatchObject({ closedLoadedScene: true, discardedUnsavedChanges: true });
    expect(description).toContain('only loaded scene');
  });
});
