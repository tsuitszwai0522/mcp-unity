import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { jsonSchemaToZodShape } from '../utils/schemaConverter.js';

/**
 * Query Unity for external tools and register them dynamically.
 * Called after WebSocket connection is established.
 * Only registers tools not already registered by built-in modules.
 */
export async function registerDynamicTools(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
): Promise<number> {
  try {
    const response = await mcpUnity.sendRequest({
      method: 'list_tools',
      params: {}
    });

    if (!response.success || !response.tools) {
      logger.info('No external tools found in Unity');
      return 0;
    }

    let registered = 0;

    for (const tool of response.tools) {
      try {
        const zodShape = jsonSchemaToZodShape(tool.parameterSchema);

        server.tool(
          tool.name,
          tool.description || `External tool: ${tool.name}`,
          zodShape,
          async (params: any) => {
            logger.info(`Executing external tool: ${tool.name}`);

            try {
              const result = await mcpUnity.sendRequest({
                method: tool.name,
                params
              });

              // Image-shaped responses (e.g. external screenshot/capture tools) carry base64 pixel
              // data — emit a real MCP image content block instead of stringifying it (which would
              // dump the entire base64 as text, or drop the image entirely when a message exists).
              // Contract: { success, type: 'image', mimeType, data: <base64>, message? }.
              if (result && result.type === 'image' && typeof result.data === 'string') {
                const content: Array<{ type: 'text'; text: string } | { type: 'image'; data: string; mimeType: string }> = [];
                if (result.message) {
                  content.push({ type: 'text' as const, text: result.message });
                }
                content.push({
                  type: 'image' as const,
                  data: result.data,
                  mimeType: result.mimeType || 'image/png'
                });
                return { content };
              }

              const text = result.message || JSON.stringify(result, null, 2);
              return {
                content: [{ type: 'text' as const, text }],
                ...(result.success !== undefined ? { data: result } : {})
              };
            } catch (error) {
              logger.error(`External tool ${tool.name} failed`, error);
              throw error;
            }
          }
        );

        registered++;
        logger.info(`Registered external tool: ${tool.name}`);
      } catch (error) {
        logger.error(`Failed to register external tool ${tool.name}`, error);
      }
    }

    if (registered > 0) {
      logger.info(`Total external tools registered: ${registered}`);
      try {
        await server.server.sendToolListChanged();
      } catch {
        // sendToolListChanged may not be supported by all MCP SDK versions
        logger.debug('sendToolListChanged not available or failed');
      }
    }

    return registered;
  } catch (error) {
    logger.error('Failed to query external tools from Unity', error);
    return 0;
  }
}
