import { describe, expect, it, jest } from '@jest/globals';
import * as z from 'zod';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { InMemoryTransport } from '@modelcontextprotocol/sdk/inMemory.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { registerWireUnityEventTool } from '../tools/wireUnityEventTool.js';
import { jsonSchemaToZodShape } from '../utils/schemaConverter.js';
import {
  getRegisteredToolNames,
  getRegisteredToolSchema,
  installStructuredContentSeam,
} from '../utils/structuredContentSeam.js';

const toolResult = (text: string) => ({
  content: [{ type: 'text' as const, text }],
});

const createServer = () => {
  const server = new McpServer(
    { name: 'strict-schema-test-server', version: '1.0.0' },
    { capabilities: { tools: {} } },
  );
  installStructuredContentSeam(server);
  return server;
};

const connectClient = async (server: McpServer) => {
  const client = new Client(
    { name: 'strict-schema-test-client', version: '1.0.0' },
    { capabilities: {} },
  );
  const [clientTransport, serverTransport] = InMemoryTransport.createLinkedPair();
  await server.connect(serverTransport);
  await client.connect(clientTransport);
  return client;
};

const registerRepresentativeTools = (server: McpServer) => {
  server.tool(
    'static_tool',
    'Static four-arity tool',
    {
      objectPath: z.string(),
      depth: z.number().int().optional(),
    },
    async () => toolResult('static ok'),
  );
  server.tool(
    'zero_param_tool',
    'Static zero-parameter tool',
    {},
    async () => toolResult('zero ok'),
  );

  const logger = {
    info: jest.fn(),
    debug: jest.fn(),
    warn: jest.fn(),
    error: jest.fn(),
  } as any;
  registerWireUnityEventTool(server, { sendRequest: jest.fn() } as any, logger);

  const dynamicShape = jsonSchemaToZodShape({
    type: 'object',
    properties: {
      count: { type: 'integer', description: 'Requested count' },
    },
    required: ['count'],
  });
  server.tool(
    'dynamic_tool',
    'Dynamic converted-schema tool',
    dynamicShape,
    async () => toolResult('dynamic ok'),
  );
};

