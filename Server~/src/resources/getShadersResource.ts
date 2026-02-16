import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { ReadResourceResult } from '@modelcontextprotocol/sdk/types.js';

// Constants for the resource
const resourceName = 'get_shaders';
const resourceUri = 'unity://shaders';
const resourceMimeType = 'application/json';

/**
 * Creates and registers the Shaders resource with the MCP server
 * This resource provides access to all available shaders (project + built-in)
 *
 * @param server The MCP server instance to register with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerGetShadersResource(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering resource: ${resourceName}`);

  // Register this resource with the MCP server
  server.resource(
    resourceName,
    resourceUri,
    {
      description: 'Lists all available shaders in the Unity project and built-in shaders',
      mimeType: resourceMimeType
    },
    async () => {
      try {
        return await resourceHandler(mcpUnity);
      } catch (error) {
        logger.error(`Error handling resource ${resourceName}: ${error}`);
        throw error;
      }
    }
  );
}

/**
 * Handles requests for shader information from Unity
 *
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @returns A promise that resolves to the shaders data
 * @throws McpUnityError if the request to Unity fails
 */
async function resourceHandler(mcpUnity: McpUnity): Promise<ReadResourceResult> {
  const response = await mcpUnity.sendRequest({
    method: resourceName,
    params: {}
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.RESOURCE_FETCH,
      response.message || 'Failed to fetch shaders from Unity'
    );
  }

  const shaders = response.shaders || [];

  const shadersData = {
    shaders: shaders.map((shader: any) => ({
      name: shader.name,
      isBuiltIn: shader.isBuiltIn,
      renderQueue: shader.renderQueue,
      propertyCount: shader.propertyCount,
      ...(shader.path ? { path: shader.path } : {})
    }))
  };

  return {
    contents: [
      {
        uri: resourceUri,
        mimeType: resourceMimeType,
        text: JSON.stringify(shadersData, null, 2)
      }
    ]
  };
}
