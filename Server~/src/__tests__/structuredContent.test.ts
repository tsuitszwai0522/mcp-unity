import { describe, expect, it, jest } from '@jest/globals';
import { readdirSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import ts from 'typescript';
import {
  attachStructuredContent,
  PAYLOAD_MAX_CHARS,
  PAYLOAD_STRUCTURED,
  payloadContent,
  type PayloadTextContent,
} from '../utils/toolPayload.js';
import { installStructuredContentSeam } from '../utils/structuredContentSeam.js';

const structuredMarker = (item: PayloadTextContent): unknown =>
  Reflect.get(item, PAYLOAD_STRUCTURED);

const expectMarkerToMatchText = (item: PayloadTextContent): void => {
  expect(structuredMarker(item)).toEqual(JSON.parse(item.text));
};

const propertyNameText = (name: ts.PropertyName | undefined): string | undefined => {
  if (name && (ts.isIdentifier(name) || ts.isStringLiteral(name))) {
    return name.text;
  }
  return undefined;
};

const findPayloadLiteralsWithoutMessage = (source: string, filePath: string): string[] => {
  const sourceFile = ts.createSourceFile(filePath, source, ts.ScriptTarget.Latest, true);
  const failures: string[] = [];

  const visit = (node: ts.Node): void => {
    if (
      ts.isCallExpression(node) &&
      ts.isIdentifier(node.expression) &&
      node.expression.text === 'payloadContent'
    ) {
      const payload = node.arguments[0];
      if (payload && ts.isObjectLiteralExpression(payload)) {
        const hasMessage = payload.properties.some(
          (property) => propertyNameText('name' in property ? property.name : undefined) === 'message'
        );
        if (!hasMessage) {
          const line = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1;
          failures.push(`${filePath}:${line}`);
        }
      }
    }
    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return failures;
};

const findSeamAndFirstRegistration = (
  source: string,
  filePath: string
): { seamPosition: number; firstRegistrationPosition: number } => {
  const sourceFile = ts.createSourceFile(filePath, source, ts.ScriptTarget.Latest, true);
  let seamPosition = -1;
  let firstRegistrationPosition = -1;

  const visit = (node: ts.Node): void => {
    if (ts.isCallExpression(node)) {
      const expression = node.expression;
      if (ts.isIdentifier(expression) && expression.text === 'installStructuredContentSeam') {
        seamPosition = node.getStart(sourceFile);
      } else {
        const isIdentifierRegistration =
          ts.isIdentifier(expression) && expression.text.startsWith('register');
        const isServerPropertyRegistration =
          ts.isPropertyAccessExpression(expression) &&
          ts.isIdentifier(expression.expression) &&
          expression.expression.text === 'server' &&
          (expression.name.text === 'tool' || expression.name.text.startsWith('register'));

        if (isIdentifierRegistration || isServerPropertyRegistration) {
          const position = node.getStart(sourceFile);
          if (firstRegistrationPosition === -1 || position < firstRegistrationPosition) {
            firstRegistrationPosition = position;
          }
        }
      }
    }
    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return { seamPosition, firstRegistrationPosition };
};

describe('payload structured marker', () => {
  it('marks the complete pretty JSON object parsed from the text', () => {
    const item = payloadContent({ success: true, items: [{ id: 1 }] });

    expectMarkerToMatchText(item);
    expect(Object.getOwnPropertyDescriptor(item, PAYLOAD_STRUCTURED)).toMatchObject({
      enumerable: false,
    });
    expect(Object.keys(item)).toEqual(['type', 'text']);
  });

  it('marks the compact fallback object parsed from the text', () => {
    const entries = Array.from({ length: 200 }, (_, index) => ({
      key: `cb_ext_item_${String(index).padStart(3, '0')}`,
      value: `Ordinary equipment description ${index}`.padEnd(40, '.'),
    }));
    const payload = { entries, totalEntries: entries.length, truncated: false };
    const pretty = JSON.stringify(payload, null, 2);
    const compact = JSON.stringify(payload);

    expect(pretty.length).toBeGreaterThan(PAYLOAD_MAX_CHARS);
    expect(compact.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);

    const item = payloadContent(payload);

    expect(item.text).toBe(compact);
    expectMarkerToMatchText(item);
  });

  it('marks the genuinely truncated metadata object parsed from the final text', () => {
    const payload = {
      elementInfo: { children: 'x'.repeat(PAYLOAD_MAX_CHARS) },
      requestId: 42,
    };

    expect(JSON.stringify(payload).length).toBeGreaterThan(PAYLOAD_MAX_CHARS);

    const item = payloadContent(payload);

    expect(JSON.parse(item.text)).toMatchObject({ _truncated: true, requestId: 42 });
    expectMarkerToMatchText(item);
  });

  it.each([
    ['array', [{ id: 1 }]],
    ['number', 42],
    ['string', 'payload'],
    ['boolean', true],
    ['null', null],
    ['undefined', undefined],
  ])('does not mark a %s payload', (_label, payload) => {
    const item = payloadContent(payload);

    expect(Object.prototype.hasOwnProperty.call(item, PAYLOAD_STRUCTURED)).toBe(false);
    expect(structuredMarker(item)).toBeUndefined();
  });
});

describe('attachStructuredContent', () => {
  it('returns a result without content unchanged', () => {
    const result = { isError: false };

    expect(attachStructuredContent(result)).toBe(result);
  });

  it('preserves an existing structuredContent value instead of replacing it', () => {
    const existing = { source: 'existing' };
    const result = {
      content: [payloadContent({ source: 'payload' })],
      structuredContent: existing,
    };

    expect(attachStructuredContent(result)).toBe(result);
    expect(result.structuredContent).toBe(existing);
  });

  it('attaches the first marker found at content[1]', () => {
    const payload = { success: true, value: 17 };
    const result = {
      content: [
        { type: 'text', text: 'Human-readable message' },
        payloadContent(payload),
      ],
    };

    const attached = attachStructuredContent(result) as Record<string, unknown>;

    expect(attached).not.toBe(result);
    expect(attached.structuredContent).toEqual(payload);
    expect(result).not.toHaveProperty('structuredContent');
  });

  it('returns content with no marker unchanged without parsing other text items', () => {
    const result = {
      content: [{ type: 'text', text: '{"looks":"structured"}' }],
    };

    expect(attachStructuredContent(result)).toBe(result);
    expect(result).not.toHaveProperty('structuredContent');
  });

  it.each([null, undefined, 42, 'text'])('returns non-object input %p unchanged', (result) => {
    expect(attachStructuredContent(result)).toBe(result);
  });
});

describe('payload completeness guard', () => {
  it('requires message on every object-literal payloadContent call in src/tools', () => {
    const toolsPath = join(dirname(fileURLToPath(import.meta.url)), '..', 'tools');
    const failures = readdirSync(toolsPath)
      .filter((fileName) => fileName.endsWith('.ts'))
      .flatMap((fileName) => {
        const filePath = join(toolsPath, fileName);
        return findPayloadLiteralsWithoutMessage(readFileSync(filePath, 'utf8'), filePath);
      });

    expect(failures).toEqual([]);
  });

  it('rejects a counterexample with message removed while exempting non-literal payloads', () => {
    const counterexample = [
      'payloadContent({ success: true });',
      'payloadContent(result);',
    ].join('\n');
    const validFixture = 'payloadContent({ success: true, message: response.message });';

    expect(findPayloadLiteralsWithoutMessage(counterexample, 'counterexample.ts')).toEqual([
      'counterexample.ts:1',
    ]);
    expect(findPayloadLiteralsWithoutMessage(validFixture, 'valid.ts')).toEqual([]);
  });
});

describe('installStructuredContentSeam', () => {
  it('wraps tool and registerTool callbacks while preserving sync and async returns', async () => {
    const callbacks: Record<string, (...args: unknown[]) => unknown> = {};
    const fakeServer = {
      tool: jest.fn((...args: unknown[]) => {
        callbacks.tool = args.at(-1) as (...callbackArgs: unknown[]) => unknown;
        return { registered: 'tool' };
      }),
      registerTool: jest.fn((...args: unknown[]) => {
        callbacks.registerTool = args.at(-1) as (...callbackArgs: unknown[]) => unknown;
        return { registered: 'registerTool' };
      }),
    };
    installStructuredContentSeam(fakeServer as unknown as McpServer);

    fakeServer.tool('sync_tool', () => ({
      content: [
        { type: 'text', text: 'Sync result' },
        payloadContent({ channel: 'tool', mode: 'sync' }),
      ],
    }));

    const syncResult = callbacks.tool({}) as Record<string, unknown>;

    expect(syncResult).not.toBeInstanceOf(Promise);
    expect(syncResult.structuredContent).toEqual({ channel: 'tool', mode: 'sync' });

    fakeServer.registerTool('async_tool', { description: 'Async test tool' }, async () => ({
      content: [
        { type: 'text', text: 'Async result' },
        payloadContent({ channel: 'registerTool', mode: 'async' }),
      ],
    }));

    const asyncResult = await callbacks.registerTool({}) as Record<string, unknown>;

    expect(asyncResult.structuredContent).toEqual({ channel: 'registerTool', mode: 'async' });

    fakeServer.tool('plain_tool', () => ({
      content: [{ type: 'text', text: '{"not":"marked"}' }],
    }));

    const plainResult = callbacks.tool({}) as Record<string, unknown>;

    expect(plainResult).not.toHaveProperty('structuredContent');
  });
});

describe('structured content seam installation guard', () => {
  it('installs the seam before the first registration call in index.ts', () => {
    const indexPath = join(dirname(fileURLToPath(import.meta.url)), '..', 'index.ts');
    const source = readFileSync(indexPath, 'utf8');
    const { seamPosition, firstRegistrationPosition } = findSeamAndFirstRegistration(
      source,
      indexPath
    );

    expect(seamPosition).toBeGreaterThanOrEqual(0);
    expect(firstRegistrationPosition).toBeGreaterThanOrEqual(0);
    expect(seamPosition).toBeLessThan(firstRegistrationPosition);
  });

  it('counts server.tool and server.registerTool as registration calls', () => {
    const source = [
      "server.tool('early', {}, callback);",
      'installStructuredContentSeam(server);',
      "server.registerTool('late', {}, callback);",
    ].join('\n');
    const { seamPosition, firstRegistrationPosition } = findSeamAndFirstRegistration(
      source,
      'property-access.ts'
    );

    expect(firstRegistrationPosition).toBeGreaterThanOrEqual(0);
    expect(firstRegistrationPosition).toBeLessThan(seamPosition);
  });
});
