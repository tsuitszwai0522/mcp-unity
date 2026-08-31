import * as z from 'zod';
import { McpUnity } from '../unity/mcpUnity.js';
import { Logger } from '../utils/logger.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';
import { payloadContent } from '../utils/toolPayload.js';
import { getRegisteredToolSchema } from '../utils/structuredContentSeam.js';

const toolName = 'batch_execute';
const toolDescription = `Executes multiple tool operations in a single batch request.
Reduces network round-trips and enables Undo-backed atomic operations outside active Prefab contents sessions.
atomic=true is rejected while a Prefab contents session is active and cannot include open_prefab_contents because preview changes bypass Unity Undo.
Atomic rollback only restores Undo-tracked in-memory state. Asset paths observed during the batch window are reported, may include other editor activity, and have no disk-reversion guarantee from Unity Undo.
Inner schema validation occurs before any operation executes; with stopOnError=true, any operation validation failure prevents the entire batch from executing.
Performance improvement: 10-100x for repetitive operations.`;

const operationSchema = z.object({
  tool: z.string().describe('The name of the tool to execute'),
  params: z.record(z.string(), z.any()).optional().default({}).describe('Parameters to pass to the tool'),
  id: z.string().optional().describe('Optional identifier for this operation (for tracking in results)')
});

const paramsSchema = z.object({
  operations: z.array(operationSchema)
    .min(1, 'At least one operation is required')
    .max(100, 'Maximum of 100 operations allowed per batch')
    .describe('Array of operations to execute sequentially'),
  stopOnError: z.boolean()
    .default(true)
    .describe('If true, stops execution on the first error. Default: true'),
  atomic: z.boolean()
    .default(false)
    .describe('If true, restores Undo-tracked in-memory state if any operation fails. Asset paths observed during the batch window are reported, may include other editor activity, and have no disk-reversion guarantee from Unity Undo. Rejected while a Prefab contents session is active and cannot include open_prefab_contents. Default: false')
});

/**
 * Result of a single operation in the batch
 */
interface OperationResult {
  index: number;
  id: string;
  success: boolean;
  result?: any;
  error?: string;
  errorCode?: string;
}

/**
 * Summary of batch execution
 */
interface BatchSummary {
  total: number;
  succeeded: number;
  failed: number;
  executed: number;
}

/**
 * Response from the batch execute tool
 */
interface BatchExecuteResponse {
  success: boolean;
  type: string;
  message: string;
  results: OperationResult[];
  summary: BatchSummary;
  unrevertedAssetWrites?: string[];
}

const validationErrorMessage = (
  index: number,
  id: string,
  tool: string,
  reason: string,
): string => `Batch operation ${index} (id "${id}", tool "${tool}") ${reason}`;

const pickParsedCallerParams = (
  parsedParams: Record<string, unknown>,
  callerParams: Record<string, unknown>,
): Record<string, unknown> => Object.fromEntries(
  Object.keys(callerParams)
    .filter((key) => Object.prototype.hasOwnProperty.call(parsedParams, key))
    .map((key) => [key, parsedParams[key]]),
);

const throwWithNodeSideContext = (
  error: unknown,
  locallyRejectedOperations: OperationResult[],
  validationWarnings: string[],
): never => {
  if (locallyRejectedOperations.length === 0 && validationWarnings.length === 0) {
    throw error;
  }

  const type = error instanceof McpUnityError ? error.type : ErrorType.INTERNAL;
  const originalMessage = error instanceof Error ? error.message : String(error);
  const contextLines = [
    ...(locallyRejectedOperations.length === 0
      ? []
      : [
          'Node-side rejections:',
          ...locallyRejectedOperations.map((operation) => `  - ${operation.error}`),
        ]),
    ...(validationWarnings.length === 0
      ? []
      : [
          'Warnings:',
          ...validationWarnings.map((warning) => `  - ${warning}`),
        ]),
  ];
  const originalDetails = error instanceof McpUnityError ? error.details : undefined;
  const details = originalDetails !== null
    && typeof originalDetails === 'object'
    && !Array.isArray(originalDetails)
    ? originalDetails
    : originalDetails === undefined
      ? {}
      : { unityErrorDetails: originalDetails };

  throw new McpUnityError(
    type,
    `${originalMessage}\n\n${contextLines.join('\n')}`,
    {
      ...details,
      locallyRejectedOperations,
      validationWarnings,
    },
  );
};

/**
 * Creates and registers the Batch Execute tool with the MCP server
 */
