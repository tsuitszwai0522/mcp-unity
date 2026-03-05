import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';

// --- get_editor_state ---

const getStateName = 'get_editor_state';
const getStateDescription = 'Gets the current Unity Editor state including play mode, compilation status, active scene, and build platform';

const getStateParamsSchema = z.object({});

// --- set_editor_state ---

const setStateName = 'set_editor_state';
const setStateDescription = 'Controls Unity Editor play mode: play, pause, unpause, or stop';

const setStateParamsSchema = z.object({
  action: z.string().describe('The action to perform: "play", "pause", "unpause", or "stop"')
});

export function registerEditorStateTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  // Register get_editor_state
  logger.info(`Registering tool: ${getStateName}`);
  server.tool(
    getStateName,
    getStateDescription,
    getStateParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${getStateName}`);
        const response = await mcpUnity.sendRequest({
          method: getStateName,
          params
        });

        if (!response.success) {
          throw new McpUnityError(
            ErrorType.TOOL_EXECUTION,
            response.message || 'Failed to get editor state'
          );
        }

        const state = response.state;
        let text = `Unity Editor State:\n`;
        text += `  Playing: ${state.isPlaying}\n`;
        text += `  Paused: ${state.isPaused}\n`;
        text += `  Compiling: ${state.isCompiling}\n`;
        text += `  Current Scene: ${state.currentScene || '(unsaved)'}\n`;
        text += `  Platform: ${state.platform}`;

        logger.info(`Tool execution successful: ${getStateName}`);
        return {
          content: [{
            type: 'text' as const,
            text
          }],
          data: {
            state: response.state
          }
        };
      } catch (error) {
        logger.error(`Tool execution failed: ${getStateName}`, error);
        throw error;
      }
    }
  );

  // Register set_editor_state
  logger.info(`Registering tool: ${setStateName}`);
  server.tool(
    setStateName,
    setStateDescription,
    setStateParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${setStateName}`, params);
        const response = await mcpUnity.sendRequest({
          method: setStateName,
          params
        });

        if (!response.success) {
          throw new McpUnityError(
            ErrorType.TOOL_EXECUTION,
            response.message || 'Failed to set editor state'
          );
        }

        logger.info(`Tool execution successful: ${setStateName}`);
        return {
          content: [{
            type: 'text' as const,
            text: response.message
          }],
          data: {
            state: response.state
          }
        };
      } catch (error) {
        logger.error(`Tool execution failed: ${setStateName}`, error);
        throw error;
      }
    }
  );
}
