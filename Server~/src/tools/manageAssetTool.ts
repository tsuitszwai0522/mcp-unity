import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { payloadContent } from '../utils/toolPayload.js';

const toolName = 'manage_asset';
const toolDescription =
  'Moves, copies, or renames an asset, or creates one folder, under this Unity project\'s Assets/ directory. Overwrite is not supported. Destination parent folders must already exist and can be created with action "create_folder". Move and copy reject a destination equal to or inside the source. Move and rename preserve the source GUID; copy creates and discloses a new GUID.';

const paramsSchema = z.object({
  action: z
    .enum(['move', 'copy', 'rename', 'create_folder'])
    .describe('Required action: move, copy, rename, or create_folder. Move and rename preserve the source GUID; copy creates and discloses a new GUID; overwrite is not supported.'),
  assetPath: z
    .string()
    .describe('Required source asset path, or the complete folder path for create_folder; it must be inside this project\'s Assets/ directory. Move and rename preserve its GUID, copy discloses a new GUID, and existing destinations are never overwritten.'),
  destinationPath: z
    .string()
    .optional()
    .describe('Required complete destination path for move or copy inside this project\'s Assets/ directory. Overwrite is not supported, and its parent folder must already exist; create it first with action "create_folder".'),
  newName: z
    .string()
    .optional()
    .describe('Required for rename. A single new asset name without path separators, leading/trailing whitespace, a trailing dot, the complete names "." or "..", or a case-insensitive .meta suffix. Other dots, including foo..bar, are allowed. Rename preserves the source GUID and does not overwrite an existing asset.'),
});

export function registerManageAssetTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger,
) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: z.infer<typeof paramsSchema>) => {
      try {
        logger.info(`Executing tool: ${toolName}`, params);
        const result = await manageAssetHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    },
  );
}

async function manageAssetHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof paramsSchema>,
): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      action: params.action,
      assetPath: params.assetPath,
      ...(params.destinationPath !== undefined && {
        destinationPath: params.destinationPath,
      }),
      ...(params.newName !== undefined && { newName: params.newName }),
    },
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.error?.message || response.message || 'Failed to manage Unity asset',
    );
  }

  return {
    content: [
      {
        type: response.type || 'text',
        text: response.message || 'Successfully managed Unity asset',
      },
      payloadContent(response),
    ],
  };
}
