import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { payloadContent } from '../utils/toolPayload.js';
import { explicitAssetPathSchema } from '../utils/assetPathSchema.js';

// ============================================================================
// IMPORT TEXTURE AS SPRITE TOOL
// ============================================================================

const importTextureAsSpriteName = 'import_texture_as_sprite';
const importTextureAsSpriteDescription = 'Sets Sprite import settings for a texture at an explicit path inside this Unity project\'s Assets directory. This tool ensures textureType = TextureImporterType.Sprite; when the previous type differs, Unity resets other importer settings to Sprite defaults. This call then writes spriteMode, meshType, and compression (using tool defaults when omitted), plus any provided wrapMode or spriteBorder; all other settings remain at the Sprite defaults. The result reports assetPath, spriteMode, meshType, compression, wrapMode, wrapModeU/V/W, and spriteBorder read back from the persisted importer; wrapMode is Mixed when the axes differ. Setting wrapMode writes all three axes. spriteBorder is valid only with spriteMode Single; Multiple requires per-sprite metadata. Unknown enum values or malformed or incompatible spriteBorder objects reaching Unity through batch_execute return validation_error before the importer is changed.';
const importTextureAsSpriteSchema = z.object({
  assetPath: explicitAssetPathSchema('Explicit texture asset path inside this project\'s Assets directory (e.g., "Assets/Sprites/Cart/tomato.png"); bare relative, absolute, and Assets/../.. escape paths are rejected'),
  spriteMode: z.enum(['Single', 'Multiple']).optional().default('Single').describe('Sprite import mode (Single or Multiple)'),
  meshType: z.enum(['FullRect', 'Tight']).optional().default('FullRect').describe('Sprite mesh type (FullRect or Tight)'),
  compression: z.enum(['None', 'LowQuality', 'NormalQuality', 'HighQuality']).optional().default('None').describe('Texture compression level'),
  wrapMode: z.enum(['Repeat', 'Clamp', 'Mirror', 'MirrorOnce']).optional().describe('Optional texture wrap mode written to the U, V, and W axes. Omitted skips this write: persisted values remain unchanged when textureType is already Sprite, while conversion to Sprite resets them to Sprite defaults.'),
  spriteBorder: z.object({
    left: z.number(),
    bottom: z.number(),
    right: z.number(),
    top: z.number(),
  }).strict().optional().describe('Optional sprite border for spriteMode Single, mapped to Vector4 x=left, y=bottom, z=right, w=top. spriteMode Multiple is rejected because it requires per-sprite metadata. Omitted skips this write: the persisted border remains unchanged when textureType is already Sprite, while conversion to Sprite resets it to the Sprite default.')
});

/**
 * Registers the Import Texture As Sprite tool with the MCP server
 */
export function registerImportTextureAsSpriteTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${importTextureAsSpriteName}`);

  server.tool(
    importTextureAsSpriteName,
    importTextureAsSpriteDescription,
    importTextureAsSpriteSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${importTextureAsSpriteName}`, params);
        const result = await importTextureAsSpriteHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${importTextureAsSpriteName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${importTextureAsSpriteName}`, error);
        throw error;
      }
    }
  );
}

async function importTextureAsSpriteHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.assetPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'assetPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: importTextureAsSpriteName,
    params: {
      assetPath: params.assetPath,
      spriteMode: params.spriteMode ?? 'Single',
      meshType: params.meshType ?? 'FullRect',
      compression: params.compression ?? 'None',
      ...(params.wrapMode !== undefined && { wrapMode: params.wrapMode }),
      ...(params.spriteBorder !== undefined && { spriteBorder: params.spriteBorder })
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.error?.message || response.message || 'Failed to import texture as sprite'
    );
  }

  return {
    content: [
      {
        type: response.type || 'text',
        text: response.message || `Successfully imported texture as sprite`
      },
      payloadContent(response)
    ]
  };
}

// ============================================================================
// CREATE SPRITE ATLAS TOOL
// ============================================================================

const createSpriteAtlasName = 'create_sprite_atlas';
const createSpriteAtlasDescription = 'Creates a SpriteAtlas asset under this Unity project\'s Assets directory from a folder explicitly under Assets. Bare relative, absolute, and escaping savePath/folderPath values are rejected rather than prepended. atlasName must exactly match the savePath filename without its .spriteatlas or .spriteatlasv2 extension; mismatches return validation_error before asset creation. Successful payload values, including folderPath, are read back from the saved atlas.';
const createSpriteAtlasSchema = z.object({
  atlasName: z.string().describe('Required consistency assertion: must exactly equal the savePath filename without the .spriteatlas or .spriteatlasv2 extension'),
  savePath: explicitAssetPathSchema('Explicit SpriteAtlas asset path inside this project\'s Assets directory; bare relative, absolute, and escaping paths are rejected, and its extensionless filename must exactly match atlasName'),
  folderPath: explicitAssetPathSchema('Explicit folder path inside this project\'s Assets directory containing sprites to include; bare relative, absolute, and escaping paths are rejected', true),
  includeInBuild: z.boolean().optional().default(true).describe('Whether to include this atlas in builds (default: true)'),
  allowRotation: z.boolean().optional().default(true).describe('Allow sprite rotation during packing (default: true)'),
  tightPacking: z.boolean().optional().default(false).describe('Enable tight packing (default: false)')
});

/**
 * Registers the Create Sprite Atlas tool with the MCP server
 */
export function registerCreateSpriteAtlasTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${createSpriteAtlasName}`);

  server.tool(
    createSpriteAtlasName,
    createSpriteAtlasDescription,
    createSpriteAtlasSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${createSpriteAtlasName}`, params);
        const result = await createSpriteAtlasHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${createSpriteAtlasName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${createSpriteAtlasName}`, error);
        throw error;
      }
    }
  );
}

async function createSpriteAtlasHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.atlasName) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'atlasName' must be provided"
    );
  }

  if (!params.savePath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'savePath' must be provided"
    );
  }

  if (!params.folderPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'folderPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: createSpriteAtlasName,
    params: {
      atlasName: params.atlasName,
      savePath: params.savePath,
      folderPath: params.folderPath,
      includeInBuild: params.includeInBuild ?? true,
      allowRotation: params.allowRotation ?? true,
      tightPacking: params.tightPacking ?? false
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to create sprite atlas'
    );
  }

  return {
    content: [
      {
        type: response.type || 'text',
        text: response.message || (response.atlasName
          ? `Successfully created SpriteAtlas '${response.atlasName}'`
          : 'Successfully created SpriteAtlas')
      },
      payloadContent({
        message: response.message,
        atlasName: response.atlasName,
        savePath: response.savePath,
        folderPath: response.folderPath,
        includeInBuild: response.includeInBuild,
        allowRotation: response.allowRotation,
        tightPacking: response.tightPacking
      })
    ]
  };
}
