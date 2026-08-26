import { jest, describe, it, expect, beforeEach } from '@jest/globals';
import * as z from 'zod';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { registerBatchExecuteTool } from '../tools/batchExecuteTool.js';
import { registerRunTestsTool } from '../tools/runTestsTool.js';
import { installStructuredContentSeam } from '../utils/structuredContentSeam.js';

// Mock the McpUnity class
const mockSendRequest = jest.fn();
const mockMcpUnity = {
  sendRequest: mockSendRequest
};

// Mock the Logger
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn()
};

// Mock the McpServer
const mockServerTool = jest.fn();
const mockServer = {
  tool: mockServerTool
};

type ToolHandler = (params: any) => Promise<any>;

const createHandlerServer = (registerAdditionalTools?: (server: any) => void) => {
  const handlers = new Map<string, ToolHandler>();
  const handlerServer = {
    tool: jest.fn((name: string, ...args: unknown[]) => {
      handlers.set(name, args.at(-1) as ToolHandler);
    }),
    registerTool: jest.fn((name: string, ...args: unknown[]) => {
      handlers.set(name, args.at(-1) as ToolHandler);
    }),
  } as any;
  installStructuredContentSeam(handlerServer);
  registerRunTestsTool(handlerServer, mockMcpUnity as any, mockLogger as any);
  registerAdditionalTools?.(handlerServer);
  registerBatchExecuteTool(handlerServer, mockMcpUnity as any, mockLogger as any);
  return { handlerServer, handlers };
};

