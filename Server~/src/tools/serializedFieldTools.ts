import * as z from 'zod';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// ============================================================================
// Read Serialized Fields Tool
// ============================================================================

const readToolName = 'read_serialized_fields';
const readToolDescription = `Reads serialized fields from a component using Unity's SerializedProperty API.
More reliable than get_gameobject for reading specific component fields.
Accepts both serialized names (m_Color, m_Sprite) and property names (color, sprite).
If fieldNames is omitted, returns all visible serialized fields.`;

const readParamsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the GameObject'),
  objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
  componentName: z.string().describe('The name of the component to read from (e.g., "Image", "Text", "Button")'),
  fieldNames: z.array(z.string()).optional().describe('Specific field names to read. Accepts both serialized names (m_Color) and property names (color). If omitted, reads all visible fields.'),
});

export function registerReadSerializedFieldsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${readToolName}`);

  server.tool(
    readToolName,
    readToolDescription,
    readParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${readToolName}`, params);
        const result = await readHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${readToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${readToolName}`, error);
        throw error;
      }
    }
  );
}

async function readHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if ((params.instanceId === undefined || params.instanceId === null) &&
      (!params.objectPath || params.objectPath.trim() === '')) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  if (!params.componentName || params.componentName.trim() === '') {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'componentName' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: readToolName,
    params: {
      instanceId: params.instanceId,
      objectPath: params.objectPath,
      componentName: params.componentName,
      fieldNames: params.fieldNames,
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to read serialized fields'
    );
  }

  // Format fields as readable text
  let text = response.message + '\n\n';
  if (response.fields) {
    text += JSON.stringify(response.fields, null, 2);
  }

  return {
    content: [{
      type: 'text',
      text
    }],
    data: {
      instanceId: response.instanceId,
      componentName: response.componentName,
      fields: response.fields
    }
  };
}

// ============================================================================
// Write Serialized Fields Tool
// ============================================================================

const writeToolName = 'write_serialized_fields';
const writeToolDescription = `Writes serialized fields on a component using Unity's SerializedProperty API.
Best for: Unity built-in component fields (m_Color, m_Sprite, etc.) and when exact serialized field control is needed.
Does NOT add missing components — use update_component for that.
Accepts both serialized names (m_Color) and property names (color).
For object references, use asset path string, instance ID number, or structured {instanceId: N} / {assetPath: "..."} / {objectPath: "Path/To/Object"}.`;

const writeParamsSchema = z.object({
  instanceId: z.number().optional().describe('The instance ID of the GameObject'),
  objectPath: z.string().optional().describe('The path of the GameObject in the hierarchy (alternative to instanceId)'),
  componentName: z.string().describe('The name of the component to write to (e.g., "Image", "Text", "Button")'),
  fieldData: z.record(z.any()).describe('Object mapping field names to values. Accepts both serialized names (m_Color) and property names (color). For colors: {r, g, b, a}. For vectors: {x, y, z}. For object refs: asset path string or instance ID.'),
});

export function registerWriteSerializedFieldsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${writeToolName}`);

  server.tool(
    writeToolName,
    writeToolDescription,
    writeParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${writeToolName}`, params);
        const result = await writeHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${writeToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${writeToolName}`, error);
        throw error;
      }
    }
  );
}

async function writeHandler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  if ((params.instanceId === undefined || params.instanceId === null) &&
      (!params.objectPath || params.objectPath.trim() === '')) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  if (!params.componentName || params.componentName.trim() === '') {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'componentName' must be provided"
    );
  }

  if (!params.fieldData || Object.keys(params.fieldData).length === 0) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'fieldData' must be provided and non-empty"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: writeToolName,
    params: {
      instanceId: params.instanceId,
      objectPath: params.objectPath,
      componentName: params.componentName,
      fieldData: params.fieldData,
    }
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || 'Failed to write serialized fields'
    );
  }

  let text = response.message;
  if (response.warnings && response.warnings.length > 0) {
    text += '\n\nWarnings:\n' + response.warnings.map((w: string) => `  - ${w}`).join('\n');
  }

  return {
    content: [{
      type: 'text',
      text
    }],
    data: {
      instanceId: response.instanceId,
      updatedFields: response.updatedFields,
      warnings: response.warnings
    }
  };
}
