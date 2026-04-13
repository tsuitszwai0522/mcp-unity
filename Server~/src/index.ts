// Import MCP SDK components
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { McpUnity } from './unity/mcpUnity.js';
import { Logger, LogLevel } from './utils/logger.js';
import { registerCreateSceneTool } from './tools/createSceneTool.js';
import { registerMenuItemTool } from './tools/menuItemTool.js';
import { registerSelectGameObjectTool } from './tools/selectGameObjectTool.js';
import { registerAddPackageTool } from './tools/addPackageTool.js';
import { registerRunTestsTool } from './tools/runTestsTool.js';
import { registerSendConsoleLogTool } from './tools/sendConsoleLogTool.js';
import { registerGetConsoleLogsTool } from './tools/getConsoleLogsTool.js';
import { registerUpdateComponentTool } from './tools/updateComponentTool.js';
import { registerRemoveComponentTool } from './tools/removeComponentTool.js';
import { registerAddAssetToSceneTool } from './tools/addAssetToSceneTool.js';
import { registerUpdateGameObjectTool } from './tools/updateGameObjectTool.js';
import { registerCreatePrefabTool } from './tools/createPrefabTool.js';
import { registerSaveAsPrefabTool } from './tools/saveAsPrefabTool.js';
import { registerDeleteSceneTool } from './tools/deleteSceneTool.js';
import { registerLoadSceneTool } from './tools/loadSceneTool.js';
import { registerSaveSceneTool } from './tools/saveSceneTool.js';
import { registerGetSceneInfoTool } from './tools/getSceneInfoTool.js';
import { registerUnloadSceneTool } from './tools/unloadSceneTool.js';
import { registerRecompileScriptsTool } from './tools/recompileScriptsTool.js';
import { registerGetGameObjectTool } from './tools/getGameObjectTool.js';
import { registerTransformTools } from './tools/transformTools.js';
import { registerCreateMaterialTool, registerAssignMaterialTool, registerModifyMaterialTool, registerGetMaterialInfoTool } from './tools/materialTools.js';
import { registerDuplicateGameObjectTool, registerDeleteGameObjectTool, registerReparentGameObjectTool, registerSetSiblingIndexTool } from './tools/gameObjectTools.js';
import { registerUGUITools } from './tools/uguiTools.js';
import { registerCreateScriptableObjectTool } from './tools/createScriptableObjectTool.js';
import { registerUpdateScriptableObjectTool } from './tools/updateScriptableObjectTool.js';
import { registerImportTextureAsSpriteTool, registerCreateSpriteAtlasTool } from './tools/spriteTools.js';
import { registerOpenPrefabContentsTool, registerSavePrefabContentsTool } from './tools/prefabEditTools.js';
import { registerScreenshotTools } from './tools/screenshotTools.js';
import { registerEditorStateTools } from './tools/editorStateTools.js';
import { registerGetSelectionTool } from './tools/getSelectionTool.js';
import { registerReadSerializedFieldsTool, registerWriteSerializedFieldsTool } from './tools/serializedFieldTools.js';
import { registerUIAutomationTools } from './tools/uiAutomationTools.js';
import { registerBatchExecuteTool } from './tools/batchExecuteTool.js';
import {
  registerLocListTablesTool,
  registerLocGetEntriesTool,
  registerLocSetEntryTool,
  registerLocSetEntriesTool,
  registerLocDeleteEntryTool,
  registerLocCreateTableTool,
  registerLocAddLocaleTool,
  registerLocRemoveLocaleTool,
  registerLocDeleteTableTool,
} from './tools/localizationTools.js';
import { registerDynamicTools } from './tools/dynamicTools.js';
import { registerGetMenuItemsResource } from './resources/getMenuItemResource.js';
import { registerGetConsoleLogsResource } from './resources/getConsoleLogsResource.js';
import { registerGetHierarchyResource } from './resources/getScenesHierarchyResource.js';
import { registerGetPackagesResource } from './resources/getPackagesResource.js';
import { registerGetAssetsResource } from './resources/getAssetsResource.js';
import { registerGetTestsResource } from './resources/getTestsResource.js';
import { registerGetGameObjectResource } from './resources/getGameObjectResource.js';
import { registerGetShadersResource } from './resources/getShadersResource.js';
import { registerGameObjectHandlingPrompt } from './prompts/gameobjectHandlingPrompt.js';