export function registerBatchExecuteTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  logger.info(`Registering tool: ${toolName}`);

  server.tool(
    toolName,
    toolDescription,
    paramsSchema.shape,
    async (params: any) => {
      try {
        logger.info(`Executing tool: ${toolName}`, {
          operationCount: params.operations?.length,
          stopOnError: params.stopOnError,
          atomic: params.atomic
        });
        const result = await batchExecuteHandler(server, mcpUnity, params, logger);
        logger.info(`Tool execution completed: ${toolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${toolName}`, error);
        throw error;
      }
    }
  );
}

async function batchExecuteHandler(
  server: McpServer,
  mcpUnity: McpUnity,
  params: z.infer<typeof paramsSchema>,
  logger: Logger
): Promise<CallToolResult> {
  // Validate operations array
  if (!params.operations || params.operations.length === 0) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "The 'operations' array is required and must contain at least one operation"
    );
  }

  if (params.operations.length > 100) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Maximum of 100 operations allowed per batch"
    );
  }

  const stopOnError = params.stopOnError ?? true;
  const forwardedOperationIndexes: number[] = [];
  const locallyRejectedOperations: OperationResult[] = [];
  const validationWarnings: string[] = [];
  const forwardedOperations = params.operations.flatMap((op, index) => {
    const id = op.id ?? index.toString();
    let validationFailure: string | undefined;

    if (op.tool === toolName) {
      validationFailure = validationErrorMessage(
        index,
        id,
        op.tool,
        'Cannot nest batch_execute operations',
      );
    }

    const schema = getRegisteredToolSchema(server, op.tool);
    if (!validationFailure && !schema) {
      const warning = validationErrorMessage(
        index,
        id,
        op.tool,
        'was not validated by Node because its schema is not registered; Unity will perform authoritative tool lookup and validation.',
      );
      validationWarnings.push(warning);
      logger.warn(warning);
    }

    const validation = validationFailure || !schema
      ? undefined
      : schema.safeParse(op.params ?? {});
    if (validation && !validation.success) {
      const details = validation.error.issues
        .map((issue) => issue.path.length > 0
          ? `${issue.message} at ${issue.path.join('.')}`
          : issue.message)
        .join('; ');
      validationFailure = validationErrorMessage(
        index,
        id,
        op.tool,
        `has invalid params: ${details}`,
      );
    }

    if (validationFailure) {
      if (stopOnError) {
        throw new McpUnityError(ErrorType.VALIDATION, validationFailure);
      }

      locallyRejectedOperations.push({
        index,
        id,
        success: false,
        error: validationFailure,
        errorCode: 'NODE_SCHEMA_VALIDATION',
      });
      return [];
    }

    const parsedParams = validation?.success
      ? validation.data as Record<string, unknown>
      : undefined;
    const callerParams = op.params ?? {};
    forwardedOperationIndexes.push(index);
    return [{
      tool: op.tool,
      // Keep caller presence semantics while matching direct-call coercion.
      // A caller-provided top-level object retains any parsed nested defaults.
      params: parsedParams
        ? pickParsedCallerParams(parsedParams, callerParams)
        : callerParams,
      id,
    }];
  });

  logger.info(`Sending batch with ${forwardedOperations.length} operations to Unity`);

  let response: BatchExecuteResponse;
  if (forwardedOperations.length === 0) {
    response = {
      success: false,
      type: 'text',
      message: `Batch execution completed with errors. 0/${params.operations.length} operations succeeded, ${locallyRejectedOperations.length} failed.`,
      results: locallyRejectedOperations,
      summary: {
        total: params.operations.length,
        succeeded: 0,
        failed: locallyRejectedOperations.length,
        executed: 0,
      },
    };
  } else {
    const unityResponse = await mcpUnity.sendRequest({
      method: toolName,
      params: {
        operations: forwardedOperations,
        stopOnError,
        atomic: params.atomic ?? false
      }
    })
      .catch((error) => throwWithNodeSideContext(
        error,
        locallyRejectedOperations,
        validationWarnings,
      )) as BatchExecuteResponse;

    const rawUnityResults = Array.isArray(unityResponse.results)
      ? unityResponse.results
      : [];
    if (!stopOnError) {
      const receivedIndexes = rawUnityResults.map((result) => result.index);
      const receivedIndexSet = new Set(receivedIndexes);
      const missingForwardedIndexes = forwardedOperationIndexes.filter(
        (_originalIndex, forwardedIndex) => !receivedIndexSet.has(forwardedIndex),
      );
      const hasUnexpectedIndexes = receivedIndexes.some((index) =>
        !Number.isInteger(index) || index < 0 || index >= forwardedOperations.length);
      const hasDuplicateIndexes = receivedIndexSet.size !== receivedIndexes.length;

      if (
        rawUnityResults.length !== forwardedOperations.length
        || missingForwardedIndexes.length > 0
        || hasUnexpectedIndexes
        || hasDuplicateIndexes
      ) {
        const protocolError = new McpUnityError(
          ErrorType.TOOL_EXECUTION,
          'Unity batch response violated result coverage for stopOnError=false: ' +
          `expected ${forwardedOperations.length} results but received ${rawUnityResults.length}; ` +
          `missing original operation indexes: ${missingForwardedIndexes.join(', ') || 'none'}.`,
          {
            expectedResultCount: forwardedOperations.length,
            actualResultCount: rawUnityResults.length,
            missingOriginalOperationIndexes: missingForwardedIndexes,
            receivedUnityResultIndexes: receivedIndexes,
          },
        );
        throwWithNodeSideContext(
          protocolError,
          locallyRejectedOperations,
          validationWarnings,
        );
      }
    }

    if (locallyRejectedOperations.length === 0) {
      response = unityResponse;
    } else {
      const unityResults = rawUnityResults.map((result, resultIndex) => ({
        ...result,
        index: forwardedOperationIndexes[result.index]
          ?? forwardedOperationIndexes[resultIndex]
          ?? result.index,
      }));
      const results = [...unityResults, ...locallyRejectedOperations]
        .sort((left, right) => left.index - right.index);
      const succeeded = unityResponse.summary?.succeeded
        ?? unityResults.filter((result) => result.success).length;
      const unityFailed = unityResponse.summary?.failed
        ?? unityResults.filter((result) => !result.success).length;
      const failed = unityFailed + locallyRejectedOperations.length;
      const executed = unityResponse.summary?.executed ?? unityResults.length;

      response = {
        ...unityResponse,
        success: false,
        message: `Batch execution completed with errors. ${succeeded}/${params.operations.length} operations succeeded, ${failed} failed.`,
        results,
        summary: {
          total: params.operations.length,
          succeeded,
          failed,
          executed,
        },
      };
    }
  }

  // Format the response message
  let resultText = response.message || 'Batch execution completed';

  // Add summary details
  if (response.summary) {
    resultText += `\n\nSummary: ${response.summary.succeeded}/${response.summary.total} succeeded`;
    if (response.summary.failed > 0) {
      resultText += `, ${response.summary.failed} failed`;
    }
  }

  if (validationWarnings.length > 0) {
    resultText += '\n\nWarnings:\n' + validationWarnings
      .map((warning) => `  - ${warning}`)
      .join('\n');
  }

  // Build structured results with full tool data for each operation
  const structuredResults = response.results?.map((res: OperationResult) => {
    if (res.result?.type === 'image') {
      const genericError =
        'Image results are not supported in batch_execute; call the image-producing tool directly.';
      const unitySideEffect = typeof res.error === 'string'
        && res.error.includes('gameViewWindowCreated=true')
        ? res.error
        : res.result.gameViewWindowCreated === true
          ? 'Side effect: gameViewWindowCreated=true; Unity Undo cannot close this editor window.'
          : undefined;
      return {
        id: res.id,
        status: 'Error',
        errorCode: 'IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH',
        error: unitySideEffect ? `${genericError} ${unitySideEffect}` : genericError
      };
    }

    const entry: Record<string, any> = {
      id: res.id,
      status: res.success ? 'OK' : 'Error'
    };
    if (res.result !== undefined) {
      entry.data = res.result;
    }
    if (!res.success && res.error) {
      entry.error = res.error;
    }
    if (!res.success && res.errorCode) {
      entry.errorCode = res.errorCode;
    }
    return entry;
  }) ?? [];

  if (structuredResults.some((result) =>
    result.errorCode === 'IMAGE_RESULT_NOT_SUPPORTED_IN_BATCH')) {
    resultText += '\n\nIMAGE_RESULT_NOT_SUPPORTED_IN_BATCH: Call the image-producing tool directly.';
  }

  // Include full results JSON so AI clients can access each tool's return data
  const structuredPayload = {
    message: response.message
      || `${response.summary?.succeeded ?? 0}/${response.summary?.total ?? structuredResults.length} operations succeeded`,
    results: structuredResults,
    summary: response.summary,
    ...(response.unrevertedAssetWrites === undefined
      ? {}
      : { unrevertedAssetWrites: response.unrevertedAssetWrites }),
    ...(validationWarnings.length === 0 ? {} : { warnings: validationWarnings }),
  };

  const hasOperationFailures = !response.success
    || structuredResults.some((result) => result.status === 'Error');

  const result: CallToolResult = {
    content: [
      {
        type: 'text',
        text: resultText
      },
      payloadContent(structuredPayload)
    ]
  };

  if (hasOperationFailures) {
    result.isError = true;
  }

  return result;
}
