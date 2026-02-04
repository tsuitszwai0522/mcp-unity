import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { McpUnity } from "../unity/mcpUnity.js";
import { McpUnityError, ErrorType } from "../utils/errors.js";
import * as z from "zod";
import { Logger } from "../utils/logger.js";
import { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

// ==================== Shared Schemas ====================

const vector2Schema = z.object({
  x: z.number().describe("X component"),
  y: z.number().describe("Y component"),
});

const vector3Schema = z.object({
  x: z.number().describe("X component"),
  y: z.number().describe("Y component"),
  z: z.number().describe("Z component"),
});

const colorSchema = z.object({
  r: z.number().min(0).max(1).describe("Red component (0-1)"),
  g: z.number().min(0).max(1).describe("Green component (0-1)"),
  b: z.number().min(0).max(1).describe("Blue component (0-1)"),
  a: z.number().min(0).max(1).optional().describe("Alpha component (0-1, default: 1)"),
});

const paddingSchema = z.object({
  left: z.number().int().optional().describe("Left padding"),
  right: z.number().int().optional().describe("Right padding"),
  top: z.number().int().optional().describe("Top padding"),
  bottom: z.number().int().optional().describe("Bottom padding"),
});

const anchorPresetEnum = z.enum([
  "topLeft", "topCenter", "topRight", "topStretch",
  "middleLeft", "middleCenter", "middleRight", "middleStretch",
  "bottomLeft", "bottomCenter", "bottomRight", "bottomStretch",
  "stretchLeft", "stretchCenter", "stretchRight", "stretch"
]).describe("Anchor preset name");

const rectTransformSchema = z.object({
  anchorPreset: anchorPresetEnum.optional(),
  anchorMin: vector2Schema.optional().describe("Minimum anchor point (0-1)"),
  anchorMax: vector2Schema.optional().describe("Maximum anchor point (0-1)"),
  pivot: vector2Schema.optional().describe("Pivot point (0-1)"),
  anchoredPosition: vector2Schema.optional().describe("Position relative to anchors"),
  sizeDelta: vector2Schema.optional().describe("Size relative to anchors"),
});

// ==================== create_canvas ====================

const createCanvasToolName = "create_canvas";
const createCanvasToolDescription = "Creates a Canvas with CanvasScaler and GraphicRaycaster components, and optionally an EventSystem";

const createCanvasParamsSchema = z.object({
  objectPath: z.string().describe("Path for the Canvas GameObject (e.g., 'MainCanvas' or 'UI/GameCanvas')"),
  renderMode: z.enum(["ScreenSpaceOverlay", "ScreenSpaceCamera", "WorldSpace"])
    .optional()
    .describe("Canvas render mode (default: ScreenSpaceOverlay)"),
  cameraPath: z.string().optional().describe("Path to camera for ScreenSpaceCamera/WorldSpace modes"),
  sortingOrder: z.number().int().optional().describe("Sorting order for the canvas"),
  pixelPerfect: z.boolean().optional().describe("Enable pixel perfect rendering"),
  scaler: z.object({
    uiScaleMode: z.enum(["ConstantPixelSize", "ScaleWithScreenSize", "ConstantPhysicalSize"])
      .optional()
      .describe("UI scale mode"),
    referenceResolution: vector2Schema.optional().describe("Reference resolution for ScaleWithScreenSize mode"),
    screenMatchMode: z.enum(["MatchWidthOrHeight", "Expand", "Shrink"])
      .optional()
      .describe("Screen match mode for ScaleWithScreenSize"),
    matchWidthOrHeight: z.number().min(0).max(1).optional().describe("Match width (0) or height (1) factor"),
  }).optional().describe("Canvas scaler settings"),
  createEventSystem: z.boolean().optional().describe("Create EventSystem if none exists (default: true)"),
});

export function registerCreateCanvasTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${createCanvasToolName}`);

  server.tool(
    createCanvasToolName,
    createCanvasToolDescription,
    createCanvasParamsSchema.shape,
    async (params: z.infer<typeof createCanvasParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${createCanvasToolName}`, params);
        const result = await createCanvasHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${createCanvasToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${createCanvasToolName}`, error);
        throw error;
      }
    }
  );
}

async function createCanvasHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof createCanvasParamsSchema>
): Promise<CallToolResult> {
  if (!params.objectPath || params.objectPath.trim() === "") {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: createCanvasToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to create canvas"
    );
  }

  return {
    content: [
      {
        type: response.type || "text",
        text: response.message || `Successfully created Canvas at '${params.objectPath}'`,
      },
    ],
    data: {
      instanceId: response.instanceId,
      path: response.path,
    },
  };
}

// ==================== create_ui_element ====================

const createUIElementToolName = "create_ui_element";
const createUIElementToolDescription = "Creates a UI element (Button, Text, TextMeshPro, Image, RawImage, Panel, InputField, InputFieldTMP, Toggle, Slider, Dropdown, DropdownTMP, ScrollView, Scrollbar)";

const elementTypeEnum = z.enum([
  "Button", "Text", "TextMeshPro", "Image", "RawImage", "Panel",
  "InputField", "InputFieldTMP", "Toggle", "Slider",
  "Dropdown", "DropdownTMP", "ScrollView", "Scrollbar"
]);

const createUIElementParamsSchema = z.object({
  objectPath: z.string().describe("Path for the UI element (e.g., 'Canvas/Panel/Button')"),
  elementType: elementTypeEnum.describe("Type of UI element to create"),
  rectTransform: rectTransformSchema.optional().describe("RectTransform settings"),
  elementData: z.object({
    text: z.string().optional().describe("Text content (for Text, Button, Toggle, InputField)"),
    placeholder: z.string().optional().describe("Placeholder text (for InputField)"),
    fontSize: z.number().optional().describe("Font size"),
    color: colorSchema.optional().describe("Color"),
    interactable: z.boolean().optional().describe("Whether the element is interactable"),
    raycastTarget: z.boolean().optional().describe("Whether the element is a raycast target"),
    alignment: z.string().optional().describe("Text alignment (e.g., 'MiddleCenter')"),
    isOn: z.boolean().optional().describe("Toggle state"),
    value: z.number().optional().describe("Value for Slider/Dropdown/Scrollbar"),
    minValue: z.number().optional().describe("Minimum value for Slider"),
    maxValue: z.number().optional().describe("Maximum value for Slider"),
    wholeNumbers: z.boolean().optional().describe("Use whole numbers for Slider"),
    options: z.array(z.string()).optional().describe("Options for Dropdown"),
    horizontal: z.boolean().optional().describe("Enable horizontal scrolling (ScrollView)"),
    vertical: z.boolean().optional().describe("Enable vertical scrolling (ScrollView)"),
    size: z.number().optional().describe("Handle size for Scrollbar (0-1)"),
    direction: z.string().optional().describe("Direction for Scrollbar (LeftToRight, RightToLeft, BottomToTop, TopToBottom)"),
  }).optional().describe("Element-specific properties"),
});

export function registerCreateUIElementTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${createUIElementToolName}`);

  server.tool(
    createUIElementToolName,
    createUIElementToolDescription,
    createUIElementParamsSchema.shape,
    async (params: z.infer<typeof createUIElementParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${createUIElementToolName}`, params);
        const result = await createUIElementHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${createUIElementToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${createUIElementToolName}`, error);
        throw error;
      }
    }
  );
}

