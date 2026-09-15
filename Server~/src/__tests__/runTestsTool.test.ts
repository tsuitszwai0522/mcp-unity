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
      runId: '11111111-1111-1111-1111-111111111111',
      artifactPath: '/Project/Library/McpUnity/TestResults/11111111-1111-1111-1111-111111111111.xml',
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
      runId: '11111111-1111-1111-1111-111111111111',
      artifactPath: '/Project/Library/McpUnity/TestResults/11111111-1111-1111-1111-111111111111.xml',
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
      runId: '20202020-2020-2020-2020-202020202020',
      artifactPath: '/Project/Library/McpUnity/TestResults/20202020-2020-2020-2020-202020202020.xml',
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
      runId: '20202020-2020-2020-2020-202020202020',
      artifactPath: '/Project/Library/McpUnity/TestResults/20202020-2020-2020-2020-202020202020.xml',
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

  it('preserves poll identity without claiming the expected artifact exists', async () => {
    mockSendRequest.mockResolvedValue({
      success: false,
      error_code: 'test_run_still_running',
      message: 'Still running; use get_test_run to poll.',
      runId: '22222222-2222-2222-2222-222222222222',
      status: 'running',
      expectedArtifactPath: '/Project/Library/McpUnity/TestResults/22222222-2222-2222-2222-222222222222.xml',
      artifactExists: false,
    });

    const result = await getToolHandler()({});
    const payload = JSON.parse(result.content[1].text);

    expect(result.isError).toBe(true);
    expect(payload).toEqual({
      runId: '22222222-2222-2222-2222-222222222222',
      status: 'running',
      expectedArtifactPath: '/Project/Library/McpUnity/TestResults/22222222-2222-2222-2222-222222222222.xml',
      artifactExists: false,
      error_code: 'test_run_still_running',
    });
    expect(payload).not.toHaveProperty('artifactPath');
  });

  it('preserves activeRunId when Unity rejects a concurrent run', async () => {
    mockSendRequest.mockResolvedValue({
      success: false,
      error_code: 'test_run_in_progress',
      message: 'A test run is already in progress.',
      activeRunId: '33333333-3333-3333-3333-333333333333',
    });

    const result = await getToolHandler()({ testFilter: 'RunB' });

    expect(result.isError).toBe(true);
    expect(JSON.parse(result.content[1].text)).toMatchObject({
      activeRunId: '33333333-3333-3333-3333-333333333333',
      error_code: 'test_run_in_progress',
    });
  });

  it('forwards dirtyScenes when Unity refuses to start a run with unsaved scenes', async () => {
    const dirtyScenes = [
      { name: 'Level', path: 'Assets/Scenes/Level.unity', untitled: false },
      { name: '', path: null, untitled: true },
    ];
    mockSendRequest.mockResolvedValue({
      success: false,
      error_code: 'dirty_scenes_present',
      message: "2 loaded scene(s) have unsaved changes: 'Assets/Scenes/Level.unity', 'Untitled' (untitled).",
      dirtyScenes,
    });

    const result = await getToolHandler()({ testFilter: 'RunA' });

    expect(result.isError).toBe(true);
    expect(result.content[0].text).toContain('unsaved changes');
    expect(JSON.parse(result.content[1].text)).toEqual({
      error_code: 'dirty_scenes_present',
      dirtyScenes,
    });
  });

  it('describes the dirty-scene refusal to callers', () => {
    getToolHandler();
    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('dirty_scenes_present');
    expect(description).toContain('dirtyScenes');
  });
});
