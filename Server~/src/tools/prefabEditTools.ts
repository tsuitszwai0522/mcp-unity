import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

// ============================================================================
// OPEN PREFAB CONTENTS TOOL
// ============================================================================

const openPrefabContentsToolName = 'open_prefab_contents';
const openPrefabContentsToolDescription =
  'Loads a Prefab asset into an isolated editing environment using PrefabUtility.LoadPrefabContents(). ' +
  'While open, other tools (create_ui_element, reparent_gameobject, update_component, etc.) can modify the Prefab\'s internal structure. ' +
  'Call save_prefab_contents to save changes or discard them.';

const openPrefabContentsParamsSchema = z.object({
  prefabPath: z.string().describe('The asset path to the Prefab (e.g., "Assets/Prefabs/MyPrefab.prefab")')
});

/**
 * Registers the Open Prefab Contents tool with the MCP server
 */
export function registerOpenPrefabContentsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${openPrefabContentsToolName}`);

  server.tool(
    openPrefabContentsToolName,
    openPrefabContentsToolDescription,
    openPrefabContentsParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${openPrefabContentsToolName}`, params);
        const result = await openPrefabContentsHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${openPrefabContentsToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${openPrefabContentsToolName}`, error);
        throw error;
      }
    }
  );
}

async function openPrefabContentsHandler(mcpUnity: McpUnity, params: any) {
  if (!params.prefabPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Required parameter 'prefabPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: openPrefabContentsToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to open prefab contents'
    );
  }

  // Build text output with hierarchy info
  let text = response.message || `Opened Prefab: ${params.prefabPath}`;
  text += `\nRoot: ${response.rootName} (instanceId: ${response.rootInstanceId})`;

  if (response.children && Array.isArray(response.children)) {
    text += '\nHierarchy:\n';
    text += formatHierarchy(response.children, '  ');
  }

  return {
    content: [{
      type: 'text' as const,
      text
    }],
    data: {
      prefabPath: response.prefabPath,
      rootInstanceId: response.rootInstanceId,
      rootName: response.rootName,
      children: response.children
    }
  };
}

function formatHierarchy(children: any[], indent: string): string {
  let result = '';
  for (const child of children) {
    result += `${indent}- ${child.name} (instanceId: ${child.instanceId})`;
    if (child.childCount > 0) {
      result += ` [${child.childCount} children]`;
    }
    result += '\n';
    if (child.children && Array.isArray(child.children)) {
      result += formatHierarchy(child.children, indent + '  ');
    }
  }
  return result;
}

// ============================================================================
// SAVE PREFAB CONTENTS TOOL
// ============================================================================

const savePrefabContentsToolName = 'save_prefab_contents';
const savePrefabContentsToolDescription =
  'Saves or discards changes to a Prefab that was opened with open_prefab_contents. ' +
  'By default saves changes back to the .prefab asset. Set discard=true to abandon changes.';

const savePrefabContentsParamsSchema = z.object({
  discard: z.boolean().optional().default(false).describe('If true, discards changes instead of saving. Default: false')
});

/**
 * Registers the Save Prefab Contents tool with the MCP server
 */
export function registerSavePrefabContentsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${savePrefabContentsToolName}`);

  server.tool(
    savePrefabContentsToolName,
    savePrefabContentsToolDescription,
    savePrefabContentsParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${savePrefabContentsToolName}`, params);
        const result = await savePrefabContentsHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${savePrefabContentsToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${savePrefabContentsToolName}`, error);
        throw error;
      }
    }
  );
}

async function savePrefabContentsHandler(mcpUnity: McpUnity, params: any) {
  const response = await mcpUnity.sendRequest({
    method: savePrefabContentsToolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to save prefab contents'
    );
  }

  return {
    content: [{
      type: response.type,
      text: response.message || `Prefab contents ${params.discard ? 'discarded' : 'saved'}`
    }],
    data: {
      prefabPath: response.prefabPath,
      discarded: response.discarded
    }
  };
}
