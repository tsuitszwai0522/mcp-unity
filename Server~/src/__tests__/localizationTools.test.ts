import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { registerLocGetEntriesTool } from '../tools/localizationTools.js';
import { PAYLOAD_MAX_CHARS } from '../utils/toolPayload.js';

const mockSendRequest = jest.fn();
const mockMcpUnity = { sendRequest: mockSendRequest } as any;

const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;

const mockServerTool = jest.fn();
const mockServer = { tool: mockServerTool } as any;

function getHandler(): Function {
  registerLocGetEntriesTool(mockServer, mockMcpUnity, mockLogger);
  return mockServerTool.mock.calls[0][3] as Function;
}

describe('loc_get_entries', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('strips include_values and max_entries before forwarding to Unity', async () => {
    (mockSendRequest as any).mockResolvedValue({ success: true, entries: [] });
    const handler = getHandler();

    await handler({
      table_name: 'CB_Tooltip',
      locale: 'zh-TW',
      include_values: true,
      max_entries: 50,
    });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'loc_get_entries',
      params: {
        table_name: 'CB_Tooltip',
        locale: 'zh-TW',
      },
    });
  });

  it('returns count summary only when include_values is omitted', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: "Read 2 entries from 'CB_Tooltip' (zh-TW)",
      table: 'CB_Tooltip',
      locale: 'zh-TW',
      entries: [
        { key: 'a', value: 'Apple' },
        { key: 'b', value: 'Banana' },
      ],
    });
    const handler = getHandler();

    const result = await handler({ table_name: 'CB_Tooltip' });

    expect(result.content).toHaveLength(2);
    expect(result.content[0].text).toBe("Read 2 entries from 'CB_Tooltip' (zh-TW)");
    expect(result.content[0].text).not.toContain('Apple');
    expect(result.content[1].text).not.toContain('Apple');
    expect(JSON.parse(result.content[1].text as string)).toEqual({
      table: 'CB_Tooltip',
      locale: 'zh-TW',
      totalEntries: 2,
      valuesIncluded: false,
      truncated: false,
    });
  });

  it('reports an empty table with omitted values as not truncated', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: "Read 0 entries from 'Empty' (zh-TW)",
      table: 'Empty',
      locale: 'zh-TW',
      entries: [],
    });
    const handler = getHandler();

    const result = await handler({ table_name: 'Empty' });

    expect(result.content[0].text).toBe("Read 0 entries from 'Empty' (zh-TW)");
    expect(JSON.parse(result.content[1].text as string)).toEqual({
      table: 'Empty',
      locale: 'zh-TW',
      totalEntries: 0,
      valuesIncluded: false,
      truncated: false,
    });
  });

  it('renders key/value lines and escapes \\r\\n in values when include_values=true', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read 2 entries',
      entries: [
        { key: 'multiline', value: 'line1\nline2\rline3' },
        { key: 'plain', value: 'simple' },
      ],
    });
    const handler = getHandler();

    const result = await handler({ table_name: 'T', include_values: true });
    const text = result.content[0].text as string;

    expect(text).toContain('Read 2 entries');
    expect(text).toContain('multiline: line1\\nline2\\rline3');
    expect(text).toContain('plain: simple');
    // No raw newline inside the value (only the separator newline between lines)
    expect(text.split('\n')).toHaveLength(3); // summary + 2 entry lines
  });

  it('caps rendered entries at max_entries and emits a truncation hint', async () => {
    const entries = Array.from({ length: 10 }, (_, i) => ({ key: `k${i}`, value: `v${i}` }));
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read 10 entries',
      entries,
    });
    const handler = getHandler();

    const result = await handler({
      table_name: 'T',
      include_values: true,
      max_entries: 3,
    });
    const text = result.content[0].text as string;

    expect(text).toContain('k0: v0');
    expect(text).toContain('k1: v1');
    expect(text).toContain('k2: v2');
    expect(text).not.toContain('k3: v3');
    expect(text).toContain('truncated 7 entries');
  });

  it('uses default cap of 200 when max_entries is omitted', async () => {
    const entries = Array.from({ length: 250 }, (_, i) => ({ key: `k${i}`, value: `v${i}` }));
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read 250 entries',
      entries,
    });
    const handler = getHandler();

    const result = await handler({ table_name: 'T', include_values: true });
    const text = result.content[0].text as string;

    expect(text).toContain('k199: v199');
    expect(text).not.toContain('k200: v200');
    expect(text).toContain('truncated 50 entries');
  });

  it('clamps a negative max_entries before slicing as handler-level defence', async () => {
    const entries = Array.from({ length: 10 }, (_, i) => ({ key: `k${i}`, value: `v${i}` }));
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read 10 entries',
      entries,
    });
    const handler = getHandler();

    const result = await handler({
      table_name: 'T',
      include_values: true,
      max_entries: -2,
    });
    const text = result.content[0].text as string;
    const payload = JSON.parse(result.content[1].text as string);

    expect(text).toContain('k0: v0');
    expect(text).not.toContain('k1: v1');
    expect(text).toContain('truncated 9 entries');
    expect(payload.entries).toHaveLength(1);
    expect(payload.truncated).toBe(true);
  });

  it('returns only capped entries in payload content with total and truncation metadata', async () => {
    const entries = Array.from({ length: 10 }, (_, i) => ({ key: `k${i}`, value: `v${i}` }));
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Read 10 entries',
      entries,
      table: 'T',
      locale: 'zh-TW',
    });
    const handler = getHandler();

    const result = await handler({
      table_name: 'T',
      include_values: true,
      max_entries: 2,
    });

    expect(result.content).toHaveLength(2);
    const payload = JSON.parse(result.content[1].text as string);
    expect(payload.entries).toHaveLength(2);
    expect(payload.totalEntries).toBe(10);
    expect(payload.valuesIncluded).toBe(true);
    expect(payload.truncated).toBe(true);
    expect(payload.table).toBe('T');
  });

  it.each([
    { entryCount: 200, valueLength: 80 },
    { entryCount: 1000, valueLength: 10 },
    { entryCount: 50, valueLength: 400 },
  ])('preserves contract metadata after second-level truncation for $entryCount entries of length $valueLength', async ({ entryCount, valueLength }) => {
    const entries = Array.from({ length: entryCount }, (_, index) => ({
      key: `cb_ext_item_${String(index).padStart(3, '0')}`,
      value: String(index).padEnd(valueLength, '.').slice(0, valueLength),
    }));
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: `Read ${entryCount} entries from 'CB_Tooltip' (zh-TW)`,
      entries,
      table: 'CB_Tooltip',
      locale: 'zh-TW',
    });
    const handler = getHandler();

    const result = await handler({
      table_name: 'CB_Tooltip',
      include_values: true,
      max_entries: entryCount,
    });
    const payloadText = result.content[1].text as string;
    const payload = JSON.parse(payloadText);

    const expectedText = [
      `Read ${entryCount} entries from 'CB_Tooltip' (zh-TW)`,
      ...entries.map((entry) => `${entry.key}: ${entry.value}`),
    ].join('\n');
    expect(result.content[0].text).toBe(expectedText);
    expect(payloadText.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(payload._truncated).toBe(true);
    expect(payload).not.toHaveProperty('entries');
    expect(payload._droppedKeys).toEqual(['entries']);
    expect(payload._droppedKeysTruncated).toBe(false);
    expect(payload.table).toBe('CB_Tooltip');
    expect(payload.locale).toBe('zh-TW');
    expect(payload.totalEntries).toBe(entryCount);
    expect(payload.valuesIncluded).toBe(true);
    expect(payload.truncated).toBe(false);
  });
});
