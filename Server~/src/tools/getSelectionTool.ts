import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

const toolName = 'get_selection';
const toolDescription = 'Gets the currently selected objects in the Unity Editor (GameObjects in hierarchy and/or assets in Project window)';

const paramsSchema = z.object({});

export function registerGetSelectionTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${toolName}`);
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

async function toolHandler(mcpUnity: McpUnity, params: any) {
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to get selection'
    );
  }

  const sel = response.selection;
  let text = `Selection (${sel.count} object(s)):\n`;

  if (sel.activeGameObject) {
    text += `\nActive GameObject: ${sel.activeGameObject.name}\n`;
    text += `  Path: ${sel.activeGameObject.path}\n`;
    text += `  Instance ID: ${sel.activeGameObject.instanceId}\n`;
  }

  if (sel.gameObjects && sel.gameObjects.length > 0) {
    text += `\nSelected GameObjects:\n`;
    for (const go of sel.gameObjects) {
      text += `  - ${go.name} (${go.path}) [ID: ${go.instanceId}]\n`;
    }
  }

  if (sel.activeObject) {
    text += `\nActive Asset: ${sel.activeObject.name}\n`;
    text += `  Type: ${sel.activeObject.type}\n`;
    text += `  Path: ${sel.activeObject.assetPath}\n`;
    text += `  Instance ID: ${sel.activeObject.instanceId}\n`;
  }

  if (sel.count === 0) {
    text = 'No objects currently selected.';
  }

  return {
    content: [{
      type: 'text' as const,
      text
    }],
    data: {
      selection: response.selection
    }
  };
}
