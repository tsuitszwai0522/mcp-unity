import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { payloadContent } from '../utils/toolPayload.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { explicitAssetPathSchema } from '../utils/assetPathSchema.js';

// Constants for the tool
const toolName = 'create_prefab';
const toolDescription = 'Creates a prefab under this Unity project\'s Assets directory with optional MonoBehaviour script and serialized field values. prefabName is an explicit Assets/... asset-path stem; absolute paths, bare relative paths, and paths that resolve outside Assets are rejected. Existing imported assets keep the current collision behavior and produce an _1, _2, ... sibling. Supports creating Prefab Variants by specifying a basePrefabPath.';

// Parameter schema for the tool
const paramsSchema = z.object({
  componentName: z.string().optional().describe('The name of the MonoBehaviour Component to add to the prefab (optional)'),
  prefabName: explicitAssetPathSchema('Explicit asset-path stem under this project\'s Assets directory, without the .prefab suffix (for example, "Assets/Prefabs/MyPrefab"). Bare relative, absolute, and Assets/../.. escape paths are rejected; imported-asset collisions receive an _1, _2, ... suffix.'),
  fieldValues: z.record(z.string(), z.any()).optional().describe('Optional JSON object of serialized field values to apply to the prefab'),
  basePrefabPath: z.string().optional().describe('Asset path to a base prefab to create a Prefab Variant from (e.g., "Assets/Prefabs/Base.prefab"). When provided, the new prefab will be a Variant of the base prefab.')
});

/**
 * Creates and registers the CreatePrefab tool with the MCP server
 * 
 * @param server The MCP server to register the tool with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerCreatePrefabTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
 * Handler function for the CreatePrefab tool
 * 
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The validated parameters for the tool
 * @returns A promise that resolves to the tool execution result
 * @throws McpUnityError if validation fails or the request to Unity fails
 */
async function toolHandler(mcpUnity: McpUnity, params: any) {
  if (!params.prefabName) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'prefabName' must be provided"
    );
  }
  
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });
  
  const hasFieldFailures = Array.isArray(response.failedFields) && response.failedFields.length > 0;

  if (!response.success && !hasFieldFailures) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to create prefab`
    );
  }
  
  const result: CallToolResult = {
    content: [
      {
        type: response.type || 'text',
        text: response.message || `Successfully created prefab`
      },
      payloadContent({
          prefabPath: response.prefabPath,
          updatedFields: response.updatedFields,
          failedFields: response.failedFields,
          warnings: response.warnings,
          message: response.message
        })
    ]
  };

  if (hasFieldFailures) {
    result.isError = true;
  }

  return result;
}
