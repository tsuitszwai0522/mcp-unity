import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerAddAssetToSceneTool } from '../tools/addAssetToSceneTool.js';
import { registerRemoveComponentTool } from '../tools/removeComponentTool.js';
import {
  registerReadSerializedFieldsTool,
  registerWriteSerializedFieldsTool,
} from '../tools/serializedFieldTools.js';
import { registerCreateSpriteAtlasTool } from '../tools/spriteTools.js';
import { registerUpdateComponentTool } from '../tools/updateComponentTool.js';
import { registerUpdateGameObjectTool } from '../tools/updateGameObjectTool.js';

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

function getRegistration(toolName: string): any[] {
  const call = mockServerTool.mock.calls.find((candidate) => candidate[0] === toolName);
  if (!call) throw new Error(`Tool ${toolName} was not registered`);
  return call as any[];
}

describe('third-batch write contracts', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('update_gameobject preserves field failures in payload and marks the result as an error', async () => {
    const failedFields = [
      { field: 'tag', reason: "Tag 'MissingTag' does not exist" },
      { field: 'layer', reason: 'Layer value 32 is outside the valid range 0-31' },
    ];
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      type: 'text',
      message: '1 field(s) succeeded, 2 field(s) failed',
      instanceId: 42,
      name: 'Probe',
      path: '/Probe',
      updatedFields: ['activeSelf'],
      failedFields,
      warnings: ['This GameObject is under a Canvas but has no RectTransform.'],
    });
    registerUpdateGameObjectTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('update_gameobject')[3]({
      instanceId: 42,
      gameObjectData: { tag: 'MissingTag', layer: 32, activeSelf: false },
    });

    expect(result.isError).toBe(true);
    expect(JSON.parse(result.content[1].text)).toEqual({
      message: '1 field(s) succeeded, 2 field(s) failed',
      instanceId: 42,
      name: 'Probe',
      path: '/Probe',
      updatedFields: ['activeSelf'],
      failedFields,
      warnings: ['This GameObject is under a Canvas but has no RectTransform.'],
    });
  });

  it('update_gameobject documents per-field outcomes and rejects unknown nested keys', () => {
    registerUpdateGameObjectTool(mockServer, mockMcpUnity, mockLogger);

    const [, description, schema] = getRegistration('update_gameobject');
    expect(description).toContain('Every supplied gameObjectData key');
    expect(description).toContain('updatedFields or failedFields');
    expect(schema.gameObjectData.safeParse({ name: 'Probe', mysteryField: 1 }).success)
      .toBe(false);
  });

  it('add_asset_to_scene documents and defaults positionSpace to world', () => {
    registerAddAssetToSceneTool(mockServer, mockMcpUnity, mockLogger);

    const [, description, schema] = getRegistration('add_asset_to_scene');
    expect(description).toContain('final world-space coordinates');
    expect(description).toContain('positionSpace="local"');
    expect(schema.positionSpace.parse(undefined)).toBe('world');
    expect(schema.positionSpace.safeParse('screen').success).toBe(false);
    expect(schema.positionSpace.description).toContain('relative to the parent');
  });

  it('add_asset_to_scene forwards the world default and preserves read-back positions', async () => {
    const worldPosition = { x: 2, y: 3, z: 4 };
    const localPosition = { x: -8, y: 3, z: 4 };
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      type: 'text',
      message: 'Asset added',
      instanceId: 99,
      worldPosition,
      localPosition,
    });
    registerAddAssetToSceneTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('add_asset_to_scene')[3]({
      assetPath: 'Assets/Probe.prefab',
      parentId: 7,
      position: worldPosition,
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'add_asset_to_scene',
      params: {
        assetPath: 'Assets/Probe.prefab',
        parentId: 7,
        position: worldPosition,
        positionSpace: 'world',
      },
    });
    expect(JSON.parse(result.content[1].text)).toEqual({
      message: 'Asset added',
      instanceId: 99,
      worldPosition,
      localPosition,
    });
  });

  it('create_sprite_atlas payload forwards accepted Unity read-back fields', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      type: 'text',
      atlasName: 'RequestAtlas',
      savePath: 'Assets/RequestAtlas.spriteatlas',
      folderPath: 'Assets/Sprites',
      includeInBuild: false,
      allowRotation: true,
      tightPacking: false,
    });
    registerCreateSpriteAtlasTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('create_sprite_atlas')[3]({
      atlasName: 'RequestAtlas',
      savePath: 'Assets/RequestAtlas.spriteatlas',
      folderPath: 'Assets/Sprites',
      allowRotation: false,
      tightPacking: true,
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'create_sprite_atlas',
      params: {
        atlasName: 'RequestAtlas',
        savePath: 'Assets/RequestAtlas.spriteatlas',
        folderPath: 'Assets/Sprites',
        includeInBuild: true,
        allowRotation: false,
        tightPacking: true,
      },
    });
    expect(result.content[0].text).toContain('RequestAtlas');
    expect(JSON.parse(result.content[1].text)).toEqual({
      atlasName: 'RequestAtlas',
      savePath: 'Assets/RequestAtlas.spriteatlas',
      folderPath: 'Assets/Sprites',
      includeInBuild: false,
      allowRotation: true,
      tightPacking: false,
    });
  });

  it('create_sprite_atlas exposes its filename consistency constraint in tools/list metadata', () => {
    registerCreateSpriteAtlasTool(mockServer, mockMcpUnity, mockLogger);

    const [, description, schema] = getRegistration('create_sprite_atlas');
    expect(description).toContain('atlasName must exactly match');
    expect(description).toContain('validation_error before asset creation');
    expect(schema.atlasName.description).toContain('must exactly equal');
    expect(schema.savePath.description).toContain('must exactly match atlasName');
  });

  it('component tools disclose ambiguity and fully-qualified-name requirements', () => {
    registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);
    registerRemoveComponentTool(mockServer, mockMcpUnity, mockLogger);
    registerReadSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);
    registerWriteSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);

    for (const toolName of [
      'update_component',
      'remove_component',
      'read_serialized_fields',
      'write_serialized_fields',
    ]) {
      const [, description, schema] = getRegistration(toolName);
      expect(description).toContain('Ambiguous short or partial component names');
      expect(description).toContain('fully-qualified name');
      expect(schema.componentName.description).toContain('fully-qualified name');
    }
  });

  it('remove_component preserves ambiguity-narrowing warnings in payload', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      type: 'text',
      message: 'Component removed',
      instanceId: 12,
      name: 'Probe',
      path: '/Probe',
      warnings: ["Component name 'ProbeComponent' is ambiguous."],
    });
    registerRemoveComponentTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('remove_component')[3]({
      instanceId: 12,
      componentName: 'ProbeComponent',
    });

    expect(JSON.parse(result.content[1].text).warnings).toEqual([
      "Component name 'ProbeComponent' is ambiguous.",
    ]);
  });

  it('read_serialized_fields preserves ambiguity-narrowing warnings in payload', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read fields',
      instanceId: 12,
      componentName: 'ProbeComponent',
      fields: { value: 17 },
      warnings: ["Component name 'ProbeComponent' is ambiguous."],
    });
    registerReadSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('read_serialized_fields')[3]({
      instanceId: 12,
      componentName: 'ProbeComponent',
    });

    expect(JSON.parse(result.content[1].text).warnings).toEqual([
      "Component name 'ProbeComponent' is ambiguous.",
    ]);
  });
});
