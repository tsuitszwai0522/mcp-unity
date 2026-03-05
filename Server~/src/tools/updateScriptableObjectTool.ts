import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

// Constants for the tool
const toolName = 'update_scriptable_object';
const toolDescription = 'Updates field values on an existing ScriptableObject asset in the project';

// Parameter schema for the tool
const paramsSchema = z.object({
  assetPath: z.string().describe('The asset path of the existing ScriptableObject (e.g., "Assets/ScriptableObjects/MyAsset.asset")'),
  fieldValues: z.record(z.any()).describe('JSON object of serialized field values to update on the ScriptableObject')
});

/**
 * Creates and registers the UpdateScriptableObject tool with the MCP server
 *
 * @param server The MCP server to register the tool with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerUpdateScriptableObjectTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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
 * Handler function for the UpdateScriptableObject tool
 *
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The validated parameters for the tool
 * @returns A promise that resolves to the tool execution result
 * @throws McpUnityError if validation fails or the request to Unity fails
 */
async function toolHandler(mcpUnity: McpUnity, params: any) {
  if (!params.assetPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'assetPath' must be provided"
    );
  }

  if (!params.fieldValues || Object.keys(params.fieldValues).length === 0) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'fieldValues' must be provided and non-empty"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to update ScriptableObject`
    );
  }

  return {
    content: [{
      type: response.type,
      text: response.message || `Successfully updated ScriptableObject`
    }],
    data: {
      assetPath: response.assetPath,
      typeName: response.typeName
    }
  };
}
