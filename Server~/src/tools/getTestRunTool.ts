import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

const toolName = 'get_test_run';
const toolDescription = 'Gets a Unity test run by GUID runId, or the most recent run when runId is omitted. Use the omitted form after a PlayMode domain reload disconnects the original request. Running responses expose expectedArtifactPath with artifactExists:false; completed responses publish artifactPath only after validating the XML, with result rows rebuilt from that artifact while SessionState stores bounded metadata only. Only one run can be tracked because Unity callbacks have no run GUID: a second RunStarted invalidates the record as untrusted. The lock ends on RunFinished or is released as stale after 24 hours. Only the 20 most recent owned artifacts are retained.';
const paramsSchema = z.object({
  runId: z.uuid().optional().describe('Unity TestRunnerApi run GUID in canonical D format. Omit to get the most recent run in this Editor session.')
});

export function registerGetTestRunTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: z.infer<typeof paramsSchema> = {}) => {
      try {
        logger.info(`Executing tool: ${toolName}`, params);
        const requestParams = params.runId === undefined
          ? {}
          : { runId: params.runId };
        const response = await mcpUnity.sendRequest({
          method: toolName,
          params: requestParams
        });

        const payload: Record<string, unknown> = {};
        for (const field of [
          'runId',
          'status',
          'artifactPath',
          'expectedArtifactPath',
          'artifactExists',
          'filter',
          'startedAt',
          'resultState',
          'durationSeconds',
          'testCount',
          'treeNodeCount',
          'passCount',
          'failCount',
          'skipCount',
          'inconclusiveCount',
          'results',
          'error_code',
          'artifactError',
          'requestedRunId',
          'invalidatedRunId',
          'lockReleased',
          'staleAfterSeconds'
        ] as const) {
          if (response[field] !== undefined) {
            payload[field] = response[field];
          }
        }

        const result: CallToolResult = {
          content: [
            {
              type: 'text',
              text: response.message || 'Test run status retrieved.'
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

        logger.info(`Tool execution successful: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    }
  );
}
