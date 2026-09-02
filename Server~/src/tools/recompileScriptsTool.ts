import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { PAYLOAD_MAX_CHARS, payloadContent } from '../utils/toolPayload.js';

// Constants for the tool
const toolName = 'recompile_scripts';
const toolDescription = 'Refreshes the AssetDatabase by default to discover file changes, then recompiles scripts. With refreshAssets=false, recompiles only scripts already known to the AssetDatabase and does not discover added or deleted files.';
const paramsSchema = z.object({
  returnWithLogs: z.boolean().optional().default(true).describe('Whether to return compilation logs'),
  logsLimit: z.number().int().min(0).max(1000).optional().default(100).describe('Maximum number of compilation logs to return'),
  refreshAssets: z.boolean().optional().default(true).describe('Whether to run AssetDatabase.Refresh() before recompiling so Unity discovers added, deleted, or changed files; even a no-op refresh typically blocks the Unity main thread for about 200–350 ms')
});

type RecompileMetadata = {
  refreshed: boolean | 'unknown';
  refreshDurationMs: number | 'unknown';
  compilationWasAlreadyInProgress: boolean | null | 'unknown';
  error_code?: string;
};

const fitLogsToPayloadLimit = (
  message: string,
  logs: unknown[],
  totalLogs: number,
  metadata: RecompileMetadata
): unknown[] => {
  let low = 0;
  let high = logs.length;
  let fittedCount = 0;

  while (low <= high) {
    const middle = Math.floor((low + high) / 2);
    const candidateLogs = logs.slice(0, middle);
    const candidate = {
      message,
      logs: candidateLogs,
      truncated: candidateLogs.length < totalLogs,
      totalLogs,
      returnedLogs: candidateLogs.length,
      ...metadata
    };

    if (JSON.stringify(candidate).length <= PAYLOAD_MAX_CHARS) {
      fittedCount = middle;
      low = middle + 1;
    } else {
      high = middle - 1;
    }
  }

  return logs.slice(0, fittedCount);
};

/**
 * Creates and registers the Recompile Scripts tool with the MCP server
 * This tool allows recompiling all scripts in the Unity project
 *
 * @param server The MCP server instance to register with
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param logger The logger instance for diagnostic information
 */
export function registerRecompileScriptsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  // Register this tool with the MCP server
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
 * Handles recompile scripts tool requests
 *
 * @param mcpUnity The McpUnity instance to communicate with Unity
 * @param params The parameters for the tool
 * @returns A promise that resolves to the tool execution result
 */
async function toolHandler(mcpUnity: McpUnity, params: z.infer<typeof paramsSchema>): Promise<CallToolResult> {
  // Validate and prepare parameters
  const returnWithLogs = params.returnWithLogs ?? true;
  const logsLimit = Math.max(0, Math.min(1000, params.logsLimit ?? 100));
  const refreshAssets = params.refreshAssets ?? true;

  // Send to Unity with validated parameters
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: {
      returnWithLogs,
      logsLimit,
      refreshAssets
    }
  });

  const baseMessage = response.message || (response.success
    ? 'Scripts recompiled successfully'
    : 'Failed to recompile scripts');
  const hasRefreshed = Object.prototype.hasOwnProperty.call(response, 'refreshed');
  const hasRefreshDurationMs = Object.prototype.hasOwnProperty.call(
    response,
    'refreshDurationMs'
  );
  const hasCompilationWasAlreadyInProgress = Object.prototype.hasOwnProperty.call(
    response,
    'compilationWasAlreadyInProgress'
  );
  const availableLogs = Array.isArray(response.logs) ? response.logs : [];
  const totalLogs = typeof response.totalLogs === 'number'
    ? response.totalLogs
    : availableLogs.length;
  const refreshed = typeof response.refreshed === 'boolean'
    ? response.refreshed
    : 'unknown';
  const refreshDurationMs = typeof response.refreshDurationMs === 'number'
    ? response.refreshDurationMs
    : 'unknown';
  const compilationWasAlreadyInProgress =
    typeof response.compilationWasAlreadyInProgress === 'boolean'
      ? response.compilationWasAlreadyInProgress
      : hasCompilationWasAlreadyInProgress
        && response.compilationWasAlreadyInProgress === null
        ? null
        : 'unknown';
  const errorCode = typeof response.error_code === 'string'
    ? response.error_code
    : undefined;
  const metadata: RecompileMetadata = {
    refreshed,
    refreshDurationMs,
    compilationWasAlreadyInProgress,
    ...(errorCode === undefined ? {} : { error_code: errorCode })
  };
  const missingMetadata: string[] = [];
  if (!hasRefreshed) {
    missingMetadata.push('refreshed=unknown');
  }
  if (!hasRefreshDurationMs) {
    missingMetadata.push('refreshDurationMs=unknown');
  }
  if (!hasCompilationWasAlreadyInProgress) {
    missingMetadata.push('compilationWasAlreadyInProgress=unknown');
  }
  const invalidMetadata: string[] = [];
  if (hasRefreshed && refreshed === 'unknown') {
    invalidMetadata.push('refreshed=unknown');
  }
  if (hasRefreshDurationMs && refreshDurationMs === 'unknown') {
    invalidMetadata.push('refreshDurationMs=unknown');
  }
  if (hasCompilationWasAlreadyInProgress
    && compilationWasAlreadyInProgress === 'unknown') {
    invalidMetadata.push('compilationWasAlreadyInProgress=unknown');
  }
  let message = missingMetadata.length === 0
    ? baseMessage
    : `${baseMessage}\n${missingMetadata.join(' ')} (unity-side recompilation metadata absent)`;
  if (invalidMetadata.length > 0) {
    message += `\n${invalidMetadata.join(' ')} (unity-side recompilation metadata invalid)`;
  }
  const logs = fitLogsToPayloadLimit(
    message,
    availableLogs,
    totalLogs,
    metadata
  );
  const returnedLogs = logs.length;

  const result: CallToolResult = {
    content: [
      {
        type: 'text',
        text: message
      },
      payloadContent({
        message,
        logs,
        truncated: returnedLogs < totalLogs,
        totalLogs,
        returnedLogs,
        ...metadata
      })
    ]
  };

  if (!response.success) {
    result.isError = true;
  }

  return result;
}