async function createUIElementHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof createUIElementParamsSchema>
): Promise<CallToolResult> {
  if (!params.objectPath || params.objectPath.trim() === "") {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'objectPath' must be provided"
    );
  }

  if (!params.elementType) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'elementType' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: createUIElementToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to create UI element"
    );
  }

  return {
    content: [
      {
        type: response.type || "text",
        text: response.message || `Successfully created ${params.elementType} at '${params.objectPath}'`,
      },
    ],
    data: {
      instanceId: response.instanceId,
      path: response.path,
      usedFallback: response.usedFallback,
    },
  };
}

// ==================== set_rect_transform ====================

const setRectTransformToolName = "set_rect_transform";
const setRectTransformToolDescription = "Modifies RectTransform properties of a UI element (anchors, pivot, position, size, rotation, scale)";

const setRectTransformParamsSchema = z.object({
  instanceId: z.number().int().optional().describe("Instance ID of the GameObject"),
  objectPath: z.string().optional().describe("Path to the GameObject in the hierarchy"),
  anchorPreset: anchorPresetEnum.optional(),
  anchorMin: vector2Schema.optional().describe("Minimum anchor point (0-1)"),
  anchorMax: vector2Schema.optional().describe("Maximum anchor point (0-1)"),
  pivot: vector2Schema.optional().describe("Pivot point (0-1)"),
  anchoredPosition: vector2Schema.optional().describe("Position relative to anchors"),
  sizeDelta: vector2Schema.optional().describe("Size relative to anchors"),
  offsetMin: vector2Schema.optional().describe("Lower left corner offset from anchor"),
  offsetMax: vector2Schema.optional().describe("Upper right corner offset from anchor"),
  rotation: vector3Schema.optional().describe("Local rotation in Euler angles"),
  localScale: vector3Schema.optional().describe("Local scale"),
});

