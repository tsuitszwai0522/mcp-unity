import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerGetTestRunTool } from '../tools/getTestRunTool.js';

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

type ToolHandler = (params?: Record<string, unknown>) => Promise<any>;

const getToolHandler = (): ToolHandler => {
  registerGetTestRunTool(mockServer, mockMcpUnity, mockLogger);
  return (mockServerTool.mock.calls[0] as any)[3] as ToolHandler;
};

describe('get_test_run result forwarding', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('omits runId on the wire so Unity returns the most recent run', async () => {
    mockSendRequest.mockResolvedValue({
      success: true,
      message: 'Still running.',
      runId: '11111111-1111-1111-1111-111111111111',
      status: 'running',
      expectedArtifactPath: '/Project/Library/McpUnity/TestResults/11111111-1111-1111-1111-111111111111.xml',
      artifactExists: false,
      filter: { testMode: 'PlayMode', testFilter: null, assemblyNames: null },
      startedAt: '2026-09-02T00:00:00.0000000Z',
    });

    const result = await getToolHandler()();

    expect(mockSendRequest).toHaveBeenCalledTimes(1);
    const wireRequest = mockSendRequest.mock.calls[0][0] as any;
    expect(wireRequest).toEqual({
      method: 'get_test_run',
      params: {},
    });
    expect(Object.prototype.hasOwnProperty.call(wireRequest.params, 'runId')).toBe(false);
    expect(result.isError).toBeUndefined();
    expect(JSON.parse(result.content[1].text)).toEqual({
      runId: '11111111-1111-1111-1111-111111111111',
      status: 'running',
      expectedArtifactPath: '/Project/Library/McpUnity/TestResults/11111111-1111-1111-1111-111111111111.xml',
      artifactExists: false,
      filter: { testMode: 'PlayMode', testFilter: null, assemblyNames: null },
      startedAt: '2026-09-02T00:00:00.0000000Z',
    });
  });

  it('forwards a completed run with the full run_tests statistics and results', async () => {
    const completed = {
      success: true,
      message: '2/2 passed',
      runId: '22222222-2222-2222-2222-222222222222',
      status: 'completed',
      artifactPath: '/Project/Library/McpUnity/TestResults/22222222-2222-2222-2222-222222222222.xml',
      filter: { testMode: 'EditMode', testFilter: 'RunA', assemblyNames: ['Tests'] },
      startedAt: '2026-09-02T00:00:00.0000000Z',
      resultState: 'Passed',
      durationSeconds: 0.5,
      testCount: 2,
      treeNodeCount: 4,
      passCount: 2,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      results: [{ fullName: 'RunA.Test1' }, { fullName: 'RunA.Test2' }],
    };
    mockSendRequest.mockResolvedValue(completed);

    const result = await getToolHandler()({ runId: completed.runId });

    expect(mockSendRequest).toHaveBeenCalledWith({
      method: 'get_test_run',
      params: { runId: completed.runId },
    });
    expect(JSON.parse(result.content[1].text)).toEqual({
      runId: completed.runId,
      status: 'completed',
      artifactPath: completed.artifactPath,
      filter: completed.filter,
      startedAt: completed.startedAt,
      resultState: 'Passed',
      durationSeconds: 0.5,
      testCount: 2,
      treeNodeCount: 4,
      passCount: 2,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      results: completed.results,
    });
  });

  it('returns typed unknown-run responses as MCP errors', async () => {
    mockSendRequest.mockResolvedValue({
      success: false,
      error_code: 'test_run_not_found',
      message: 'Run not found.',
      runId: '33333333-3333-3333-3333-333333333333',
      status: 'unknown',
    });

    const result = await getToolHandler()({
      runId: '33333333-3333-3333-3333-333333333333',
    });

    expect(result.isError).toBe(true);
    expect(result.content[0]).toEqual({ type: 'text', text: 'Run not found.' });
    expect(JSON.parse(result.content[1].text)).toEqual({
      runId: '33333333-3333-3333-3333-333333333333',
      status: 'unknown',
      error_code: 'test_run_not_found',
    });
  });
});