describe('Batch Execute Tool', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  describe('registerBatchExecuteTool', () => {
    it('should register the batch_execute tool with the server', () => {
      registerBatchExecuteTool(mockServer as any, mockMcpUnity as any, mockLogger as any);

      expect(mockServerTool).toHaveBeenCalledTimes(1);
      expect(mockServerTool).toHaveBeenCalledWith(
        'batch_execute',
        expect.any(String),
        expect.any(Object),
        expect.any(Function)
      );
      expect(mockLogger.info).toHaveBeenCalledWith('Registering tool: batch_execute');
    });

    it('should have correct tool description mentioning batch and performance', () => {
      registerBatchExecuteTool(mockServer as any, mockMcpUnity as any, mockLogger as any);

      const [, description] = mockServerTool.mock.calls[0];
      expect(description).toContain('batch');
      expect(description).toContain('operations');
      expect(description).toContain('atomic=true is rejected');
      expect(description).toContain('active Prefab contents session');
      expect(description).toContain('cannot include open_prefab_contents');
      expect(description).toContain('before any operation executes');
      expect(description).toContain('prevents the entire batch from executing');
    });

    it('should have correct schema with operations array', () => {
      registerBatchExecuteTool(mockServer as any, mockMcpUnity as any, mockLogger as any);

      const [, , schema] = mockServerTool.mock.calls[0];
      expect(schema).toHaveProperty('operations');
      expect(schema).toHaveProperty('stopOnError');
      expect(schema).toHaveProperty('atomic');
    });
  });

  describe('batch_execute handler', () => {
    let toolHandler: (params: any) => Promise<any>;

    beforeEach(() => {
      const { handlers } = createHandlerServer();
      toolHandler = handlers.get('batch_execute')!;
    });

    it('should send batch request to Unity with correct parameters', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 2/2 operations.',
        results: [
          { index: 0, id: '0', success: true },
          { index: 1, id: '1', success: true }
        ],
        summary: { total: 2, succeeded: 2, failed: 0, executed: 2 }
      });

      const params = {
        operations: [
          { tool: 'run_tests', params: { testFilter: 'Test1' } },
          { tool: 'run_tests', params: { testFilter: 'Test2' } }
        ],
        stopOnError: true,
        atomic: false
      };

      const result = await toolHandler(params);

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: expect.objectContaining({
          operations: expect.arrayContaining([
            expect.objectContaining({ tool: 'run_tests' }),
            expect.objectContaining({ tool: 'run_tests' })
          ]),
          stopOnError: true,
          atomic: false
        })
      });
      expect(result.content[0].text).toContain('Successfully');
      expect(JSON.parse(result.content[1].text).message).toBe(
        'Successfully executed 2/2 operations.'
      );
      expect(result.isError).toBeUndefined();
    });

    it('validates with the real run_tests schema without injecting its defaults into Unity params', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 1/1 operations.',
        results: [{ index: 0, id: 'run', success: true }],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      });
      const callerParams = {};

      await toolHandler({
        operations: [{ tool: 'run_tests', id: 'run', params: callerParams }]
      });

      const request = mockSendRequest.mock.calls[0][0] as any;
      expect(request.params.operations).toEqual([
        { tool: 'run_tests', id: 'run', params: callerParams }
      ]);
      expect(Object.keys(request.params.operations[0].params)).toEqual(Object.keys(callerParams));
      expect(request.params.operations[0].params).not.toHaveProperty('returnOnlyFailures');
    });

    it('forwards caller-provided keys with dynamic-schema preprocess coercion', async () => {
      const { handlers } = createHandlerServer((server) => {
        server.tool(
          'test_echo',
          'Echoes a coerced integer',
          {
            n: z.preprocess(
              (value) => typeof value === 'string' ? Number(value) : value,
              z.number().int(),
            ),
          },
          async () => ({ content: [{ type: 'text', text: 'unused' }] }),
        );
      });
      const dynamicBatchHandler = handlers.get('batch_execute')!;
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 1/1 operations.',
        results: [{ index: 0, id: 'echo', success: true }],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      });

      await dynamicBatchHandler({
        operations: [{ tool: 'test_echo', id: 'echo', params: { n: '7' } }]
      });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: {
          operations: [{ tool: 'test_echo', id: 'echo', params: { n: 7 } }],
          stopOnError: true,
          atomic: false
        }
      });
      const forwardedNumber = (mockSendRequest.mock.calls[0][0] as any)
        .params.operations[0].params.n;
      expect(typeof forwardedNumber).toBe('number');
    });

    it('should return a client-visible validation error when operations array is empty', async () => {
      const params = {
        operations: [],
        stopOnError: true
      };

      const result = await toolHandler(params);
      expect(result.isError).toBe(true);
      expect(result.structuredContent.error).toMatchObject({
        type: ErrorType.VALIDATION,
        message: "The 'operations' array is required and must contain at least one operation"
      });
      expect(result.content).toEqual([{ type: 'text', text: result.structuredContent.error.message }]);
    });

    it('should return a client-visible error when nested batch_execute is detected', async () => {
      const params = {
        operations: [
          { tool: 'batch_execute', params: { operations: [] } }
        ]
      };

      const result = await toolHandler(params);
      expect(result.isError).toBe(true);
      expect(result.structuredContent.error).toMatchObject({
        type: ErrorType.VALIDATION,
        message: expect.stringContaining('Cannot nest batch_execute')
      });
      expect(result.content[0].text).toContain('Batch operation 0 (id "0", tool "batch_execute")');
    });

    it('fails fast on known invalid inner params with operation context and valid parameter names', async () => {
      const params = {
        operations: [
          {
            tool: 'run_tests',
            id: 'invalid-tests',
            params: { bogusOne: true, bogusTwo: 2 }
          },
          { tool: 'not_in_registry_but_valid_in_unity', id: 'later', params: {} }
        ],
        stopOnError: true
      };

      const result = await toolHandler(params);
      expect(result.isError).toBe(true);
      expect(result.structuredContent.error.message).toBe(
        'Batch operation 0 (id "invalid-tests", tool "run_tests") has invalid params: ' +
        'Unrecognized parameter(s): "bogusOne", "bogusTwo". ' +
        'Valid parameters for run_tests: testMode, testFilter, assemblyNames, returnOnlyFailures, returnWithLogs'
      );
      expect(result.content).toEqual([{
        type: 'text',
        text: result.structuredContent.error.message
      }]);
      expect(mockSendRequest).not.toHaveBeenCalled();
    });

    it('forwards tools absent from the Node registry for authoritative Unity lookup', async () => {
      mockSendRequest.mockResolvedValue({
        success: false,
        type: 'text',
        message: 'Batch execution stopped on error. 0/1 operations succeeded.',
        results: [{ index: 0, id: 'missing-tool', success: false, error: 'Unknown tool' }],
        summary: { total: 1, succeeded: 0, failed: 1, executed: 1 }
      });
      const params = {
        operations: [{ tool: 'not_a_registered_tool', id: 'missing-tool', params: {} }]
      };

      const result = await toolHandler(params);

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: {
          operations: [{ tool: 'not_a_registered_tool', id: 'missing-tool', params: {} }],
          stopOnError: true,
          atomic: false
        }
      });
      expect(result.isError).toBe(true);
      expect(result.content[0].text).toContain('Warnings:');
      expect(result.content[0].text).toContain(
        'was not validated by Node because its schema is not registered'
      );
      expect(JSON.parse(result.content[1].text).warnings[0]).toContain(
        'was not validated by Node because its schema is not registered'
      );
    });

    it('continues valid operations after Node validation failures when stopOnError=false', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 1/1 operations.',
        results: [{ index: 0, id: 'valid-tests', success: true }],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      });

      const result = await toolHandler({
        operations: [
          { tool: 'run_tests', id: 'invalid-tests', params: { bogus: true } },
          { tool: 'run_tests', id: 'valid-tests', params: { returnOnlyFailures: false } }
        ],
        stopOnError: false
      });

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: {
          operations: [{
            tool: 'run_tests',
            id: 'valid-tests',
            params: { returnOnlyFailures: false }
          }],
          stopOnError: false,
          atomic: false
        }
      });
      const payload = JSON.parse(result.content[1].text);
      expect(result.isError).toBe(true);
      expect(payload.summary).toEqual({ total: 2, succeeded: 1, failed: 1, executed: 1 });
      expect(payload.results).toEqual([
        expect.objectContaining({
          id: 'invalid-tests',
          status: 'Error',
          errorCode: 'NODE_SCHEMA_VALIDATION',
          error: expect.stringContaining('Batch operation 0')
        }),
        expect.objectContaining({ id: 'valid-tests', status: 'OK' })
      ]);
    });

    it('reports zero executed operations when Node rejects the entire batch', async () => {
      const result = await toolHandler({
        operations: [
          { tool: 'run_tests', id: 'invalid-0', params: { bogus: true } },
          { tool: 'run_tests', id: 'invalid-1', params: { nope: true } }
        ],
        stopOnError: false
      });

      expect(mockSendRequest).not.toHaveBeenCalled();
      expect(JSON.parse(result.content[1].text).summary).toEqual({
        total: 2,
        succeeded: 0,
        failed: 2,
        executed: 0
      });
    });

    it('fails loudly when a non-stopping Unity response omits a forwarded result', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Unexpectedly incomplete response',
        results: [{ index: 0, id: 'first', success: true }],
        summary: { total: 2, succeeded: 1, failed: 0, executed: 1 }
      });

      const result = await toolHandler({
        operations: [
          { tool: 'run_tests', id: 'first', params: {} },
          { tool: 'run_tests', id: 'missing', params: {} }
        ],
        stopOnError: false
      });

      expect(result.isError).toBe(true);
      expect(result.content[0].text).toBe(
        'Unity batch response violated result coverage for stopOnError=false: ' +
        'expected 2 results but received 1; missing original operation indexes: 1.'
      );
      expect(result.structuredContent.error).toMatchObject({
        type: ErrorType.TOOL_EXECUTION,
        details: {
          expectedResultCount: 2,
          actualResultCount: 1,
          missingOriginalOperationIndexes: [1],
          receivedUnityResultIndexes: [0]
        }
      });
    });

    it('preserves Node rejections and warnings when the Unity request rejects', async () => {
      mockSendRequest.mockRejectedValueOnce(new McpUnityError(
        ErrorType.TIMEOUT,
        'Request timed out',
        { requestId: 'batch-timeout' }
      ));

      const result = await toolHandler({
        operations: [
          { tool: 'run_tests', id: 'invalid-tests', params: { bogus: true } },
          { tool: 'unity_only_tool', id: 'unity-only', params: {} }
        ],
        stopOnError: false
      });

      expect(result.isError).toBe(true);
      expect(result.content[0].text).toContain('Request timed out');
      expect(result.content[0].text).toContain('Node-side rejections:');
      expect(result.content[0].text).toContain('Batch operation 0 (id "invalid-tests"');
      expect(result.content[0].text).toContain('Warnings:');
      expect(result.content[0].text).toContain('Batch operation 1 (id "unity-only"');
      expect(result.structuredContent.error).toMatchObject({
        type: ErrorType.TIMEOUT,
        details: {
          requestId: 'batch-timeout',
          locallyRejectedOperations: [{
            index: 0,
            id: 'invalid-tests',
            success: false,
            errorCode: 'NODE_SCHEMA_VALIDATION'
          }],
          validationWarnings: [expect.stringContaining('Batch operation 1')]
        }
      });
    });

    it('forwards atomic prefab-opening batches and preserves Unity validation errors', async () => {
      mockSendRequest.mockRejectedValueOnce(new McpUnityError(
        ErrorType.TOOL_EXECUTION,
        'atomic=true cannot include open_prefab_contents',
        { unityErrorType: 'validation_error' }
      ));
      const params = {
        operations: [{
          tool: 'open_prefab_contents',
          params: { prefabPath: 'Assets/Prefabs/Card.prefab' }
        }],
        stopOnError: true,
        atomic: true
      };

      const result = await toolHandler(params);
      const warning =
        'Batch operation 0 (id "0", tool "open_prefab_contents") was not validated by Node ' +
        'because its schema is not registered; Unity will perform authoritative tool lookup and validation.';
      const expectedMessage =
        `atomic=true cannot include open_prefab_contents\n\nWarnings:\n  - ${warning}`;
      expect(result.isError).toBe(true);
      expect(result.structuredContent.error).toEqual({
        type: ErrorType.TOOL_EXECUTION,
        message: expectedMessage,
        details: {
          unityErrorType: 'validation_error',
          locallyRejectedOperations: [],
          validationWarnings: [warning]
        }
      });
      expect(result.content).toEqual([{
        type: 'text',
        text: expectedMessage
      }]);
      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: {
          operations: [{
            tool: 'open_prefab_contents',
            params: { prefabPath: 'Assets/Prefabs/Card.prefab' },
            id: '0'
          }],
          stopOnError: true,
          atomic: true
        }
      });
    });

    it('should handle partial failures with stopOnError=false', async () => {
      mockSendRequest.mockResolvedValue({
        success: false,
        type: 'text',
        message: 'Batch execution completed with errors. 1/2 operations succeeded, 1 failed.',
        results: [
          { index: 0, id: '0', success: true },
          { index: 1, id: '1', success: false, error: 'Tool failed' }
        ],
        summary: { total: 2, succeeded: 1, failed: 1, executed: 2 }
      });

      const params = {
        operations: [
          { tool: 'tool1', params: {} },
          { tool: 'tool2', params: {} }
        ],
        stopOnError: false
      };

      // With stopOnError=false, should return result even with failures
      const result = await toolHandler(params);
      expect(result.content[0].text).toContain('1/2');
      expect(result.content[0].text).toContain('failed');
      expect(result.isError).toBe(true);
    });

    it('should return isError with payload on failure when stopOnError=true', async () => {
      mockSendRequest.mockResolvedValue({
        success: false,
        type: 'text',
        message: 'Batch execution stopped on error. 0/2 operations succeeded.',
        results: [
          { index: 0, id: '0', success: false, error: 'First tool failed' }
        ],
        summary: { total: 2, succeeded: 0, failed: 1, executed: 1 }
      });

      const params = {
        operations: [
          { tool: 'tool1', params: {} },
          { tool: 'tool2', params: {} }
        ],
        stopOnError: true
      };

      const result = await toolHandler(params);
      expect(result.isError).toBe(true);
      expect(JSON.parse(result.content[1].text).results[0]).toMatchObject({
        status: 'Error',
        error: 'First tool failed'
      });
    });

    it('should preserve failed operation result data', async () => {
      mockSendRequest.mockResolvedValue({
        success: false,
        type: 'text',
        message: 'Batch execution stopped on error.',
        results: [
          {
            index: 0,
            id: 'field-write',
            success: false,
            error: '1 field failed',
            result: {
              success: false,
              failedFields: [{ field: 'tpyo', reason: 'not found' }]
            }
          }
        ],
        summary: { total: 1, succeeded: 0, failed: 1, executed: 1 }
      });

      const result = await toolHandler({
        operations: [{ tool: 'update_component', params: {}, id: 'field-write' }],
        stopOnError: true,
        atomic: false
      });

      const payload = JSON.parse(result.content[1].text);
      expect(result.isError).toBe(true);
      expect(payload.results[0]).toMatchObject({
        id: 'field-write',
        status: 'Error',
        error: '1 field failed',
        data: {
          success: false,
          failedFields: [{ field: 'tpyo', reason: 'not found' }]
        }
      });
    });

    it('preserves Unity image-batch error codes without image payload data', async () => {
      mockSendRequest.mockResolvedValue({
        success: false,
        type: 'text',
        message: 'Batch execution stopped on error.',
        results: [
          {
            index: 0,
            id: 'capture',
            success: false,
            errorCode: 'IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH',
            error: 'Call screenshot_camera directly.'
          }
        ],
        summary: { total: 1, succeeded: 0, failed: 1, executed: 1 }
      });

      const result = await toolHandler({
        operations: [{ tool: 'screenshot_camera', params: {}, id: 'capture' }]
      });

      const payloadText = result.content[1].text;
      const payload = JSON.parse(payloadText);
      expect(result.isError).toBe(true);
      expect(payload.results[0]).toMatchObject({
        id: 'capture',
        status: 'Error',
        errorCode: 'IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH'
      });
      expect(payloadText).not.toContain('data:image');
      expect(payloadText).not.toContain('iVBOR');
    });

    it('defensively strips unexpected image-shaped Unity batch results', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 1/1 operations.',
        results: [
          {
            index: 0,
            id: 'legacy-capture',
            success: true,
            error: 'Side effect: gameViewWindowCreated=true; Unity Undo cannot close this editor window.',
            result: {
              success: true,
              type: 'image',
              mimeType: 'image/png',
              data: 'iVBORw0KGgo=',
              gameViewWindowCreated: true
            }
          }
        ],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      });

      const result = await toolHandler({
        operations: [{ tool: 'screenshot_game_view', params: {}, id: 'legacy-capture' }]
      });

      const payloadText = result.content[1].text;
      const payload = JSON.parse(payloadText);
      expect(result.isError).toBe(true);
      expect(result.content[0].text).toContain('IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH');
      expect(payload.results[0]).toMatchObject({
        id: 'legacy-capture',
        status: 'Error',
        errorCode: 'IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH',
        error: expect.stringContaining('gameViewWindowCreated=true')
      });
      expect(payload.results[0].error).toContain('Unity Undo');
      expect(payloadText).not.toContain('iVBORw0KGgo=');
    });

    it('should preserve operation ids in request', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Successfully executed 2/2 operations.',
        results: [
          { index: 0, id: 'op1', success: true },
          { index: 1, id: 'op2', success: true }
        ],
        summary: { total: 2, succeeded: 2, failed: 0, executed: 2 }
      });

      const params = {
        operations: [
          { tool: 'tool1', params: {}, id: 'op1' },
          { tool: 'tool2', params: {}, id: 'op2' }
        ]
      };

      await toolHandler(params);

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: expect.objectContaining({
          operations: expect.arrayContaining([
            expect.objectContaining({ id: 'op1' }),
            expect.objectContaining({ id: 'op2' })
          ])
        })
      });
    });

    it('should use default values for stopOnError and atomic', async () => {
      mockSendRequest.mockResolvedValue({
        success: true,
        type: 'text',
        message: 'Success',
        results: [],
        summary: { total: 1, succeeded: 1, failed: 0, executed: 1 }
      });

      const params = {
        operations: [{ tool: 'tool1', params: {} }]
      };

      await toolHandler(params);

      expect(mockSendRequest).toHaveBeenCalledWith({
        method: 'batch_execute',
        params: expect.objectContaining({
          stopOnError: true,
          atomic: false
        })
      });
    });
  });
});