export function registerSetRectTransformTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${setRectTransformToolName}`);

  server.tool(
    setRectTransformToolName,
    setRectTransformToolDescription,
    setRectTransformParamsSchema.shape,
    async (params: z.infer<typeof setRectTransformParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${setRectTransformToolName}`, params);
        const result = await setRectTransformHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${setRectTransformToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${setRectTransformToolName}`, error);
        throw error;
      }
    }
  );
}

async function setRectTransformHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof setRectTransformParamsSchema>
): Promise<CallToolResult> {
  if (
    (params.instanceId === undefined || params.instanceId === null) &&
    (!params.objectPath || params.objectPath.trim() === "")
  ) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: setRectTransformToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to set RectTransform"
    );
  }

  const targetDescription = params.objectPath
    ? `path '${params.objectPath}'`
    : `ID ${params.instanceId}`;

  return {
    content: [
      {
        type: response.type || "text",
        text: response.message || `Successfully updated RectTransform with ${targetDescription}`,
      },
    ],
    data: {
      instanceId: response.instanceId,
      path: response.path,
      rectTransform: response.rectTransform,
    },
  };
}

// ==================== add_layout_component ====================

const addLayoutComponentToolName = "add_layout_component";
const addLayoutComponentToolDescription = "Adds a layout component (HorizontalLayoutGroup, VerticalLayoutGroup, GridLayoutGroup, ContentSizeFitter, LayoutElement, AspectRatioFitter) to a UI element";

const layoutTypeEnum = z.enum([
  "HorizontalLayoutGroup", "VerticalLayoutGroup", "GridLayoutGroup",
  "ContentSizeFitter", "LayoutElement", "AspectRatioFitter"
]);

const addLayoutComponentParamsSchema = z.object({
  instanceId: z.number().int().optional().describe("Instance ID of the GameObject"),
  objectPath: z.string().optional().describe("Path to the GameObject in the hierarchy"),
  layoutType: layoutTypeEnum.describe("Type of layout component to add"),
  layoutData: z.object({
    // Common LayoutGroup
    padding: paddingSchema.optional().describe("Padding around children"),
    spacing: z.number().optional().describe("Spacing between children"),
    childAlignment: z.string().optional().describe("Child alignment (e.g., 'UpperLeft', 'MiddleCenter')"),
    reverseArrangement: z.boolean().optional().describe("Reverse child arrangement"),
    childControlWidth: z.boolean().optional().describe("Control child width"),
    childControlHeight: z.boolean().optional().describe("Control child height"),
    childScaleWidth: z.boolean().optional().describe("Use child scale width"),
    childScaleHeight: z.boolean().optional().describe("Use child scale height"),
    childForceExpandWidth: z.boolean().optional().describe("Force expand child width"),
    childForceExpandHeight: z.boolean().optional().describe("Force expand child height"),

    // GridLayoutGroup specific
    cellSize: vector2Schema.optional().describe("Size of each cell"),
    startCorner: z.string().optional().describe("Grid start corner (UpperLeft, UpperRight, LowerLeft, LowerRight)"),
    startAxis: z.string().optional().describe("Grid start axis (Horizontal, Vertical)"),
    constraint: z.enum(["Flexible", "FixedColumnCount", "FixedRowCount"]).optional().describe("Grid constraint"),
    constraintCount: z.number().int().optional().describe("Constraint count"),

    // ContentSizeFitter
    horizontalFit: z.enum(["Unconstrained", "MinSize", "PreferredSize"]).optional().describe("Horizontal fit mode"),
    verticalFit: z.enum(["Unconstrained", "MinSize", "PreferredSize"]).optional().describe("Vertical fit mode"),

    // LayoutElement
    ignoreLayout: z.boolean().optional().describe("Ignore layout"),
    minWidth: z.number().optional().describe("Minimum width"),
    minHeight: z.number().optional().describe("Minimum height"),
    preferredWidth: z.number().optional().describe("Preferred width"),
    preferredHeight: z.number().optional().describe("Preferred height"),
    flexibleWidth: z.number().optional().describe("Flexible width"),
    flexibleHeight: z.number().optional().describe("Flexible height"),
    layoutPriority: z.number().int().optional().describe("Layout priority"),

    // AspectRatioFitter
    aspectMode: z.string().optional().describe("Aspect mode (None, WidthControlsHeight, HeightControlsWidth, FitInParent, EnvelopeParent)"),
    aspectRatio: z.number().optional().describe("Aspect ratio"),
  }).optional().describe("Layout component settings"),
});

export function registerAddLayoutComponentTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${addLayoutComponentToolName}`);

  server.tool(
    addLayoutComponentToolName,
    addLayoutComponentToolDescription,
    addLayoutComponentParamsSchema.shape,
    async (params: z.infer<typeof addLayoutComponentParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${addLayoutComponentToolName}`, params);
        const result = await addLayoutComponentHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${addLayoutComponentToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${addLayoutComponentToolName}`, error);
        throw error;
      }
    }
  );
}

