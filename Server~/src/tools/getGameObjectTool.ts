import * as z from "zod";
import { Logger } from "../utils/logger.js";
import { McpUnity } from "../unity/mcpUnity.js";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { McpUnityError, ErrorType } from "../utils/errors.js";
import { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

// Constants for the tool
const toolName = "get_gameobject";
const toolDescription =
  "Retrieves detailed information about a specific GameObject by instance ID, name, or hierarchical path (e.g., 'Parent/Child/MyObject'). Returns all component properties including Transform position, rotation, scale, and more.";
const paramsSchema = z.object({
  idOrName: z
    .string()
    .describe(
      "The instance ID (integer), name, or hierarchical path of the GameObject to retrieve. Use hierarchical paths like 'Canvas/Panel/Button' for nested objects."
    ),
  maxDepth: z
    .number()
    .int()
    .gte(-1)
    .optional()
    .describe(
      "Maximum depth for traversing children. 0 = target only, 1 = direct children, -1 = unlimited (default)"
    ),
  includeChildren: z
    .boolean()
    .optional()
    .describe(
      "Whether to include child GameObjects in the response. Default: true"
    ),
});

/**
 * Creates and registers the Get GameObject tool with the MCP server
 * This tool allows retrieving detailed information about GameObjects in Unity scenes
 *
 * @param server The MCP server instance to register with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerGetGameObjectTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${toolName}`);

  // Register this tool with the MCP server
  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: z.infer<typeof paramsSchema>) => {
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
 * Handles requests for GameObject information from Unity
 *
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The parameters for the tool
 * @returns A promise that resolves to the tool execution result
 * @throws McpUnityError if the request to Unity fails
 */
async function toolHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof paramsSchema>
): Promise<CallToolResult> {
  const { idOrName, maxDepth, includeChildren } = params;

  // Send request to Unity
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      idOrName: idOrName,
      ...(maxDepth !== undefined && { maxDepth }),
      ...(includeChildren !== undefined && { includeChildren }),
    },
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to fetch GameObject from Unity"
    );
  }

  return {
    content: [
      {
        type: "text",
        text: JSON.stringify(response, null, 2),
      },
    ],
  };
}

// ============================================================================
// get_gameobjects_by_name (plural — glob match)
// ============================================================================

const byNameToolName = "get_gameobjects_by_name";
const byNameToolDescription =
  "Finds ALL GameObjects whose name matches a glob pattern (supports '*' and '?'). Returns an array of matches with hierarchical paths. Use this instead of get_gameobject when multiple instances share the same name (e.g. 'CBCardUI(Clone)').";
const byNameParamsSchema = z.object({
  name: z
    .string()
    .describe(
      "Glob pattern matched against GameObject.name. Examples: 'CBCardUI(Clone)' (exact), 'CBCardUI*' (prefix), '*Button*' (contains), '?layer' (single-char wildcard)."
    ),
  includeInactive: z
    .boolean()
    .optional()
    .describe("Include inactive GameObjects. Default: true"),
  maxDepth: z
    .number()
    .int()
    .gte(-1)
    .optional()
    .describe(
      "Max child-traversal depth for each match. 0 = target only (default), 1 = direct children, -1 = unlimited. Deep results can be token-heavy — raise explicitly when needed."
    ),
  includeChildren: z
    .boolean()
    .optional()
    .describe("Include child GameObjects in each match. Default: false"),
  limit: z
    .number()
    .int()
    .min(1)
    .max(1000)
    .optional()
    .describe("Max number of matches to return. Default: 100"),
});

export function registerGetGameObjectsByNameTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${byNameToolName}`);
  server.tool(
    byNameToolName,
    byNameToolDescription,
    byNameParamsSchema.shape,
    async (params: z.infer<typeof byNameParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${byNameToolName}`, params);
        const response = await mcpUnity.sendRequest({
          method: byNameToolName,
          params,
        });
        if (!response.success) {
          throw new McpUnityError(
            ErrorType.TOOL_EXECUTION,
            response.message || "Failed to find GameObjects by name"
          );
        }
        logger.info(`Tool execution successful: ${byNameToolName}`);
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(response, null, 2),
            },
          ],
        };
      } catch (error) {
        logger.error(`Tool execution failed: ${byNameToolName}`, error);
        throw error;
      }
    }
  );
}