// Initialize loggers
const serverLogger = new Logger('Server', LogLevel.INFO);
const unityLogger = new Logger('Unity', LogLevel.INFO);
const toolLogger = new Logger('Tools', LogLevel.INFO);
const resourceLogger = new Logger('Resources', LogLevel.INFO);

// Initialize the MCP server
const server = new McpServer (
  {
    name: "MCP Unity Server",
    version: "1.0.0"
  },
  {
    capabilities: {
      tools: {},
      resources: {},
      prompts: {},
    },
  }
);

// Initialize MCP HTTP bridge with Unity editor
const mcpUnity = new McpUnity(unityLogger);

// Register all tools into the MCP server
registerMenuItemTool(server, mcpUnity, toolLogger);
registerSelectGameObjectTool(server, mcpUnity, toolLogger);
registerAddPackageTool(server, mcpUnity, toolLogger);
registerRunTestsTool(server, mcpUnity, toolLogger);
registerSendConsoleLogTool(server, mcpUnity, toolLogger);
registerGetConsoleLogsTool(server, mcpUnity, toolLogger);
registerUpdateComponentTool(server, mcpUnity, toolLogger);
registerRemoveComponentTool(server, mcpUnity, toolLogger);
registerAddAssetToSceneTool(server, mcpUnity, toolLogger);
registerUpdateGameObjectTool(server, mcpUnity, toolLogger);
registerCreatePrefabTool(server, mcpUnity, toolLogger);
registerSaveAsPrefabTool(server, mcpUnity, toolLogger);
registerCreateSceneTool(server, mcpUnity, toolLogger);
registerDeleteSceneTool(server, mcpUnity, toolLogger);
registerLoadSceneTool(server, mcpUnity, toolLogger);
registerSaveSceneTool(server, mcpUnity, toolLogger);
registerGetSceneInfoTool(server, mcpUnity, toolLogger);
registerUnloadSceneTool(server, mcpUnity, toolLogger);
registerRecompileScriptsTool(server, mcpUnity, toolLogger);
registerGetGameObjectTool(server, mcpUnity, toolLogger);
registerTransformTools(server, mcpUnity, toolLogger);
registerDuplicateGameObjectTool(server, mcpUnity, toolLogger);
registerDeleteGameObjectTool(server, mcpUnity, toolLogger);
registerReparentGameObjectTool(server, mcpUnity, toolLogger);
registerSetSiblingIndexTool(server, mcpUnity, toolLogger);

// Register Material Tools
registerCreateMaterialTool(server, mcpUnity, toolLogger);
registerAssignMaterialTool(server, mcpUnity, toolLogger);
registerModifyMaterialTool(server, mcpUnity, toolLogger);
registerGetMaterialInfoTool(server, mcpUnity, toolLogger);

// Register UGUI Tools
registerUGUITools(server, mcpUnity, toolLogger);

// Register ScriptableObject Tools
registerCreateScriptableObjectTool(server, mcpUnity, toolLogger);
registerUpdateScriptableObjectTool(server, mcpUnity, toolLogger);

// Register Sprite Tools
registerImportTextureAsSpriteTool(server, mcpUnity, toolLogger);
registerCreateSpriteAtlasTool(server, mcpUnity, toolLogger);

// Register Prefab Edit Tools
registerOpenPrefabContentsTool(server, mcpUnity, toolLogger);
registerSavePrefabContentsTool(server, mcpUnity, toolLogger);

// Register Screenshot Tools
registerScreenshotTools(server, mcpUnity, toolLogger);

// Register Editor State Tools
registerEditorStateTools(server, mcpUnity, toolLogger);

// Register Get Selection Tool
registerGetSelectionTool(server, mcpUnity, toolLogger);

// Register Serialized Field Tools
registerReadSerializedFieldsTool(server, mcpUnity, toolLogger);
registerWriteSerializedFieldsTool(server, mcpUnity, toolLogger);

// Register UI Automation Tools
registerUIAutomationTools(server, mcpUnity, toolLogger);

