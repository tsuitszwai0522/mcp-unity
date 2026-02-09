import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// IMPORT TEXTURE AS SPRITE TOOL
// ============================================================================

const importTextureAsSpriteName = 'import_texture_as_sprite';
const importTextureAsSpriteDescription = 'Sets a texture\'s import settings to Sprite type with configurable sprite mode, mesh type, and compression';
const importTextureAsSpriteSchema = z.object({
  assetPath: z.string().describe('The asset path of the texture (e.g., "Assets/Sprites/Cart/tomato.png")'),
  spriteMode: z.enum(['Single', 'Multiple']).optional().default('Single').describe('Sprite import mode (Single or Multiple)'),
  meshType: z.enum(['FullRect', 'Tight']).optional().default('FullRect').describe('Sprite mesh type (FullRect or Tight)'),
  compression: z.enum(['None', 'LowQuality', 'NormalQuality', 'HighQuality']).optional().default('None').describe('Texture compression level')
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
      compression: params.compression ?? 'None'
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to import texture as sprite'
    );
  }

  return {
    content: [{
      type: response.type,
      text: response.message || `Successfully imported texture as sprite`
    }]
  };
}

// ============================================================================
// CREATE SPRITE ATLAS TOOL
// ============================================================================

const createSpriteAtlasName = 'create_sprite_atlas';
const createSpriteAtlasDescription = 'Creates a SpriteAtlas asset that packs sprites from a specified folder';
const createSpriteAtlasSchema = z.object({
  atlasName: z.string().describe('The name of the SpriteAtlas'),
  savePath: z.string().describe('The asset path to save the SpriteAtlas (e.g., "Assets/SpriteAtlas/Cart/Cart.spriteatlas")'),
  folderPath: z.string().describe('The folder containing sprites to include (e.g., "Assets/Sprites/Cart")'),
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
    content: [{
      type: response.type,
      text: response.message || `Successfully created SpriteAtlas '${params.atlasName}'`
    }]
  };
}
