import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerReparentGameObjectTool } from '../tools/gameObjectTools.js';
import { registerGetUIElementInfoTool } from '../tools/uguiTools.js';
import { registerUIAutomationTools } from '../tools/uiAutomationTools.js';
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

function getRegisteredHandler(toolName: string): Function {
  const registration = mockServerTool.mock.calls.find(([name]) => name === toolName);
  if (!registration) {
    throw new Error(`Tool '${toolName}' was not registered`);
  }

  return registration[3] as Function;
}

describe('payload preservation in MCP content', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('returns get_ui_element_info payload in content', async () => {
    const elementInfo = {
      name: 'PlayButton',
      instanceId: 101,
      rectTransform: { anchoredPosition: { x: 12, y: 34 } },
      components: [{ type: 'Button', interactable: true }],
    };
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: 'Retrieved UI element info for PlayButton',
      elementInfo,
    });
    registerGetUIElementInfoTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegisteredHandler('get_ui_element_info')({
      objectPath: 'Canvas/PlayButton',
    });

    expect(result.content).toHaveLength(2);
    expect(JSON.parse(result.content[1].text)).toEqual({
      elementInfo,
      message: 'Retrieved UI element info for PlayButton',
    });
    expect(result).not.toHaveProperty('data');
  });

  it('returns get_ui_element_state payload in content', async () => {
    const response = {
      success: true,
      message: 'UI element state for Canvas/PlayButton',
      path: 'Canvas/PlayButton',
      instanceId: 101,
      active: true,
      activeInHierarchy: true,
      components: { Button: { interactable: true } },
      rectTransform: { sizeDelta: { x: 200, y: 80 } },
      displayText: 'Play',
    };
    mockSendRequest.mockResolvedValue(response);
    registerUIAutomationTools(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegisteredHandler('get_ui_element_state')({
      objectPath: 'Canvas/PlayButton',
    });

    expect(result.content).toHaveLength(2);
    expect(JSON.parse(result.content[1].text)).toEqual({
      path: response.path,
      instanceId: response.instanceId,
      active: response.active,
      activeInHierarchy: response.activeInHierarchy,
      components: response.components,
      rectTransform: response.rectTransform,
      displayText: response.displayText,
      message: response.message,
    });
    expect(result).not.toHaveProperty('data');
  });

  it('keeps a useful preview for oversized get_ui_element_info payloads', async () => {
    const elementInfo = {
      name: 'InventoryPanel',
      children: Array.from({ length: 2500 }, (_, index) => ({
        name: `InventorySlot_${index}`,
        text: 'Large child payload'.repeat(8),
      })),
    };
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: 'Retrieved oversized InventoryPanel info',
      elementInfo,
    });
    registerGetUIElementInfoTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegisteredHandler('get_ui_element_info')({
      objectPath: 'Canvas/InventoryPanel',
      includeChildren: true,
    });
    const summary = JSON.parse(result.content[1].text);

    expect(result.content[0].text).toBe('Retrieved oversized InventoryPanel info');
    expect(result.content[1].text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(summary._truncated).toBe(true);
    expect(summary._preview).toContain('InventoryPanel');
    expect(summary._preview).toContain('InventorySlot_0');
  });

  it('keeps a useful preview for oversized get_ui_element_state payloads', async () => {
    const response = {
      success: true,
      message: 'UI element state for Canvas/InventoryPanel',
      path: 'Canvas/InventoryPanel',
      instanceId: 101,
      active: true,
      activeInHierarchy: true,
      components: Array.from({ length: 2500 }, (_, index) => ({
        type: `InventorySlot_${index}`,
        state: 'Large component state'.repeat(8),
      })),
      rectTransform: { sizeDelta: { x: 800, y: 600 } },
      displayText: 'Inventory',
    };
    mockSendRequest.mockResolvedValue(response);
    registerUIAutomationTools(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegisteredHandler('get_ui_element_state')({
      objectPath: 'Canvas/InventoryPanel',
    });
    const summary = JSON.parse(result.content[1].text);

    expect(result.content[0].text).toBe(response.message);
    expect(result.content[1].text.length).toBeLessThanOrEqual(PAYLOAD_MAX_CHARS);
    expect(summary._truncated).toBe(true);
    expect(summary._preview).toContain('Canvas/InventoryPanel');
    expect(summary._preview).toContain('InventorySlot_0');
  });

  it('returns reparent_gameobject payload in content', async () => {
    mockSendRequest.mockResolvedValue({
      success: true,
      type: 'text',
      message: "Successfully reparented GameObject 'Child' to 'NewParent'.",
      instanceId: 202,
      name: 'Child',
      oldPath: 'OldParent/Child',
      newPath: 'NewParent/Child',
      changed: true,
    });
    registerReparentGameObjectTool(mockServer, mockMcpUnity, mockLogger);

    const result = await getRegisteredHandler('reparent_gameobject')({
      objectPath: 'OldParent/Child',
      newParent: 'NewParent',
    });

    expect(result.content).toHaveLength(2);
    expect(JSON.parse(result.content[1].text)).toEqual({
      instanceId: 202,
      name: 'Child',
      oldPath: 'OldParent/Child',
      newPath: 'NewParent/Child',
      changed: true,
      message: "Successfully reparented GameObject 'Child' to 'NewParent'.",
    });
    expect(result).not.toHaveProperty('data');
  });
});
