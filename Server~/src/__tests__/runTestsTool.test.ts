import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerRunTestsTool } from '../tools/runTestsTool.js';

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
  registerRunTestsTool(mockServer, mockMcpUnity, mockLogger);
  return (mockServerTool.mock.calls[0] as any)[3] as ToolHandler;
};

describe('run_tests result forwarding', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('forwards result metadata in the uncapped JSON text payload', async () => {
    const filter = {
      testMode: 'EditMode',
      testFilter: 'McpUnity.Tests.RecompileScriptsToolTests',
      assemblyNames: null,
    };
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: '3/3 passed',
      resultState: 'Passed',
      durationSeconds: 0.42,
      testCount: 3,
      treeNodeCount: 8,
      passCount: 3,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      filter,
      results: [{ fullName: 'McpUnity.Tests.RecompileScriptsToolTests.TestA' }],
    });

    const result = await getToolHandler()({ returnOnlyFailures: false });

    expect(JSON.parse(result.content[1].text)).toEqual({
      testCount: 3,
      passCount: 3,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      results: [{ fullName: 'McpUnity.Tests.RecompileScriptsToolTests.TestA' }],
      resultState: 'Passed',
      durationSeconds: 0.42,
      treeNodeCount: 8,
      filter,
    });
  });

  it('returns isError with the complete no-tests payload instead of throwing', async () => {
    const filter = {
      testMode: 'EditMode',
      testFilter: 'NoSuchTestName_ZZZ_12345',
      assemblyNames: null,
    };
    mockSendRequest.mockResolvedValue({
      success: false,
      error_code: 'no_tests_matched',
      type: 'text',
      message: 'No tests matched.',
      resultState: 'Passed',
      durationSeconds: 0.0014,
      testCount: 0,
      treeNodeCount: 1,
      passCount: 0,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      filter,
      results: [],
    });

    const result = await getToolHandler()({
      testFilter: 'NoSuchTestName_ZZZ_12345',
    });

    expect(result.isError).toBe(true);
    expect(result.content[0]).toEqual({ type: 'text', text: 'No tests matched.' });
    expect(JSON.parse(result.content[1].text)).toEqual({
      testCount: 0,
      passCount: 0,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      results: [],
      resultState: 'Passed',
      durationSeconds: 0.0014,
      treeNodeCount: 1,
      filter,
      error_code: 'no_tests_matched',
    });
  });

  it('does not mark a successful run as an MCP error', async () => {
    mockSendRequest.mockResolvedValue({
      success: true,
      message: '1/1 passed',
      testCount: 1,
      passCount: 1,
      failCount: 0,
      skipCount: 0,
      inconclusiveCount: 0,
      results: [],
    });

    const result = await getToolHandler()({});

    expect(result.isError).toBeUndefined();
  });
});
