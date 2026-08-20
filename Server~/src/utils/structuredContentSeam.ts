import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import type { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { ErrorType, McpUnityError } from './errors.js';
import { attachStructuredContent } from './toolPayload.js';

type RegistrationMethod = (...args: unknown[]) => unknown;
type ToolCallback = (...args: unknown[]) => unknown;
type SeamServer = Pick<McpServer, 'tool' | 'registerTool'>;

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

export function installStructuredContentSeam(server: SeamServer): void {
  const methods = server as unknown as Record<'tool' | 'registerTool', RegistrationMethod>;

  for (const methodName of ['tool', 'registerTool'] as const) {
    const original = methods[methodName].bind(server);
    methods[methodName] = (...args: unknown[]) => {
      const callback = args.at(-1);
      if (typeof callback !== 'function') {
        return original(...args);
      }

      const wrappedArgs = [...args];
      wrappedArgs[wrappedArgs.length - 1] = wrapCallback(callback as ToolCallback);
      return original(...wrappedArgs);
    };
  }
}
