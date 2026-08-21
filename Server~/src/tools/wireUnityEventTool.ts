import * as z from 'zod';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { ErrorType, McpUnityError } from '../utils/errors.js';
import { payloadContent } from '../utils/toolPayload.js';

const toolName = 'wire_unity_event';

const toolDescription = `Adds a validated persistent listener to a serialized UnityEvent field.
Provide the source GameObject/component, eventFieldName, listener GameObject/component, methodName, and staticArgument only when the listener consumes a static value.
PersistentListenerMode/m_Mode is not an input: it is derived from the UnityEvent generic signature and the listener method signature.
Unknown top-level keys are rejected instead of being ignored.
Missing or signature-incompatible methods fail. Ambiguous dynamic/static or numeric overloads fail instead of being selected automatically.
The result includes the inferred mode name and underlying value plus the persistent call read back from Unity serialization.`;

function createStaticObjectLocatorSchema() {
  return z.object({
    assetPath: z.string().optional().describe('Asset path for a static UnityEngine.Object argument.'),
    instanceId: z.number().int().optional().describe('GameObject instance ID for a static UnityEngine.Object argument.'),
    objectPath: z.string().optional().describe('GameObject hierarchy path for a static UnityEngine.Object argument. Same-name hierarchy segments return object_path_ambiguity_error; use canonical Name[n] syntax (0-based among same-name siblings or loaded roots).'),
    componentName: z.string().optional().describe('Optional component on the located GameObject. Not valid with assetPath.'),
  }).strict().describe('Structured UnityEngine.Object locator. Supply exactly one of assetPath, instanceId, or objectPath.');
}

const paramsSchema = z.object({
  instanceId: z.number().int().optional().describe('Source GameObject instance ID. Supply exactly one of instanceId or objectPath.'),
  objectPath: z.string().optional().describe('Source GameObject hierarchy path. Supply exactly one of instanceId or objectPath. Same-name hierarchy segments return object_path_ambiguity_error; use canonical Name[n] syntax (0-based among same-name siblings or loaded roots).'),
  componentName: z.string().describe('Source component containing the UnityEvent. Ambiguous names require a fully-qualified type name.'),
  eventFieldName: z.string().describe('Serialized UnityEvent field name, for example m_OnClick (property-style onClick is also accepted).'),
  listenerInstanceId: z.number().int().optional().describe('Listener GameObject instance ID. Supply exactly one of listenerInstanceId or listenerObjectPath.'),
  listenerObjectPath: z.string().optional().describe('Listener GameObject hierarchy path. Supply exactly one of listenerInstanceId or listenerObjectPath. Same-name hierarchy segments return object_path_ambiguity_error; use canonical Name[n] syntax (0-based among same-name siblings or loaded roots).'),
  listenerComponentName: z.string().optional().describe('Component that owns methodName. Omit to target the listener GameObject itself.'),
  methodName: z.string().describe('Listener method name. The method must exist with a signature compatible with the event or staticArgument.'),
  staticArgument: z.union([
    z.boolean(),
    z.number(),
    z.string(),
    z.null(),
    createStaticObjectLocatorSchema(),
  ]).optional().describe('Optional static listener argument. Omit for dynamic-event or zero-argument listeners. Its value and the method signature determine the mode.'),
}).strict();

export function registerWireUnityEventTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger,
) {
  logger.info(`Registering tool: ${toolName}`);
  server.registerTool(
    toolName,
    {
      description: toolDescription,
      inputSchema: paramsSchema,
    },
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${toolName}`, params);
        const result = await handler(mcpUnity, params);
        logger.info(`Tool execution successful: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    },
  );
}

function validateExclusiveLocator(
  params: any,
  idName: string,
  pathName: string,
  role: string,
) {
  const hasId = params[idName] !== undefined && params[idName] !== null;
  const hasPath = typeof params[pathName] === 'string' && params[pathName].trim() !== '';
  if (hasId === hasPath) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      `${role} requires exactly one of '${idName}' or '${pathName}'`,
    );
  }
}

async function handler(mcpUnity: McpUnity, params: any): Promise<CallToolResult> {
  validateExclusiveLocator(params, 'instanceId', 'objectPath', 'Source');
  validateExclusiveLocator(
    params,
    'listenerInstanceId',
    'listenerObjectPath',
    'Listener',
  );

  for (const required of ['componentName', 'eventFieldName', 'methodName']) {
    if (typeof params[required] !== 'string' || params[required].trim() === '') {
      throw new McpUnityError(
        ErrorType.VALIDATION,
        `'${required}' must be provided`,
      );
    }
  }

  const requestParams: Record<string, unknown> = {
    instanceId: params.instanceId,
    objectPath: params.objectPath,
    componentName: params.componentName,
    eventFieldName: params.eventFieldName,
    listenerInstanceId: params.listenerInstanceId,
    listenerObjectPath: params.listenerObjectPath,
    listenerComponentName: params.listenerComponentName,
    methodName: params.methodName,
  };
  if (Object.prototype.hasOwnProperty.call(params, 'staticArgument')) {
    requestParams.staticArgument = params.staticArgument;
  }

  const response = await mcpUnity.sendRequest({
    method: toolName,
    params: requestParams,
  });
  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || response.error?.message || 'Failed to wire UnityEvent listener',
      response.error,
    );
  }

  const modeName = response.mode?.name ?? 'Unknown';
  const modeValue = response.mode?.value ?? 'unknown';
  let text = `${response.message}\nInferred mode: ${modeName} (${modeValue})`;
  if (Array.isArray(response.warnings) && response.warnings.length > 0) {
    text += '\n\nWarnings:\n' + response.warnings
      .map((warning: string) => `  - ${warning}`)
      .join('\n');
  }

  return {
    content: [
      { type: 'text', text },
      payloadContent({
        instanceId: response.instanceId,
        componentName: response.componentName,
        eventFieldName: response.eventFieldName,
        listenerIndex: response.listenerIndex,
        listenerTarget: response.listenerTarget,
        methodName: response.methodName,
        mode: response.mode,
        callState: response.callState,
        staticArgument: response.staticArgument,
        persistentCall: response.persistentCall,
        warnings: response.warnings,
        message: response.message,
      }),
    ],
  };
}
