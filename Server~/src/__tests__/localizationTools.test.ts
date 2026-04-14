import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import { registerLocGetEntriesTool } from '../tools/localizationTools.js';

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
      entries: [
        { key: 'a', value: 'Apple' },
        { key: 'b', value: 'Banana' },
      ],
    });
    const handler = getHandler();

    const result = await handler({ table_name: 'CB_Tooltip' });

    expect(result.content[0].text).toBe("Read 2 entries from 'CB_Tooltip' (zh-TW)");
    expect(result.content[0].text).not.toContain('Apple');
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

  it('still returns full entries array on data field regardless of cap', async () => {
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

    expect(result.data.entries).toHaveLength(10);
    expect(result.data.table).toBe('T');
  });
});