async function addLayoutComponentHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof addLayoutComponentParamsSchema>
): Promise<CallToolResult> {
  if (
    (params.instanceId === undefined || params.instanceId === null) &&
    (!params.objectPath || params.objectPath.trim() === "")
  ) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  if (!params.layoutType) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "'layoutType' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: addLayoutComponentToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to add layout component"
    );
  }

  const targetDescription = params.objectPath
    ? `path '${params.objectPath}'`
    : `ID ${params.instanceId}`;

  return {
    content: [
      {
        type: response.type || "text",
        text: response.message || `Successfully added ${params.layoutType} to ${targetDescription}`,
      },
    ],
    data: {
      instanceId: response.instanceId,
      path: response.path,
    },
  };
}

// ==================== get_ui_element_info ====================

const getUIElementInfoToolName = "get_ui_element_info";
const getUIElementInfoToolDescription = "Gets detailed information about a UI element including RectTransform, UI components, and layout settings";

const getUIElementInfoParamsSchema = z.object({
  instanceId: z.number().int().optional().describe("Instance ID of the GameObject"),
  objectPath: z.string().optional().describe("Path to the GameObject in the hierarchy"),
  includeChildren: z.boolean().optional().describe("Include information about child elements (default: false)"),
});

export function registerGetUIElementInfoTool(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  logger.info(`Registering tool: ${getUIElementInfoToolName}`);

  server.tool(
    getUIElementInfoToolName,
    getUIElementInfoToolDescription,
    getUIElementInfoParamsSchema.shape,
    async (params: z.infer<typeof getUIElementInfoParamsSchema>) => {
      try {
        logger.info(`Executing tool: ${getUIElementInfoToolName}`, params);
        const result = await getUIElementInfoHandler(mcpUnity, params);
        logger.info(`Tool execution successful: ${getUIElementInfoToolName}`);
        return result;
      } catch (error) {
        logger.error(`Tool execution failed: ${getUIElementInfoToolName}`, error);
        throw error;
      }
    }
  );
}

async function getUIElementInfoHandler(
  mcpUnity: McpUnity,
  params: z.infer<typeof getUIElementInfoParamsSchema>
): Promise<CallToolResult> {
  if (
    (params.instanceId === undefined || params.instanceId === null) &&
    (!params.objectPath || params.objectPath.trim() === "")
  ) {
    throw new McpUnityError(
      ErrorType.VALIDATION,
      "Either 'instanceId' or 'objectPath' must be provided"
    );
  }

  const response = await mcpUnity.sendRequest({
    method: getUIElementInfoToolName,
    params,
  });

  if (!response.success) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response.message || "Failed to get UI element info"
    );
  }

  const targetDescription = params.objectPath
    ? `path '${params.objectPath}'`
    : `ID ${params.instanceId}`;

  return {
    content: [
      {
        type: response.type || "text",
        text: response.message || `Retrieved UI element info for ${targetDescription}`,
      },
    ],
    data: {
      elementInfo: response.elementInfo,
    },
  };
}

// ==================== Combined Registration ====================

/**
 * Registers all UGUI tools with the MCP server
 */
export function registerUGUITools(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger
) {
  registerCreateCanvasTool(server, mcpUnity, logger);
  registerCreateUIElementTool(server, mcpUnity, logger);
  registerSetRectTransformTool(server, mcpUnity, logger);
  registerAddLayoutComponentTool(server, mcpUnity, logger);
  registerGetUIElementInfoTool(server, mcpUnity, logger);
}
