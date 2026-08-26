import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// --- screenshot_game_view ---

const gameViewToolName = 'screenshot_game_view';
const gameViewToolDescription = 'Captures a screenshot from the Game View, reflecting what the player sees. Only frameFresh=verified means the pixels reflect the current scene. When frameFreshReason includes game_view_not_active_tab, retry with force_focus=true so the Game View becomes the active tab and rerenders before capture through Unity\'s normal path. When it includes repaint_immediately_unavailable:, retry with force_focus=true only when isolatedCameraCount=0; focus cannot repair the post-isolation frame while isolated cameras exist. no_camera_render has no force-focus remediation. While Prefab contents are open, failed Game View capture never falls back to a loaded scene Main Camera.';

function screenshotDimension(name: 'width' | 'height') {
  const message = `Screenshot ${name} must be between 1 and 4096 pixels (maximum 4096)`;
  return z.number().int().min(1, message).max(4096, message);
}

const gameViewParamsSchema = z.object({
  width: screenshotDimension('width').optional().default(960).describe('Screenshot width in pixels (1-4096)'),
  height: screenshotDimension('height').optional().default(540).describe('Screenshot height in pixels (1-4096)'),
  force_focus: z
    .boolean()
    .optional()
    .describe('Force-focus the Game View tab before capture (waits 2 editor updates). Use when frameFreshReason indicates this remediation is applicable. Default: false.')
});

// --- screenshot_scene_view ---

const sceneViewToolName = 'screenshot_scene_view';
const sceneViewToolDescription = 'Captures a screenshot from the Scene View, reflecting the editor camera perspective';

const sceneViewParamsSchema = z.object({
  width: screenshotDimension('width').optional().default(960).describe('Screenshot width in pixels (1-4096)'),
  height: screenshotDimension('height').optional().default(540).describe('Screenshot height in pixels (1-4096)')
});

// --- screenshot_camera ---

const cameraToolName = 'screenshot_camera';
const cameraToolDescription = 'Captures a screenshot from a specific Camera in the active GameObject context. With no locator, uses Camera.main when no Prefab session is active; during an active Prefab session, requires an enabled MainCamera-tagged Camera inside the Prefab contents and never falls back to loaded scene cameras';

const cameraParamsSchema = z.object({
  cameraPath: z.string().optional().describe('Camera GameObject path in the active scene or Prefab context; omitted uses the context-specific default Camera described by the tool'),
  cameraInstanceId: z.number().int().optional().describe('Camera GameObject instance ID'),
  width: screenshotDimension('width').optional().default(960).describe('Screenshot width in pixels (1-4096)'),
  height: screenshotDimension('height').optional().default(540).describe('Screenshot height in pixels (1-4096)')
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

  if (typeof response.data !== 'string') {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      `Invalid screenshot response from ${toolName}: expected image data to be a string`
    );
  }

  const message = response.message || `Screenshot captured via ${toolName}`;
  const hasRequiredUnityMetadata = typeof response.capturePath === 'string'
    && typeof response.degraded === 'boolean';
  const messageHasCapturePath = message.includes('capturePath=');
  const messageHasDegraded = message.includes('degraded=');
  let text = message;

  const diagnostics: string[] = [];
  let coreUnityMetadataAbsent = false;
  let s8bUnityMetadataAbsent = false;
  if (!messageHasCapturePath) {
    if (typeof response.capturePath === 'string') {
      diagnostics.push(`capturePath=${response.capturePath}`);
    } else {
      diagnostics.push('capturePath=unknown');
      coreUnityMetadataAbsent = true;
    }
  }
  if (!messageHasDegraded) {
    if (typeof response.degraded === 'boolean') {
      diagnostics.push(`degraded=${response.degraded}`);
    } else {
      diagnostics.push('degraded=unknown');
      coreUnityMetadataAbsent = true;
    }
  }
  if (hasRequiredUnityMetadata
    && typeof response.degradedReason === 'string'
    && !message.includes('degradedReason=')) {
    diagnostics.push(`degradedReason=${response.degradedReason}`);
  }
  if (hasRequiredUnityMetadata
    && typeof response.gameViewWindowCreated === 'boolean'
    && !message.includes('gameViewWindowCreated=')) {
    diagnostics.push(`gameViewWindowCreated=${response.gameViewWindowCreated}`);
  }

  if (toolName === gameViewToolName) {
    if (!message.includes('frameFresh=')) {
      if (typeof response.frameFresh === 'string') {
        diagnostics.push(`frameFresh=${response.frameFresh}`);
      } else {
        diagnostics.push('frameFresh=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('cameraRenders=')) {
      if (Number.isInteger(response.cameraRenders)) {
        diagnostics.push(`cameraRenders=${response.cameraRenders}`);
      } else {
        diagnostics.push('cameraRenders=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('frameFreshReason=')) {
      if (typeof response.frameFreshReason === 'string') {
        diagnostics.push(`frameFreshReason=${response.frameFreshReason}`);
      } else {
        diagnostics.push('frameFreshReason=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('isolatedCameras=')) {
      if (Array.isArray(response.isolatedCameras)) {
        diagnostics.push(`isolatedCameras=${JSON.stringify(response.isolatedCameras)}`);
      } else {
        diagnostics.push('isolatedCameras=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('isolatedCameraCount=')) {
      if (Number.isInteger(response.isolatedCameraCount)) {
        diagnostics.push(`isolatedCameraCount=${response.isolatedCameraCount}`);
      } else {
        diagnostics.push('isolatedCameraCount=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('contextCameras=')) {
      if (Array.isArray(response.contextCameras)) {
        diagnostics.push(`contextCameras=${JSON.stringify(response.contextCameras)}`);
      } else {
        diagnostics.push('contextCameras=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
    if (!message.includes('contextCameraCount=')) {
      if (Number.isInteger(response.contextCameraCount)) {
        diagnostics.push(`contextCameraCount=${response.contextCameraCount}`);
      } else {
        diagnostics.push('contextCameraCount=unknown');
        s8bUnityMetadataAbsent = true;
      }
    }
  }

  if (coreUnityMetadataAbsent) {
    diagnostics.push('(core unity-side metadata absent)');
  }
  if (s8bUnityMetadataAbsent) {
    diagnostics.push('(S8-b unity-side metadata absent)');
  }
  if (diagnostics.length > 0) {
    text += `\n${diagnostics.join(' ')}`;
  }

  return {
    content: [
      { type: 'text' as const, text },
      {
        type: 'image' as const,
        mimeType: response.mimeType || 'image/png',
        data: response.data
      }
    ]
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