describe('strict tool schema seam', () => {
  it('enforces unknown-key rejection through every representative registration path', async () => {
    const server = createServer();
    registerRepresentativeTools(server);
    const client = await connectClient(server);

    try {
      const listed = await client.listTools();
      expect(listed.tools.map((tool) => tool.name)).toEqual([
        'static_tool',
        'zero_param_tool',
        'wire_unity_event',
        'dynamic_tool',
      ]);
      for (const tool of listed.tools) {
        expect(tool.inputSchema.additionalProperties).toBe(false);
        expect(getRegisteredToolSchema(server, tool.name)).toBeDefined();
      }

      const invalidCalls = [
        ['static_tool', { objectPath: 'Canvas', bogus: true }],
        ['zero_param_tool', { bogus: true }],
        ['wire_unity_event', {
          componentName: 'Probe',
          eventFieldName: 'event',
          methodName: 'Receive',
          bogus: true,
        }],
        ['dynamic_tool', { count: '7', bogus: true }],
      ] as const;
      for (const [name, args] of invalidCalls) {
        const invalid = await client.callTool({ name, arguments: args }) as any;
        expect(invalid.isError).toBe(true);
        expect(invalid.content).toEqual(expect.arrayContaining([
          expect.objectContaining({
            type: 'text',
            text: expect.stringContaining('Unrecognized parameter(s): "bogus"'),
          }),
        ]));
      }

      const valid = await client.callTool({
        name: 'dynamic_tool',
        arguments: { count: '7' },
      }) as any;
      expect(valid.isError).toBeFalsy();
      expect(valid.content).toEqual([{ type: 'text', text: 'dynamic ok' }]);
    } finally {
      await client.close();
      await server.close();
    }
  });

  it('preserves every required property from converted JSON Schema in tools/list', async () => {
    const sourceSchema = {
      type: 'object',
      properties: {
        requiredInteger: { type: 'integer' },
        requiredNumber: { type: 'number' },
        requiredBoolean: { type: 'boolean' },
        requiredString: { type: 'string' },
        optionalNumber: { type: 'number' },
      },
      required: [
        'requiredInteger',
        'requiredNumber',
        'requiredBoolean',
        'requiredString',
      ],
    };
    const server = createServer();
    server.tool(
      'converted_required_tool',
      'Converted required-fields test tool',
      jsonSchemaToZodShape(sourceSchema),
      async () => toolResult('ok'),
    );
    const client = await connectClient(server);

    try {
      const listed = await client.listTools();
      const tool = listed.tools.find(({ name }) => name === 'converted_required_tool');
      expect(tool).toBeDefined();
      expect(tool!.inputSchema.required).toEqual(sourceSchema.required);
    } finally {
      await client.close();
      await server.close();
    }
  });

  it('fails loudly when a registered ZodObject has a top-level refinement', () => {
    const server = createServer();
    const refined = z.object({ value: z.string().optional() }).refine(
      ({ value }) => value === 'accepted',
      { message: 'value must be accepted' },
    );

    expect(refined.safeParse({}).success).toBe(false);
    expect(() => server.registerTool(
      'refined_tool',
      { inputSchema: refined },
      async () => toolResult('must not register'),
    )).toThrow(
      'Tool "refined_tool" uses an object-level Zod refinement that the strict schema seam cannot preserve. ' +
      'Register its raw shape and run the refined schema inside the tool callback instead.',
    );
  });

  it('fails loudly for schema-bearing server.tool overloads the seam does not support', () => {
    const server = createServer();

    expect(() => server.tool(
      'annotated_tool',
      'Five-arity annotated tool',
      { value: z.string() },
      { readOnlyHint: true },
      async () => toolResult('must not register'),
    )).toThrow(
      'Tool "annotated_tool" uses an unsupported schema-bearing server.tool overload in the strict schema seam. ' +
      'Use server.tool(name, description, zodRawShape, callback), or use ' +
      'server.registerTool(name, { description, inputSchema: zodRawShape, annotations }, callback) ' +
      'when annotations are needed.',
    );
  });

  it('fails loudly when registerTool receives plain JSON Schema instead of Zod', () => {
    const server = createServer();

    expect(() => (server.registerTool as any)(
      'json_schema_tool',
      {
        inputSchema: {
          type: 'object',
          properties: { value: { type: 'string' } },
        },
      },
      async () => toolResult('must not register'),
    )).toThrow(
      'Tool "json_schema_tool" uses an unsupported input schema in the strict schema seam. ' +
      'Use a Zod raw shape or an unrefined ZodObject; convert plain JSON Schema with ' +
      'jsonSchemaToZodShape before registration.',
    );
  });

  it('isolates registered schemas between server instances', () => {
    const server1 = createServer();
    const server2 = createServer();
    server1.tool('server1_only', 'Server 1 tool', {}, async () => toolResult('one'));
    server2.tool('server2_only', 'Server 2 tool', {}, async () => toolResult('two'));

    expect(getRegisteredToolSchema(server1, 'server1_only')).toBeDefined();
    expect(getRegisteredToolSchema(server1, 'server2_only')).toBeUndefined();
    expect(getRegisteredToolSchema(server2, 'server2_only')).toBeDefined();
    expect(getRegisteredToolSchema(server2, 'server1_only')).toBeUndefined();
    expect(getRegisteredToolNames(server1)).toEqual(['server1_only']);
    expect(getRegisteredToolNames(server2)).toEqual(['server2_only']);
  });

  it('reports offending and valid keys, with readable zero-param wording, without replacing default errors', async () => {
    const server = createServer();
    server.tool(
      'static_tool_messages',
      'Message test tool',
      { objectPath: z.string(), depth: z.number().int().optional() },
      async () => toolResult('ok'),
    );
    server.tool(
      'zero_param_messages',
      'Zero-param message test tool',
      {},
      async () => toolResult('ok'),
    );
    const client = await connectClient(server);

    try {
      const unknown = await client.callTool({
        name: 'static_tool_messages',
        arguments: { objectPath: 'Canvas', bogusOne: true, bogusTwo: 2 },
      }) as any;
      expect(unknown.isError).toBe(true);
      expect(unknown.content[0].text).toContain(
        'Unrecognized parameter(s): "bogusOne", "bogusTwo". ' +
        'Valid parameters for static_tool_messages: objectPath, depth',
      );

      const zeroParam = await client.callTool({
        name: 'zero_param_messages',
        arguments: { bogus: true },
      }) as any;
      expect(zeroParam.isError).toBe(true);
      expect(zeroParam.content[0].text).toContain(
        'Unrecognized parameter(s): "bogus". Tool zero_param_messages accepts no parameters',
      );

      const typeError = await client.callTool({
        name: 'static_tool_messages',
        arguments: { objectPath: 42 },
      }) as any;
      expect(typeError.isError).toBe(true);
      expect(typeError.content[0].text).toContain('expected string');
      expect(typeError.content[0].text).not.toContain('Unrecognized parameter(s)');

      const missingRequired = await client.callTool({
        name: 'static_tool_messages',
        arguments: {},
      }) as any;
      expect(missingRequired.isError).toBe(true);
      expect(missingRequired.content[0].text).toContain('expected string');
      expect(missingRequired.content[0].text).not.toContain('Unrecognized parameter(s)');
    } finally {
      await client.close();
      await server.close();
    }
  });

  it('rewraps wire_unity_event while preserving its nested strict locator and staticArgument union', () => {
    const server = createServer();
    registerWireUnityEventTool(
      server,
      { sendRequest: jest.fn() } as any,
      { info: jest.fn(), debug: jest.fn(), warn: jest.fn(), error: jest.fn() } as any,
    );
    const schema = getRegisteredToolSchema(server, 'wire_unity_event');
    expect(schema).toBeDefined();

    const base = {
      componentName: 'Probe',
      eventFieldName: 'event',
      methodName: 'Receive',
    };
    for (const staticArgument of [
      true,
      42,
      'text',
      null,
      { assetPath: 'Assets/Target.asset' },
    ]) {
      expect(schema!.safeParse({ ...base, staticArgument }).success).toBe(true);
    }

    const nestedUnknown = schema!.safeParse({
      ...base,
      staticArgument: { assetPath: 'Assets/Target.asset', nestedBogus: true },
    });
    expect(nestedUnknown.success).toBe(false);
    if (!nestedUnknown.success) {
      const nestedIssues = JSON.stringify(nestedUnknown.error.issues);
      expect(nestedIssues).toContain('"code":"unrecognized_keys"');
      expect(nestedIssues).toContain('"keys":["nestedBogus"]');
    }

    const topLevelUnknown = schema!.safeParse({ ...base, outerBogus: true });
    expect(topLevelUnknown.success).toBe(false);
    if (!topLevelUnknown.success) {
      expect(topLevelUnknown.error.issues[0].message).toContain(
        'Unrecognized parameter(s): "outerBogus". Valid parameters for wire_unity_event:',
      );
    }
  });
});