// Register Localization Tools (Unity Localization package — Unity-side only compiles
// when com.unity.localization is installed; Node side always registers, calls fall
// through to "unknown method" if Localization is not present in Unity)
registerLocAddLocaleTool(server, mcpUnity, toolLogger);
registerLocRemoveLocaleTool(server, mcpUnity, toolLogger);
registerLocListTablesTool(server, mcpUnity, toolLogger);
registerLocGetEntriesTool(server, mcpUnity, toolLogger);
registerLocSetEntryTool(server, mcpUnity, toolLogger);
registerLocSetEntriesTool(server, mcpUnity, toolLogger);
registerLocDeleteEntryTool(server, mcpUnity, toolLogger);
registerLocCreateTableTool(server, mcpUnity, toolLogger);
registerLocDeleteTableTool(server, mcpUnity, toolLogger);

// Register Batch Execute Tool (high-priority for performance)
registerBatchExecuteTool(server, mcpUnity, toolLogger);

// Register all resources into the MCP server
registerGetTestsResource(server, mcpUnity, resourceLogger);
registerGetGameObjectResource(server, mcpUnity, resourceLogger);
registerGetMenuItemsResource(server, mcpUnity, resourceLogger);
registerGetConsoleLogsResource(server, mcpUnity, resourceLogger);
registerGetHierarchyResource(server, mcpUnity, resourceLogger);
registerGetPackagesResource(server, mcpUnity, resourceLogger);
registerGetAssetsResource(server, mcpUnity, resourceLogger);
registerGetShadersResource(server, mcpUnity, resourceLogger);

// Register all prompts into the MCP server
registerGameObjectHandlingPrompt(server);

// Server startup function
async function startServer() {
  try {
    // 1. Connect to Unity WebSocket FIRST (before MCP client connects)
    //    so dynamic tools are available when tools/list is queried.
    //    start() handles connection failure gracefully (warns + continues).
    await mcpUnity.start();

    // 2. Discover and register external tools from Unity (if connected)
    if (mcpUnity.isConnected) {
      try {
        const dynamicCount = await registerDynamicTools(server, mcpUnity, toolLogger);
        if (dynamicCount > 0) {
          serverLogger.info(`Registered ${dynamicCount} external tool(s) from Unity`);
        }
      } catch (error) {
        serverLogger.warn('Failed to register dynamic tools (non-fatal)', error);
      }
    } else {
      serverLogger.info('Unity not connected — dynamic tools will be registered when Unity connects');
    }

    // 3. NOW connect the MCP server to the transport
    //    At this point all built-in + dynamic tools are registered,
    //    so the first tools/list response includes everything.
    const stdioTransport = new StdioServerTransport();
    await server.connect(stdioTransport);

    serverLogger.info('MCP Server started');

    // Update Unity connection with client name (for logging/headers)
    const clientName = server.server.getClientVersion()?.name || 'Unknown MCP Client';
    serverLogger.info(`Connected MCP client: ${clientName}`);

  } catch (error) {
    serverLogger.error('Failed to start server', error);
    process.exit(1);
  }
}

// Graceful shutdown handler
let isShuttingDown = false;
async function shutdown() {
  if (isShuttingDown) return;
  isShuttingDown = true;

  try {
    serverLogger.info('Shutting down...');
    await mcpUnity.stop();
    await server.close();
  } catch (error) {
    // Ignore errors during shutdown
  }
  process.exit(0);
}

// Start the server
startServer();

// Handle shutdown signals
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
process.on('SIGHUP', shutdown);

// Handle stdin close (when MCP client disconnects)
process.stdin.on('close', shutdown);
process.stdin.on('end', shutdown);
process.stdin.on('error', shutdown);

// Handle uncaught exceptions - exit cleanly if it's just a closed pipe
process.on('uncaughtException', (error: NodeJS.ErrnoException) => {
  // EPIPE/EOF errors are expected when the MCP client disconnects
  if (error.code === 'EPIPE' || error.code === 'EOF' || error.code === 'ERR_USE_AFTER_CLOSE') {
    shutdown();
    return;
  }
  serverLogger.error('Uncaught exception', error);
  process.exit(1);
});

// Handle unhandled promise rejections
process.on('unhandledRejection', (reason) => {
  serverLogger.error('Unhandled rejection', reason);
  process.exit(1);
});
