import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerReadSerializedFieldsTool } from '../tools/serializedFieldTools.js';

const sendRequest = jest.fn();
const mockMcpUnity = { sendRequest } as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;
const tool = jest.fn();
const mockServer = { tool } as any;

function registration() {
  registerReadSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);
  return tool.mock.calls[0] as any;
}

describe('read_serialized_fields recursive depth contract', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('exposes narrowing-only depth and array-width schemas with honest truncation guidance', () => {
    const [, description, schema] = registration();

    expect(description).toContain('maxDepth defaults to 8');
    expect(description).toContain('can only be lowered (0-8)');
    expect(description).toContain('20,000-character transport cap');
    expect(description).toContain('_truncated marker');
    expect(description).toContain('one global returned-element budget');
    expect(description).toContain('scalar message summarizes truncation');
    expect(schema.maxDepth.safeParse(-1).success).toBe(false);
    expect(schema.maxDepth.safeParse(0).success).toBe(true);
    expect(schema.maxDepth.safeParse(8).success).toBe(true);
    expect(schema.maxDepth.safeParse(9).success).toBe(false);
    expect(schema.maxElements.safeParse(-1).success).toBe(false);
    expect(schema.maxElements.safeParse(0).success).toBe(true);
    expect(schema.maxElements.safeParse(100).success).toBe(true);
    expect(schema.maxElements.safeParse(101).success).toBe(false);
  });

  it('forwards defaults and narrowed bounds while preserving array truncation metadata', async () => {
    sendRequest.mockResolvedValue({
      success: true,
      message: 'Read fields',
      instanceId: 1,
      componentName: 'Probe',
      fields: { payloads: [{ label: 'first' }] },
      maxDepth: 8,
      maxElements: 1,
      arrayMetadata: {
        payloads: { total: 3, returned: 1, truncated: true },
      },
    } as never);
    const handler = registration()[3];

    await handler({
      instanceId: 1,
      componentName: 'Probe',
    });
    expect((sendRequest.mock.calls[0] as any)[0].params.maxDepth).toBe(8);
    expect((sendRequest.mock.calls[0] as any)[0].params.maxElements).toBe(100);

    const result = await handler({
      instanceId: 1,
      componentName: 'Probe',
      maxDepth: 3,
      maxElements: 1,
    });
    expect((sendRequest.mock.calls[1] as any)[0].params.maxDepth).toBe(3);
    expect((sendRequest.mock.calls[1] as any)[0].params.maxElements).toBe(1);
    const payload = JSON.parse(result.content[1].text);
    expect(payload.arrayMetadata.payloads).toEqual({
      total: 3,
      returned: 1,
      truncated: true,
    });
  });

  it('keeps the scalar truncation summary when payloadContent drops oversized object metadata', async () => {
    const summary = 'Array traversal returned 100 of 500 visited element slots; truncated arrays=1.';
    sendRequest.mockResolvedValue({
      success: true,
      message: summary,
      instanceId: 1,
      componentName: 'Probe',
      fields: {
        payloads: Array.from({ length: 500 }, (_, index) => ({
          label: `item-${index}-${'x'.repeat(80)}`,
        })),
      },
      maxDepth: 8,
      maxElements: 100,
      arrayMetadata: {
        payloads: {
          total: 500,
          returned: 100,
          truncated: true,
          depthTruncated: false,
          budgetTruncated: true,
        },
      },
    } as never);
    const handler = registration()[3];

    const result = await handler({
      instanceId: 1,
      componentName: 'Probe',
    });
    const payload = JSON.parse(result.content[1].text);

    expect(payload._truncated).toBe(true);
    expect(payload.message).toBe(summary);
    expect(payload._droppedKeys).toEqual(expect.arrayContaining(['fields', 'arrayMetadata']));
  });
});
