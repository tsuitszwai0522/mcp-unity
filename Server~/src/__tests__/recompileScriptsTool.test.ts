import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerRecompileScriptsTool } from '../tools/recompileScriptsTool.js';
import {
  PAYLOAD_MAX_CHARS,
  PAYLOAD_STRUCTURED,
  type PayloadTextContent,
} from '../utils/toolPayload.js';

const structuredMarker = (item: PayloadTextContent): unknown =>
  Reflect.get(item, PAYLOAD_STRUCTURED);

const expectMarkerToMatchText = (item: PayloadTextContent): void => {
  expect(structuredMarker(item)).toEqual(JSON.parse(item.text));
};

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

type ToolHandler = (params: {
  returnWithLogs?: boolean;
  logsLimit?: number;
  refreshAssets?: boolean;
}) => Promise<any>;

const getToolHandler = (): ToolHandler => {
  registerRecompileScriptsTool(mockServer, mockMcpUnity, mockLogger);
  return (mockServerTool.mock.calls[0] as any)[3] as ToolHandler;
};

describe('recompile_scripts', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('keeps response.message unchanged in content[0]', async () => {
    const response = {
      success: true,
      message: 'Successfully recompiled all scripts with 1 warning(s)',
      logs: [{ message: 'Unused variable', type: 'Warning' }],
      truncated: false,
      totalLogs: 1,
      returnedLogs: 1,
      refreshed: true,
      refreshDurationMs: 204,
      compilationWasAlreadyInProgress: false,
    };
    (mockSendRequest as any).mockResolvedValue(response);

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });

    expect(result.content[0]).toEqual({ type: 'text', text: response.message });
  });

  it('includes all recompilation metadata in content[1]', async () => {
    const response = {
      success: true,
      message: 'Recompilation completed with 1 error(s) and 0 warning(s)',
      logs: [{ message: 'Compile failed', type: 'Error' }],
      truncated: true,
      totalLogs: 3,
      returnedLogs: 1,
      refreshed: true,
      refreshDurationMs: 346,
      compilationWasAlreadyInProgress: false,
    };
    (mockSendRequest as any).mockResolvedValue(response);

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 1 });

    expect(JSON.parse(result.content[1].text as string)).toEqual({
      message: response.message,
      logs: response.logs,
      truncated: response.truncated,
      totalLogs: response.totalLogs,
      returnedLogs: response.returnedLogs,
      refreshed: response.refreshed,
      refreshDurationMs: response.refreshDurationMs,
      compilationWasAlreadyInProgress: response.compilationWasAlreadyInProgress,
    });
  });

  it('defaults refreshAssets to true and forwards it to Unity', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs: [],
      totalLogs: 0,
      refreshed: true,
      refreshDurationMs: 204,
    });

    await getToolHandler()({});

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'recompile_scripts',
      params: {
        returnWithLogs: true,
        logsLimit: 100,
        refreshAssets: true,
      },
    });
  });

  it('forwards refreshAssets false to Unity', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs: [],
      totalLogs: 0,
      refreshed: false,
      refreshDurationMs: 0,
    });

    await getToolHandler()({ refreshAssets: false });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'recompile_scripts',
      params: {
        returnWithLogs: true,
        logsLimit: 100,
        refreshAssets: false,
      },
    });
  });

  it('forwards refresh execution metadata in structured content', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs: [],
      totalLogs: 0,
      refreshed: true,
      refreshDurationMs: 1186,
      compilationWasAlreadyInProgress: true,
    });

    const result = await getToolHandler()({});
    const payload = JSON.parse(result.content[1].text as string);

    expect(payload.refreshed).toBe(true);
    expect(payload.refreshDurationMs).toBe(1186);
    expect(payload.compilationWasAlreadyInProgress).toBe(true);
  });

  it('reports missing Unity-side recompilation metadata as unknown', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled by an older Unity package',
      logs: [],
      totalLogs: 0,
    });

    const result = await getToolHandler()({});
    const payload = JSON.parse(result.content[1].text as string);

    expect(result.content[0].text).toContain('refreshed=unknown');
    expect(result.content[0].text).toContain('refreshDurationMs=unknown');
    expect(result.content[0].text).toContain(
      'compilationWasAlreadyInProgress=unknown',
    );
    expect(result.content[0].text).toContain(
      'unity-side recompilation metadata absent',
    );
    expect(payload.refreshed).toBe('unknown');
    expect(payload.refreshDurationMs).toBe('unknown');
    expect(payload.compilationWasAlreadyInProgress).toBe('unknown');
  });

  it('preserves explicit null compilation state without reporting metadata absent', async () => {
    const response = {
      success: true,
      message: 'Piggybacked compilation completed with uncertain coverage',
      logs: [],
      totalLogs: 0,
      refreshed: false,
      refreshDurationMs: 0,
      compilationWasAlreadyInProgress: null,
    };
    (mockSendRequest as any).mockResolvedValue(response);

    const result = await getToolHandler()({ refreshAssets: false });
    const payload = JSON.parse(result.content[1].text as string);

    expect(result.content[0].text).toBe(response.message);
    expect(result.content[0].text).not.toContain('metadata absent');
    expect(payload.compilationWasAlreadyInProgress).toBeNull();
  });

  it('preserves an explicit logsLimit of zero', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs: [],
      totalLogs: 0,
      refreshed: false,
      refreshDurationMs: 0,
    });

    await getToolHandler()({ logsLimit: 0 });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'recompile_scripts',
      params: {
        returnWithLogs: true,
        logsLimit: 0,
        refreshAssets: true,
      },
    });
  });

  it('marks content[1] with structured data matching its JSON text', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs: [{ message: 'Warning', type: 'Warning' }],
      truncated: false,
      totalLogs: 1,
      returnedLogs: 1,
    });

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });

    expectMarkerToMatchText(result.content[1] as PayloadTextContent);
  });

  it('keeps every log and consistent counts when the payload fits', async () => {
    const logs = [
      { message: 'First warning', type: 'Warning' },
      { message: 'Second warning', type: 'Warning' },
    ];
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      message: 'Scripts recompiled',
      logs,
      truncated: false,
      totalLogs: logs.length,
      returnedLogs: logs.length,
    });

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });
    const payload = JSON.parse(result.content[1].text as string);

    expect(payload.logs).toEqual(logs);
    expect(payload.logs.length).toBe(payload.returnedLogs);
    expect(payload.truncated).toBe(payload.returnedLogs < payload.totalLogs);
    expect(payload.totalLogs).toBe(logs.length);
  });

  it('keeps logs present and reports honest counts after fitting an oversized payload', async () => {
    const logs = Array.from({ length: 100 }, (_, index) => ({
      message: `error CS0246: Missing type ${index} ${'x'.repeat(180)}`,
      type: 'Error',
      file: `Assets/Scripts/CompilerFailure${index}.cs`,
      line: index + 1,
      column: 17,
    }));
    const response = {
      success: true,
      message: 'Recompilation completed with 100 error(s) and 0 warning(s)',
      logs,
      truncated: false,
      totalLogs: logs.length,
      returnedLogs: logs.length,
    };
    const untrimmedPayload = {
      message: response.message,
      logs,
      truncated: false,
      totalLogs: logs.length,
      returnedLogs: logs.length,
    };
    expect(JSON.stringify(untrimmedPayload).length).toBeGreaterThan(PAYLOAD_MAX_CHARS);
    (mockSendRequest as any).mockResolvedValue(response);

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });
    const payload = JSON.parse(result.content[1].text as string);

    expect(payload.logs).toBeInstanceOf(Array);
    expect(payload.logs.length).toBeGreaterThan(0);
    expect(payload.logs.length).toBeLessThan(logs.length);
    expect(payload.logs.length).toBe(payload.returnedLogs);
    expect(payload.truncated).toBe(payload.returnedLogs < payload.totalLogs);
    expect(payload.totalLogs).toBe(response.totalLogs);
    expect(payload).not.toHaveProperty('_truncated');
    expect(result.content[1].text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
  });

  it('uses the same fallback message in human and structured content', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: true,
      logs: [],
      truncated: false,
      totalLogs: 0,
      returnedLogs: 0,
      refreshed: false,
      refreshDurationMs: 0,
      compilationWasAlreadyInProgress: false,
    });

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });
    const payload = JSON.parse(result.content[1].text as string);

    expect(result.content[0].text).toBe('Scripts recompiled successfully');
    expect(payload.message).toBe(result.content[0].text);
  });

  it('returns typed refresh failures as isError with observability metadata', async () => {
    (mockSendRequest as any).mockResolvedValue({
      success: false,
      message: 'AssetDatabase.Refresh failed before script recompilation',
      error_code: 'asset_database_refresh_failed',
      logs: [],
      totalLogs: 0,
      returnedLogs: 0,
      refreshed: true,
      refreshDurationMs: 17,
      compilationWasAlreadyInProgress: false,
    });

    const result = await getToolHandler()({ returnWithLogs: true, logsLimit: 100 });
    const payload = JSON.parse(result.content[1].text as string);

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toBe(
      'AssetDatabase.Refresh failed before script recompilation',
    );
    expect(payload).toMatchObject({
      error_code: 'asset_database_refresh_failed',
      refreshed: true,
      refreshDurationMs: 17,
      compilationWasAlreadyInProgress: false,
    });
  });
});
