import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerManageAssetTool } from '../tools/manageAssetTool.js';

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

function getRegistration(): any[] {
  const call = mockServerTool.mock.calls.find(
    (candidate) => candidate[0] === 'manage_asset',
  );
  if (!call) throw new Error('manage_asset was not registered');
  return call as any[];
}

describe('manage_asset', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers the action enum and optional parameters without defaults', () => {
    registerManageAssetTool(mockServer, mockMcpUnity, mockLogger);

    const [name, description, schema] = getRegistration();
    expect(name).toBe('manage_asset');
    expect(description).toContain('Overwrite is not supported');
    expect(description).toContain('copy creates and discloses a new GUID');
    expect(description).toContain('inside the source');
    for (const action of ['move', 'copy', 'rename', 'create_folder']) {
      expect(schema.action.safeParse(action).success).toBe(true);
    }
    expect(schema.action.safeParse('merge').success).toBe(false);
    expect(schema.destinationPath.safeParse(undefined).success).toBe(true);
    expect(schema.destinationPath.parse(undefined)).toBeUndefined();
    expect(schema.newName.safeParse(undefined).success).toBe(true);
    expect(schema.newName.parse(undefined)).toBeUndefined();
    expect(schema.assetPath.description).toContain('Assets/');
    expect(schema.destinationPath.description).toContain('create_folder');
    expect(schema.destinationPath.description).toContain('Overwrite is not supported');
    expect(schema.newName.description).toContain('foo..bar');
    expect(schema.newName.description).toContain('.meta');
    expect(schema.newName.description).toContain('trailing dot');
  });

  it('forwards optional inputs and the complete Unity response', async () => {
    const unityResponse = {
      success: true,
      type: 'text',
      action: 'copy',
      message: 'Copied asset with a new GUID',
      assetPath: 'Assets/Target.txt',
      guid: 'copy-guid',
      sourcePath: 'Assets/Source.txt',
      sourceGuid: 'source-guid',
      warnings: ['read-back warning retained'],
    };
    mockSendRequest.mockResolvedValue(unityResponse);
    registerManageAssetTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration()[3]({
      action: 'copy',
      assetPath: 'Assets/Source.txt',
      destinationPath: 'Assets/Target.txt',
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'manage_asset',
      params: {
        action: 'copy',
        assetPath: 'Assets/Source.txt',
        destinationPath: 'Assets/Target.txt',
      },
    });
    expect(JSON.parse(result.content[1].text)).toEqual(unityResponse);
  });
});
