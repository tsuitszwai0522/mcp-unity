import * as z from 'zod';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

const toolName = 'remove_component';
const toolDescription = 'Removes a component from a GameObject. Identifies the GameObject by instance ID or hierarchy path.';
const paramsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the GameObject'),
  objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
  componentName: z.string().describe('The name of the component to remove'),
});

/**
 * Creates and registers the Remove Component tool with the MCP server
 */
export function registerRemoveComponentTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
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

async function toolHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  // Validate parameters - require either instanceId or objectPath
  if ((params.instanceId === undefined || params.instanceId === null) &&
      (!params.objectPath || params.objectPath.trim() === '')) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  if (!params.componentName) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'componentName' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      instanceId: params.instanceId,
      objectPath: params.objectPath,
      componentName: params.componentName,
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to remove component from GameObject'
    );
  }

  return {
    content: [{
      type: response.type || 'text',
      text: response.message || 'Successfully removed component from GameObject'
    }]
  };
}
