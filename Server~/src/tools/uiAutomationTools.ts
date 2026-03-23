import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { McpUnity } from "../unity/mcpUnity.js";
import { McpUnityError, ErrorType } from "../utils/errors.js";
import * as z from "zod";
import { Logger } from "../utils/logger.js";
import { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

// ==================== get_interactable_elements ====================

const getInteractableElementsToolName = "get_interactable_elements";
const getInteractableElementsToolDescription =
  "Scans the scene for all interactable UI elements (Button, Toggle, InputField, Slider, Dropdown, ScrollRect, etc.) and returns their paths, types, and current states. Requires Play Mode.";

const componentTypeFilter = z.enum([
  "Button",
  "Toggle",
  "InputField",
  "TMP_InputField",
  "Slider",
  "Dropdown",
  "TMP_Dropdown",
  "ScrollRect",
  "Scrollbar",
]);

const getInteractableElementsParamsSchema = z.object({
  rootPath: z
    .string()
    .optional()
    .describe(
      "Limit scan scope to children of this GameObject path (null = entire scene)"
    ),
  filter: z
    .array(componentTypeFilter)
    .optional()
    .describe(
      "Filter by component types (e.g., ['Button', 'Toggle']). Null = all types"
    ),
  includeNonInteractable: z
    .boolean()
    .optional()
    .describe(
      "Include elements with interactable=false (default: false)"
    ),
});

function registerGetInteractableElementsTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${getInteractableElementsToolName}`);

  server.tool(
    getInteractableElementsToolName,
    getInteractableElementsToolDescription,
    getInteractableElementsParamsSchema.shape,
    async (params: z.infer<typeof getInteractableElementsParamsSchema>) => {
      try {
        logger.info(
          `Executing tool: ${getInteractableElementsToolName}`,
          params
        );
        const result = await getInteractableElementsHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${getInteractableElementsToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${getInteractableElementsToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function getInteractableElementsHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof getInteractableElementsParamsSchema>
): Promise<CallToolResult> {
  const response = await mcpUnity.sendRequest({
    method: getInteractableElementsToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to get interactable elements"
    );
  }

  return {
    content: [
      {
        type: "text",
        text:
          response.message ||
          `Found ${response.count} interactable element(s)`,
      },
    ],
    data: {
      elements: response.elements,
      count: response.count,
    },
  };
}

// ==================== simulate_pointer_click ====================

const simulatePointerClickToolName = "simulate_pointer_click";
const simulatePointerClickToolDescription =
  "Simulates a full pointer click event sequence (PointerEnter → PointerDown → PointerUp → PointerClick → PointerExit) on a UI element. Requires Play Mode.";

const simulatePointerClickParamsSchema = z.object({
  instanceId: z
    .number()
    .int()
    .optional()
    .describe("Instance ID of the target GameObject"),
  objectPath: z
    .string()
    .optional()
    .describe("Hierarchy path of the target GameObject"),
});

function registerSimulatePointerClickTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${simulatePointerClickToolName}`);

  server.tool(
    simulatePointerClickToolName,
    simulatePointerClickToolDescription,
    simulatePointerClickParamsSchema.shape,
    async (params: z.infer<typeof simulatePointerClickParamsSchema>) => {
      try {
        logger.info(
          `Executing tool: ${simulatePointerClickToolName}`,
          params
        );
        const result = await simulatePointerClickHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${simulatePointerClickToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${simulatePointerClickToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function simulatePointerClickHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof simulatePointerClickParamsSchema>
): Promise<CallToolResult> {
  if (!params.instanceId && (!params.objectPath || params.objectPath.trim() === "")) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: simulatePointerClickToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to simulate pointer click"
    );
  }

  return {
    content: [
      {
        type: "text",
        text:
          response.message ||
          `Successfully clicked ${response.targetPath}`,
      },
    ],
    data: {
      targetPath: response.targetPath,
      eventsDispatched: response.eventsDispatched,
      stateAfter: response.stateAfter,
    },
  };
}

// ==================== simulate_input_field ====================

const simulateInputFieldToolName = "simulate_input_field";
const simulateInputFieldToolDescription =
  "Fills text into an InputField or TMP_InputField, triggering onValueChanged and optionally onEndEdit/onSubmit events. Requires Play Mode.";

const simulateInputFieldParamsSchema = z.object({
  instanceId: z
    .number()
    .int()
    .optional()
    .describe("Instance ID of the target GameObject"),
  objectPath: z
    .string()
    .optional()
    .describe("Hierarchy path of the target GameObject"),
  text: z.string().describe("Text to fill into the input field"),
  mode: z
    .enum(["replace", "append"])
    .optional()
    .describe("'replace' (default) overwrites existing text, 'append' adds to it"),
  submitAfter: z
    .boolean()
    .optional()
    .describe(
      "Whether to trigger onEndEdit/onSubmit after setting text (default: true)"
    ),
});

function registerSimulateInputFieldTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${simulateInputFieldToolName}`);

  server.tool(
    simulateInputFieldToolName,
    simulateInputFieldToolDescription,
    simulateInputFieldParamsSchema.shape,
    async (params: z.infer<typeof simulateInputFieldParamsSchema>) => {
      try {
        logger.info(
          `Executing tool: ${simulateInputFieldToolName}`,
          params
        );
        const result = await simulateInputFieldHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${simulateInputFieldToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${simulateInputFieldToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function simulateInputFieldHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof simulateInputFieldParamsSchema>
): Promise<CallToolResult> {
  if (!params.instanceId && (!params.objectPath || params.objectPath.trim() === "")) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: simulateInputFieldToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to simulate input field"
    );
  }

  return {
    content: [
      {
        type: "text",
        text:
          response.message ||
          `Successfully set text on ${response.inputFieldType} at ${response.targetPath}`,
      },
    ],
    data: {
      targetPath: response.targetPath,
      inputFieldType: response.inputFieldType,
      previousText: response.previousText,
      currentText: response.currentText,
      submitted: response.submitted,
    },
  };
}

// ==================== get_ui_element_state ====================

const getUIElementStateToolName = "get_ui_element_state";
const getUIElementStateToolDescription =
  "Queries the runtime state of a single UI element including component states, RectTransform info, and display text. Works in both Edit and Play Mode.";

const getUIElementStateParamsSchema = z.object({
  instanceId: z
    .number()
    .int()
    .optional()
    .describe("Instance ID of the target GameObject"),
  objectPath: z
    .string()
    .optional()
    .describe("Hierarchy path of the target GameObject"),
});

function registerGetUIElementStateTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${getUIElementStateToolName}`);

  server.tool(
    getUIElementStateToolName,
    getUIElementStateToolDescription,
    getUIElementStateParamsSchema.shape,
    async (params: z.infer<typeof getUIElementStateParamsSchema>) => {
      try {
        logger.info(
          `Executing tool: ${getUIElementStateToolName}`,
          params
        );
        const result = await getUIElementStateHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${getUIElementStateToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${getUIElementStateToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function getUIElementStateHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof getUIElementStateParamsSchema>
): Promise<CallToolResult> {
  if (!params.instanceId && (!params.objectPath || params.objectPath.trim() === "")) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: getUIElementStateToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to get UI element state"
    );
  }

  return {
    content: [
      {
        type: "text",
        text:
          response.message ||
          `UI element state for ${response.path}`,
      },
    ],
    data: {
      path: response.path,
      instanceId: response.instanceId,
      active: response.active,
      activeInHierarchy: response.activeInHierarchy,
      components: response.components,
      rectTransform: response.rectTransform,
      displayText: response.displayText,
    },
  };
}

// ==================== wait_for_condition ====================

const waitForConditionToolName = "wait_for_condition";
const waitForConditionToolDescription =
  "Waits for a specified condition (active, inactive, exists, not_exists, interactable, text_equals, text_contains, component_enabled) on a GameObject with a configurable timeout. Requires Play Mode.";

const conditionEnum = z.enum([
  "active",
  "inactive",
  "exists",
  "not_exists",
  "interactable",
  "text_equals",
  "text_contains",
  "component_enabled",
]);

const waitForConditionParamsSchema = z.object({
  objectPath: z
    .string()
    .describe("Hierarchy path of the target GameObject"),
  condition: conditionEnum.describe("Condition type to wait for"),
  value: z
    .string()
    .optional()
    .describe(
      "Condition parameter: matching text for text_equals/text_contains, component type name for component_enabled"
    ),
  timeout: z
    .number()
    .optional()
    .describe("Timeout in seconds (default: 10, max: 30)"),
  pollInterval: z
    .number()
    .optional()
    .describe("Poll interval in seconds (default: 0.1, min: 0.05)"),
});

function registerWaitForConditionTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${waitForConditionToolName}`);

  server.tool(
    waitForConditionToolName,
    waitForConditionToolDescription,
    waitForConditionParamsSchema.shape,
    async (params: z.infer<typeof waitForConditionParamsSchema>) => {
      try {
        logger.info(
          `Executing tool: ${waitForConditionToolName}`,
          params
        );
        const result = await waitForConditionHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${waitForConditionToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${waitForConditionToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function waitForConditionHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof waitForConditionParamsSchema>
): Promise<CallToolResult> {
  if (!params.objectPath || params.objectPath.trim() === "") {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'objectPath' must be provided"
    );
  }

  // Use a longer timeout for the request since this tool polls internally
  const requestTimeout = ((params.timeout ?? 10) + 5) * 1000;

  const response = await mcpUnity.sendRequest(
    {
      method: waitForConditionToolName,
      params,
    },
    { timeout: requestTimeout }
  );

  // wait_for_condition can return success=false on timeout (not an error)
  if (response.error && !response.condition) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.error?.message || "Failed to wait for condition"
    );
  }

  const isSuccess = response.success === true;

  return {
    content: [
      {
        type: "text",
        text: isSuccess
          ? response.message || `Condition '${params.condition}' met on '${params.objectPath}'`
          : response.error?.message || `Timeout waiting for '${params.condition}' on '${params.objectPath}'`,
      },
    ],
    data: {
      success: isSuccess,
      condition: response.condition,
      objectPath: response.objectPath,
      elapsed: response.elapsed,
      finalState: response.finalState,
    },
    isError: !isSuccess,
  };
}

// ==================== simulate_drag ====================

const simulateDragToolName = "simulate_drag";
const simulateDragToolDescription =
  "Simulates a drag gesture on a UI element with a full event sequence (PointerDown → BeginDrag → Drag (N frames) → EndDrag → PointerUp). Supports delta (pixel offset) or targetPath (drag to another element). Requires Play Mode.";

const vector2Schema = z.object({
  x: z.number().describe("X component (screen pixels)"),
  y: z.number().describe("Y component (screen pixels)"),
});

const simulateDragParamsSchema = z.object({
  instanceId: z
    .number()
    .int()
    .optional()
    .describe("Instance ID of the source GameObject to drag"),
  objectPath: z
    .string()
    .optional()
    .describe("Hierarchy path of the source GameObject to drag"),
  delta: vector2Schema
    .optional()
    .describe(
      "Drag offset in screen pixels relative to element center"
    ),
  targetPath: z
    .string()
    .optional()
    .describe(
      "Hierarchy path of the target GameObject to drag to (alternative to delta)"
    ),
  steps: z
    .number()
    .int()
    .optional()
    .describe("Number of intermediate drag frames (default: 5, max: 60)"),
  duration: z
    .number()
    .optional()
    .describe("Drag duration in seconds (default: 0.3, max: 5)"),
});

function registerSimulateDragTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${simulateDragToolName}`);

  server.tool(
    simulateDragToolName,
    simulateDragToolDescription,
    simulateDragParamsSchema.shape,
    async (params: z.infer<typeof simulateDragParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${simulateDragToolName}`, params);
        const result = await simulateDragHandler(mcpUnity, params);
        logger.info(
          `Tool execution successful: ${simulateDragToolName}`
        );
        return result;
      } catch (error) {
        logger.error(
          `Tool execution failed: ${simulateDragToolName}`,
          error
        );
        throw error;
      }
    }
  );
}

async function simulateDragHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof simulateDragParamsSchema>
): Promise<CallToolResult> {
  if (!params.instanceId && (!params.objectPath || params.objectPath.trim() === "")) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided for the source element"
    );
  }

  if (!params.delta && (!params.targetPath || params.targetPath.trim() === "")) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'delta' or 'targetPath' must be provided"
    );
  }

  // Use a longer timeout for drag operations
  const requestTimeout = ((params.duration ?? 0.3) + 10) * 1000;

  const response = await mcpUnity.sendRequest(
    {
      method: simulateDragToolName,
      params,
    },
    { timeout: requestTimeout }
  );

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to simulate drag"
    );
  }

  return {
    content: [
      {
        type: "text",
        text:
          response.message ||
          `Successfully dragged ${response.sourcePath}`,
      },
    ],
    data: {
      sourcePath: response.sourcePath,
      startPosition: response.startPosition,
      endPosition: response.endPosition,
      totalDelta: response.totalDelta,
      steps: response.steps,
      dropReceiver: response.dropReceiver,
    },
  };
}

// ==================== Aggregate Registration ====================

export function registerUIAutomationTools(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  registerGetInteractableElementsTool(server, mcpUnity, logger);
  registerSimulatePointerClickTool(server, mcpUnity, logger);
  registerSimulateInputFieldTool(server, mcpUnity, logger);
  registerGetUIElementStateTool(server, mcpUnity, logger);
  registerWaitForConditionTool(server, mcpUnity, logger);
  registerSimulateDragTool(server, mcpUnity, logger);
}
