import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerCreatePrefabTool } from '../tools/createPrefabTool.js';
import { registerSaveAsPrefabTool } from '../tools/saveAsPrefabTool.js';
import {
  registerCreateSpriteAtlasTool,
  registerImportTextureAsSpriteTool,
} from '../tools/spriteTools.js';
import { registerSavePrefabContentsTool } from '../tools/prefabEditTools.js';
import { registerUpdateComponentTool } from '../tools/updateComponentTool.js';
import { isExplicitAssetPathInsideAssets } from '../utils/assetPathSchema.js';

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

describe('S7-b asset write honesty contracts', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('schema helper rejects prefix tricks and paths that normalize outside Assets', () => {
    for (const invalidPath of [
      '../Escape.prefab',
      'Assets/../../Escape.prefab',
      '/tmp/Escape.prefab',
      'Bare/Escape.prefab',
      'AssetsSibling/Escape.prefab',
    ]) {
      expect(isExplicitAssetPathInsideAssets(invalidPath)).toBe(false);
    }

    expect(isExplicitAssetPathInsideAssets('Assets/Prefabs/../Probe.prefab')).toBe(true);
    expect(isExplicitAssetPathInsideAssets('Assets')).toBe(false);
    expect(isExplicitAssetPathInsideAssets('Assets', true)).toBe(true);
  });

  it('all write-path schemas reject the four invalid path classes and accept Assets paths', () => {
    registerCreatePrefabTool(mockServer, mockMcpUnity, mockLogger);
    registerSaveAsPrefabTool(mockServer, mockMcpUnity, mockLogger);
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);
    registerCreateSpriteAtlasTool(mockServer, mockMcpUnity, mockLogger);

    const createPrefabSchema = getRegistration('create_prefab')[2].prefabName;
    const savePrefabSchema = getRegistration('save_as_prefab')[2].savePath;
    const importSchema = getRegistration('import_texture_as_sprite')[2].assetPath;
    const atlasSchema = getRegistration('create_sprite_atlas')[2];
    const invalidPaths = [
      '../Escape',
      'Assets/../../Escape',
      '/tmp/Escape',
      'Bare/Escape',
    ];

    for (const invalidPath of invalidPaths) {
      expect(createPrefabSchema.safeParse(invalidPath).success).toBe(false);
      expect(savePrefabSchema.safeParse(`${invalidPath}.prefab`).success).toBe(false);
      expect(importSchema.safeParse(`${invalidPath}.png`).success).toBe(false);
      expect(atlasSchema.savePath.safeParse(`${invalidPath}.spriteatlas`).success).toBe(false);
      expect(atlasSchema.folderPath.safeParse(invalidPath).success).toBe(false);
    }

    expect(createPrefabSchema.safeParse('Assets/Prefabs/Probe').success).toBe(true);
    expect(savePrefabSchema.safeParse('Assets/Prefabs/Probe.prefab').success).toBe(true);
    expect(importSchema.safeParse('Assets/Sprites/Probe.png').success).toBe(true);
    expect(atlasSchema.savePath.safeParse('Assets/Atlases/Probe.spriteatlas').success).toBe(true);
    expect(atlasSchema.folderPath.safeParse('Assets').success).toBe(true);
  });

  it('tool metadata discloses containment, readback, read-only, and zero-dirty behavior', () => {
    registerCreatePrefabTool(mockServer, mockMcpUnity, mockLogger);
    registerSaveAsPrefabTool(mockServer, mockMcpUnity, mockLogger);
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);
    registerCreateSpriteAtlasTool(mockServer, mockMcpUnity, mockLogger);
    registerSavePrefabContentsTool(mockServer, mockMcpUnity, mockLogger);
    registerUpdateComponentTool(mockServer, mockMcpUnity, mockLogger);

    const [createName, createDescription, createSchema] = getRegistration('create_prefab');
    expect(createName).toBe('create_prefab');
    expect(createDescription).toContain('Assets directory');
    expect(createDescription).toContain('_1, _2');
    expect(createSchema.prefabName.description).toContain('escape paths are rejected');

    const [, saveDescription, saveSchema] = getRegistration('save_as_prefab');
    expect(saveDescription).toContain('before directories are created');
    expect(saveDescription).toContain('read-only target fails');
    expect(saveSchema.savePath.description).toContain('escape paths are rejected');

    const [, importDescription, importSchema] = getRegistration('import_texture_as_sprite');
    expect(importDescription).toContain('read back from the persisted importer');
    expect(importDescription).toContain('batch_execute');
    expect(importDescription).toContain('validation_error');
    expect(importDescription).toContain('before the importer is changed');
    expect(importDescription).toContain('textureType = TextureImporterType.Sprite');
    expect(importDescription).toContain('resets other importer settings to Sprite defaults');
    expect(importDescription).toContain('using tool defaults when omitted');
    expect(importDescription).toContain('all other settings remain at the Sprite defaults');
    expect(importSchema.assetPath.description).toContain('escape paths are rejected');

    const [, atlasDescription, atlasSchema] = getRegistration('create_sprite_atlas');
    expect(atlasDescription).toContain('rejected rather than prepended');
    expect(atlasDescription).toContain('folderPath');
    expect(atlasDescription).toContain('read back from the saved atlas');
    expect(atlasSchema.savePath.description).toContain('escaping paths are rejected');
    expect(atlasSchema.folderPath.description).toContain('escaping paths are rejected');

    const [, saveContentsDescription] = getRegistration('save_prefab_contents');
    expect(saveContentsDescription).toContain('read-only target fails');
    expect(saveContentsDescription).toContain('without changing the asset');
    expect(saveContentsDescription).toContain('closing the editing session');

    const [, updateComponentDescription] = getRegistration('update_component');
    expect(updateComponentDescription).toContain('returns success=false');
    expect(updateComponentDescription).toContain('without marking the GameObject dirty');
  });

  it('import_texture_as_sprite returns Unity read-back values even when they differ from the request', async () => {
    const unityResponse = {
      success: true,
      type: 'text',
      message: 'Imported with persisted values',
      assetPath: 'Assets/Sprites/Probe.png',
      spriteMode: 'Multiple',
      meshType: 'Tight',
      compression: 'HighQuality',
      wrapMode: 'MirrorOnce',
      wrapModeU: 'MirrorOnce',
      wrapModeV: 'MirrorOnce',
      wrapModeW: 'MirrorOnce',
      spriteBorder: { left: 1, bottom: 2, right: 3, top: 4 },
    };
    mockSendRequest.mockResolvedValue(unityResponse);
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('import_texture_as_sprite')[3]({
      assetPath: 'Assets/Sprites/Probe.png',
      spriteMode: 'Single',
      meshType: 'FullRect',
      compression: 'None',
    });

    expect(JSON.parse(result.content[1].text)).toEqual(unityResponse);
  });

  it('import_texture_as_sprite preserves conversion warnings in payload and summary text', async () => {
    const warning =
      'textureType changed from Default to Sprite; Unity reset other importer settings to Sprite defaults.';
    const unityResponse = {
      success: true,
      type: 'text',
      message: `Imported with persisted values. Warning: ${warning}`,
      assetPath: 'Assets/Sprites/Probe.png',
      spriteMode: 'Single',
      meshType: 'FullRect',
      compression: 'None',
      wrapMode: 'Repeat',
      wrapModeU: 'Repeat',
      wrapModeV: 'Repeat',
      wrapModeW: 'Repeat',
      spriteBorder: { left: 0, bottom: 0, right: 0, top: 0 },
      warnings: [warning],
    };
    mockSendRequest.mockResolvedValue(unityResponse);
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegistration('import_texture_as_sprite')[3]({
      assetPath: 'Assets/Sprites/Probe.png',
    });

    expect(JSON.parse(result.content[1].text).warnings).toEqual([warning]);
    expect(result.content[0].text).toContain(warning);
  });

  it('import_texture_as_sprite exposes optional no-default wrapMode and strict spriteBorder schemas', () => {
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);

    const [, description, schema] = getRegistration('import_texture_as_sprite');
    expect(description).toContain('textureType = TextureImporterType.Sprite');
    for (const wrapMode of ['Repeat', 'Clamp', 'Mirror', 'MirrorOnce']) {
      expect(schema.wrapMode.safeParse(wrapMode).success).toBe(true);
    }
    expect(schema.wrapMode.safeParse('PingPong').success).toBe(false);
    expect(schema.wrapMode.parse(undefined)).toBeUndefined();
    expect(schema.wrapMode.description).toContain('Omitted');
    expect(schema.wrapMode.description).toContain('U, V, and W');
    expect(schema.wrapMode.description).toContain('already Sprite');
    expect(schema.wrapMode.description).toContain('resets them to Sprite defaults');
    expect(schema.spriteBorder.safeParse({
      left: 1,
      bottom: 2,
      right: 3,
      top: 4,
    }).success).toBe(true);
    expect(schema.spriteBorder.safeParse({ left: 1, bottom: 2, right: 3 }).success)
      .toBe(false);
    expect(schema.spriteBorder.safeParse({
      left: 1,
      bottom: 2,
      right: 3,
      top: 4,
      center: 5,
    }).success).toBe(false);
    expect(schema.spriteBorder.parse(undefined)).toBeUndefined();
    expect(schema.spriteBorder.description).toContain('x=left');
    expect(schema.spriteBorder.description).toContain('Single');
    expect(schema.spriteBorder.description).toContain('Multiple is rejected');
    expect(description).toContain('wrapMode is Mixed');
    expect(description).toContain('per-sprite metadata');
    expect(schema.spriteBorder.description).toContain('Omitted');
    expect(schema.spriteBorder.description).toContain('already Sprite');
    expect(schema.spriteBorder.description).toContain('resets it to the Sprite default');
  });

  it('import_texture_as_sprite omits new parameters when the caller does not provide them', async () => {
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: 'Imported with existing optional settings',
      assetPath: 'Assets/Sprites/Probe.png',
      spriteMode: 'Single',
      meshType: 'FullRect',
      compression: 'LowQuality',
      wrapMode: 'Mirror',
      spriteBorder: { left: 0, bottom: 0, right: 0, top: 0 },
    });
    registerImportTextureAsSpriteTool(mockServer, mockMcpUnity, mockLogger);

    await getRegistration('import_texture_as_sprite')[3]({
      assetPath: 'Assets/Sprites/Probe.png',
      compression: 'LowQuality',
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'import_texture_as_sprite',
      params: {
        assetPath: 'Assets/Sprites/Probe.png',
        spriteMode: 'Single',
        meshType: 'FullRect',
        compression: 'LowQuality',
      },
    });
  });
});
