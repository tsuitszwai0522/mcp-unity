import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { payloadContent } from '../utils/toolPayload.js';

// Constants for the tool
const toolName = 'add_asset_to_scene';
const toolDescription = 'Instantiates an AssetDatabase prefab in the active context: a loaded scene normally, or the open Prefab contents when a Prefab editing session is active. Position defaults to final world-space coordinates; use positionSpace="local" for coordinates relative to the parent.';

// Parameter schema for the tool
const paramsSchema = z.object({
  assetPath: z.string().optional().describe('The path of the asset in the AssetDatabase'),
  guid: z.string().optional().describe('The GUID of the asset'),
  position: z.object({
    x: z.number().default(0).describe('X coordinate in the selected position space'),
    y: z.number().default(0).describe('Y coordinate in the selected position space'),
    z: z.number().default(0).describe('Z coordinate in the selected position space')
  }).optional().describe('Position coordinates (defaults to Vector3.zero)'),
  positionSpace: z.enum(['world', 'local']).optional().default('world').describe('Coordinate space for position. "world" (default) guarantees the final world position even when parented; "local" treats position as relative to the parent.'),
  parentPath: z.string().optional().describe('Parent path in the active scene or Prefab contents; an unresolved parent fails without instantiating'),
  parentId: z.number().optional().describe('Parent instance ID in the active context; an unresolved parent fails without instantiating')
});

/**
 * Creates and registers the AddAssetToScene tool with the MCP server
 * 
 * @param server The MCP server to register the tool with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerAddAssetToSceneTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);
  
  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${toolName}`, params);
        const result = await toolHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    }
  );
}

/**
 * Handler function for the AddAssetToScene tool
 * 
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The validated parameters for the tool
 * @param logger The logger instance for diagnostic information
 * @returns A promise that resolves to the tool execution result
 * @throws McpUnityError if validation fails or the request to Unity fails
 */
async function toolHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if (!params.assetPath && !params.guid) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'assetPath' or 'guid' must be provided"
    );
  }
  
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      ...params,
      positionSpace: params.positionSpace ?? 'world'
    }
  });
  
  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to add asset to the active context`
    );
  }
  
  return {
    content: [
      {
        type: response.type || 'text',
        text: response.message || `Successfully added asset to the active context`
      },
      payloadContent({
        message: response.message,
        instanceId: response.instanceId,
        worldPosition: response.worldPosition,
        localPosition: response.localPosition
      })
    ]
  };
}
