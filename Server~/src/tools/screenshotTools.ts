import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// --- screenshot_game_view ---

const gameViewToolName = 'screenshot_game_view';
const gameViewToolDescription = 'Captures a screenshot from the Game View, reflecting what the player sees. Set force_focus=true to force-focus the Game View tab before capture (prevents capturing the Scene View when it\'s the active tab).';

const gameViewParamsSchema = z.object({
  width: z.number().int().optional().default(960).describe('Screenshot width in pixels'),
  height: z.number().int().optional().default(540).describe('Screenshot height in pixels'),
  force_focus: z
    .boolean()
    .optional()
    .describe('Force-focus the Game View tab before capture (adds a 1-frame delay). Use when Scene View is the active tab and you need the actual Game View render. Default: false.')
});

// --- screenshot_scene_view ---

const sceneViewToolName = 'screenshot_scene_view';
const sceneViewToolDescription = 'Captures a screenshot from the Scene View, reflecting the editor camera perspective';

const sceneViewParamsSchema = z.object({
  width: z.number().int().optional().default(960).describe('Screenshot width in pixels'),
  height: z.number().int().optional().default(540).describe('Screenshot height in pixels')
});

// --- screenshot_camera ---

const cameraToolName = 'screenshot_camera';
const cameraToolDescription = 'Captures a screenshot from a specific Camera in the scene';

const cameraParamsSchema = z.object({
  cameraPath: z.string().optional().describe('Camera GameObject path, null uses Main Camera'),
  cameraInstanceId: z.number().int().optional().describe('Camera GameObject instance ID'),
  width: z.number().int().optional().default(960).describe('Screenshot width in pixels'),
  height: z.number().int().optional().default(540).describe('Screenshot height in pixels')
});

/**
 * Handles screenshot response from Unity and returns MCP image content
 */
async function screenshotHandler(mcpUnity: McpUnity, toolName: string, params: any): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: toolName,
    params
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || `Failed to capture screenshot via ${toolName}`
    );
  }

  return {
    content: [{
      type: 'image',
      mimeType: response.mimeType || 'image/png',
      data: response.data
    }]
  };
}

export function registerScreenshotTools(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  // Register screenshot_game_view
  logger.info(`Registering tool: ${gameViewToolName}`);
  server.tool(
    gameViewToolName,
    gameViewToolDescription,
    gameViewParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${gameViewToolName}`, params);
        const result = await screenshotHandler(mcpUnity, gameViewToolName, params);
        logger.info(`Tool execution successful: ${gameViewToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${gameViewToolName}`, error);
        throw error;
      }
    }
  );

  // Register screenshot_scene_view
  logger.info(`Registering tool: ${sceneViewToolName}`);
  server.tool(
    sceneViewToolName,
    sceneViewToolDescription,
    sceneViewParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${sceneViewToolName}`, params);
        const result = await screenshotHandler(mcpUnity, sceneViewToolName, params);
        logger.info(`Tool execution successful: ${sceneViewToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${sceneViewToolName}`, error);
        throw error;
      }
    }
  );

  // Register screenshot_camera
  logger.info(`Registering tool: ${cameraToolName}`);
  server.tool(
    cameraToolName,
    cameraToolDescription,
    cameraParamsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${cameraToolName}`, params);
        const result = await screenshotHandler(mcpUnity, cameraToolName, params);
        logger.info(`Tool execution successful: ${cameraToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${cameraToolName}`, error);
        throw error;
      }
    }
  );
}
