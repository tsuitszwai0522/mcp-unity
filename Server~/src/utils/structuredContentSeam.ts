import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { attachStructuredContent } from './toolPayload.js';

type RegistrationMethod = (...args: unknown[]) => unknown;
type ToolCallback = (...args: unknown[]) => unknown;
type SeamServer = Pick<McpServer, 'tool' | 'registerTool'>;

const isPromiseLike = (value: unknown): value is PromiseLike<unknown> =>
  value !== null &&
  (typeof value === 'object' || typeof value === 'function') &&
  typeof (value as { then?: unknown }).then === 'function';

const wrapCallback = (callback: ToolCallback): ToolCallback =>
  function (this: unknown, ...args: unknown[]) {
    const result = callback.apply(this, args);
    return isPromiseLike(result)
      ? result.then(attachStructuredContent)
      : attachStructuredContent(result);
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
