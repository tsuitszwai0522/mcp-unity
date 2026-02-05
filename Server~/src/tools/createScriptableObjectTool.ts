import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

// Constants for the tool
const toolName = 'create_scriptable_object';
const toolDescription = 'Creates a ScriptableObject asset with optional field values and saves it to the project';

// Parameter schema for the tool
const paramsSchema = z.object({
  typeName: z.string().describe('The name of the ScriptableObject class to instantiate (e.g., "GameSettings", "MyNamespace.MyScriptableObject")'),
  savePath: z.string().describe('The asset path to save the ScriptableObject (e.g., "Assets/ScriptableObjects/MyAsset.asset")'),
  fieldValues: z.record(z.any()).optional().describe('Optional JSON object of serialized field values to apply to the ScriptableObject')
});

/**
 * Creates and registers the CreateScriptableObject tool with the MCP server
 *
 * @param server The MCP server to register the tool with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerCreateScriptableObjectTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
 * Handler function for the CreateScriptableObject tool
 *
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The validated parameters for the tool
 * @returns A promise that resolves to the tool execution result
 * @throws McpUnityError if validation fails or the request to Unity fails
 */
async function toolHandler(mcpUnity: McpUnity, params: any) {
  if (!params.typeName) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'typeName' must be provided"
    );
  }

  if (!params.savePath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'savePath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to create ScriptableObject`
    );
  }

  return {
    content: [{
      type: response.type,
      text: response.message || `Successfully created ScriptableObject`
    }],
    // Include the asset path in the result for programmatic access
    data: {
      assetPath: response.assetPath,
      typeName: response.typeName
    }
  };
}
