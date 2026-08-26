import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import * as z from 'zod';
import { ErrorType, McpUnityError } from './errors.js';
import { attachStructuredContent } from './toolPayload.js';

type RegistrationMethod = (...args: unknown[]) => unknown;
type ToolCallback = (...args: unknown[]) => unknown;
type SeamServer = Pick<McpServer, 'tool' | 'registerTool'>;
type RegisteredToolSchema = z.ZodType;

const registeredToolSchemas = new WeakMap<SeamServer, Map<string, RegisteredToolSchema>>();

const isPromiseLike = (value: unknown): value is PromiseLike<unknown> =>
  value !== null &&
  (typeof value === 'object' || typeof value === 'function') &&
  typeof (value as { then?: unknown }).then === 'function';

const toClientVisibleToolError = (error: unknown): CallToolResult => {
  const serializedError: Record<string, unknown> = error instanceof McpUnityError
    ? {
        type: error.type,
        message: error.message,
        ...(error.details === undefined ? {} : { details: error.details }),
      }
    : {
        type: ErrorType.INTERNAL,
        message: error instanceof Error ? error.message : String(error),
      };

  return {
    content: [{ type: 'text', text: serializedError.message as string }],
    structuredContent: { error: serializedError },
    isError: true,
  };
};

const wrapCallback = (callback: ToolCallback): ToolCallback =>
  function (this: unknown, ...args: unknown[]) {
    try {
      const result = callback.apply(this, args);
      return isPromiseLike(result)
        ? result.then(attachStructuredContent, toClientVisibleToolError)
        : attachStructuredContent(result);
    } catch (error) {
      return toClientVisibleToolError(error);
    }
  };

const isZodSchema = (value: unknown): value is z.ZodType =>
  value !== null &&
  typeof value === 'object' &&
  ('_zod' in value || '_def' in value || typeof (value as { safeParse?: unknown }).safeParse === 'function');

const isRawShape = (value: unknown): value is z.ZodRawShape => {
  if (value === null || typeof value !== 'object' || isZodSchema(value)) {
    return false;
  }

  const prototype = Object.getPrototypeOf(value);
  return (prototype === Object.prototype || prototype === null) &&
    Object.values(value).every(isZodSchema);
};

const unrecognizedParameterMessage = (
  toolName: string,
  validParameters: string[],
  keys: string[],
): string => {
  const invalidList = keys.map((key) => JSON.stringify(key)).join(', ');
  const validList = validParameters.length > 0
    ? `Valid parameters for ${toolName}: ${validParameters.join(', ')}`
    : `Tool ${toolName} accepts no parameters`;
  return `Unrecognized parameter(s): ${invalidList}. ${validList}`;
};

const strictObjectFromShape = (
  toolName: string,
  shape: z.ZodRawShape,
): z.ZodObject<z.ZodRawShape> => {
  const validParameters = Object.keys(shape);
  return z.strictObject(shape, {
    error: (issue) => issue.code === 'unrecognized_keys'
      ? unrecognizedParameterMessage(toolName, validParameters, issue.keys)
      : undefined,
  });
};

const hasObjectLevelChecks = (schema: z.ZodObject): boolean => {
  const definition = (schema as unknown as {
    _zod?: { def?: { checks?: unknown[] } };
    _def?: { checks?: unknown[] };
  })._zod?.def ?? schema._def;
  return Array.isArray(definition.checks) && definition.checks.length > 0;
};

const strictifyInputSchema = (toolName: string, inputSchema: unknown): RegisteredToolSchema => {
  if (isRawShape(inputSchema)) {
    return strictObjectFromShape(toolName, inputSchema);
  }

  if (inputSchema instanceof z.ZodObject) {
    if (hasObjectLevelChecks(inputSchema)) {
      throw new Error(
        `Tool "${toolName}" uses an object-level Zod refinement that the strict schema seam cannot preserve. ` +
        'Register its raw shape and run the refined schema inside the tool callback instead.',
      );
    }
    const strictSchema = strictObjectFromShape(toolName, inputSchema.shape);
    return inputSchema.description
      ? strictSchema.describe(inputSchema.description)
      : strictSchema;
  }

  throw new Error(
    `Tool "${toolName}" uses an unsupported input schema in the strict schema seam. ` +
    'Use a Zod raw shape or an unrefined ZodObject; convert plain JSON Schema with ' +
    'jsonSchemaToZodShape before registration.',
  );
};

