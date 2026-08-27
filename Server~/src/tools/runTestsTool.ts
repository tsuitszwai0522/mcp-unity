import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// Constants for the tool
const toolName = 'run_tests';
const toolDescription = 'Runs Unity\'s Test Runner tests';
const paramsSchema = z.object({
  testMode: z.string().optional().default('EditMode').describe('The test mode to run (EditMode or PlayMode) - defaults to EditMode (optional)'),
  testFilter: z.string().optional().default('').describe('The specific test filter to run (e.g. specific test name or class name, must include namespace) (optional)'),
  assemblyNames: z.array(z.string()).optional().describe('Optional assembly-name filter forwarded to Unity Test Framework Filter.assemblyNames. Each entry is matched against the test\'s assembly name (without .dll); prefix an entry with "!" to exclude that assembly. Useful for skipping broken third-party test assemblies (e.g. ["!Unity.Multiplayer.Tools.Adapters.Tests"]) (optional)'),
  returnOnlyFailures: z.boolean().optional().default(true).describe('Whether to show only failed tests in the results (optional)'),
  returnWithLogs: z.boolean().optional().default(false).describe('Whether to return the test logs in the results (optional)')
});

/**
 * Creates and registers the Run Tests tool with the MCP server
 * This tool allows running tests in the Unity Test Runner
 * 
 * @param server The MCP server instance to register with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerRunTestsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);
  
  // Register this tool with the MCP server
  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any = {}) => {
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
 * Handles running tests in Unity
 * 
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The parameters for the tool
 * @returns A promise that resolves to the tool execution result
 */
async function toolHandler(mcpUnity: McpUnity, params: any = {}): Promise<CallToolResult> {
  const {
    testMode = 'EditMode',
    testFilter = '',
    assemblyNames,
    returnOnlyFailures = true,
    returnWithLogs = false
  } = params;

  // Create and wait for the test run
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      testMode,
      testFilter,
      assemblyNames,
      returnOnlyFailures,
      returnWithLogs
    }
  });
  
  // Extract test results
  const testResults = response.results || [];
  const testCount = response.testCount || 0;
  const passCount = response.passCount || 0;
  const failCount = response.failCount || 0;
  const skipCount = response.skipCount || 0;
  const inconclusiveCount = response.inconclusiveCount || 0;

  const payload: Record<string, unknown> = {
    testCount,
    passCount,
    failCount,
    skipCount,
    inconclusiveCount,
    results: testResults
  };

  for (const field of [
    'resultState',
    'durationSeconds',
    'treeNodeCount',
    'filter',
    'error_code'
  ] as const) {
    if (response[field] !== undefined) {
      payload[field] = response[field];
    }
  }

  const result: CallToolResult = {
    content: [
      {
        type: 'text',
        text: response.message || 'Tests completed successfully'
      },
      {
        type: 'text',
        text: JSON.stringify(payload, null, 2)
      }
    ]
  };

  if (!response.success) {
    result.isError = true;
  }

  return result;
}