const unsupportedToolRegistration = (toolName: unknown): Error => new Error(
  `Tool "${String(toolName)}" uses an unsupported schema-bearing server.tool overload in the strict schema seam. ` +
  'Use server.tool(name, description, zodRawShape, callback), or use ' +
  'server.registerTool(name, { description, inputSchema: zodRawShape, annotations }, callback) ' +
  'when annotations are needed.',
);

export function getRegisteredToolSchema(
  server: SeamServer,
  toolName: string,
): RegisteredToolSchema | undefined {
  return registeredToolSchemas.get(server)?.get(toolName);
}

export function getRegisteredToolNames(server: SeamServer): string[] {
  return [...(registeredToolSchemas.get(server)?.keys() ?? [])];
}

export function installStructuredContentSeam(server: SeamServer): void {
  const toolSchemas = new Map<string, RegisteredToolSchema>();
  registeredToolSchemas.set(server, toolSchemas);
  const methods = server as unknown as Record<'tool' | 'registerTool', RegistrationMethod>;
  const originalTool = methods.tool.bind(server);
  const originalRegisterTool = methods.registerTool.bind(server);

  methods.tool = (...args: unknown[]) => {
    const callback = args.at(-1);
    if (typeof callback !== 'function') {
      return originalTool(...args);
    }

    if (
      args.length === 4 &&
      typeof args[0] === 'string' &&
      typeof args[1] === 'string' &&
      isRawShape(args[2])
    ) {
      const [toolName, description, rawShape] = args as [string, string, z.ZodRawShape, ToolCallback];
      const inputSchema = strictifyInputSchema(toolName, rawShape);
      const registration = originalRegisterTool(
        toolName,
        { description, inputSchema },
        wrapCallback(callback as ToolCallback),
      );
      toolSchemas.set(toolName, inputSchema);
      return registration;
    }

    if (args.slice(1, -1).some((argument) => isRawShape(argument) || isZodSchema(argument))) {
      throw unsupportedToolRegistration(args[0]);
    }

    // These overloads contain no input schema (for example tool(name, callback)),
    // so there is nothing for the strict-schema seam to transform or register.
    const wrappedArgs = [...args];
    wrappedArgs[wrappedArgs.length - 1] = wrapCallback(callback as ToolCallback);
    return originalTool(...wrappedArgs);
  };

  methods.registerTool = (...args: unknown[]) => {
    const callback = args.at(-1);
    if (typeof callback !== 'function') {
      return originalRegisterTool(...args);
    }

    if (
      args.length === 3 &&
      typeof args[0] === 'string' &&
      args[1] !== null &&
      typeof args[1] === 'object'
    ) {
      const [toolName, rawConfig] = args as [string, Record<string, unknown>, ToolCallback];
      if (rawConfig.inputSchema !== undefined) {
        const inputSchema = strictifyInputSchema(toolName, rawConfig.inputSchema);
        const config = { ...rawConfig, inputSchema };
        const registration = originalRegisterTool(
          toolName,
          config,
          wrapCallback(callback as ToolCallback),
        );
        toolSchemas.set(toolName, inputSchema);
        return registration;
      }
    }

    // registerTool without inputSchema is intentionally passed through: it exposes
    // no parameter schema that can be made strict or recorded for batch validation.
    const wrappedArgs = [...args];
    wrappedArgs[wrappedArgs.length - 1] = wrapCallback(callback as ToolCallback);
    return originalRegisterTool(...wrappedArgs);
  };
}
