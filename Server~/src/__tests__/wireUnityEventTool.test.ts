import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import { registerWireUnityEventTool } from '../tools/wireUnityEventTool.js';
import { ErrorType, McpUnityError } from '../utils/errors.js';

const sendRequest = jest.fn();
const mockMcpUnity = { sendRequest } as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;
const registerTool = jest.fn();
const mockServer = { registerTool } as any;

function registration() {
  registerWireUnityEventTool(mockServer, mockMcpUnity, mockLogger);
  return registerTool.mock.calls[0] as any;
}

function successResponse() {
  return {
    success: true,
    message: "Wired 'm_OnClick' to 'UnityEngine.GameObject.SetActive(System.Boolean)' using Bool (6).",
    instanceId: 10,
    componentName: 'UnityEngine.UI.Button',
    eventFieldName: 'm_OnClick',
    listenerIndex: 0,
    listenerTarget: { instanceId: 20, name: 'Panel', type: 'GameObject' },
    methodName: 'SetActive',
    mode: { name: 'Bool', value: 6, index: 6 },
    callState: { name: 'RuntimeOnly', value: 2, index: 2 },
    staticArgument: true,
    persistentCall: {
      m_MethodName: 'SetActive',
      m_Mode: { name: 'Bool', value: 6, index: 6 },
      m_Arguments: { m_BoolArgument: true },
    },
  };
}

describe('wire_unity_event', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('registers a mode-free schema and documents signature inference', () => {
    const [name, config] = registration();
    const { description, inputSchema } = config;

    expect(name).toBe('wire_unity_event');
    expect(description).toContain('PersistentListenerMode/m_Mode is not an input');
    expect(description).toContain('derived from the UnityEvent generic signature');
    expect(description).toContain('Ambiguous');
    expect(inputSchema.shape).not.toHaveProperty('mode');
    expect(inputSchema.shape).not.toHaveProperty('m_Mode');
    expect(inputSchema.shape).toHaveProperty('staticArgument');
  });

  it('rejects unknown top-level keys instead of stripping caller-supplied m_Mode', () => {
    const [, config] = registration();

    expect(() => config.inputSchema.parse({
      instanceId: 10,
      componentName: 'Probe',
      eventFieldName: 'event',
      listenerInstanceId: 20,
      methodName: 'Receive',
      m_Mode: 'String',
    })).toThrow();
    expect(sendRequest).not.toHaveBeenCalled();
  });

  it('forwards listener intent without synthesizing a mode', async () => {
    sendRequest.mockResolvedValue(successResponse() as never);
    const handler = registration()[2];

    await handler({
      objectPath: 'Canvas/Button',
      componentName: 'UnityEngine.UI.Button',
      eventFieldName: 'm_OnClick',
      listenerInstanceId: 20,
      methodName: 'SetActive',
      staticArgument: true,
    });

    expect(sendRequest).toHaveBeenCalledWith({
      method: 'wire_unity_event',
      params: {
        instanceId: undefined,
        objectPath: 'Canvas/Button',
        componentName: 'UnityEngine.UI.Button',
        eventFieldName: 'm_OnClick',
        listenerInstanceId: 20,
        listenerObjectPath: undefined,
        listenerComponentName: undefined,
        methodName: 'SetActive',
        staticArgument: true,
      },
    });
    expect((sendRequest.mock.calls[0] as any)[0].params).not.toHaveProperty('mode');
    expect((sendRequest.mock.calls[0] as any)[0].params).not.toHaveProperty('m_Mode');
  });

  it('returns inferred mode and Unity read-back payload', async () => {
    sendRequest.mockResolvedValue(successResponse() as never);
    const handler = registration()[2];

    const result = await handler({
      instanceId: 10,
      componentName: 'UnityEngine.UI.Button',
      eventFieldName: 'm_OnClick',
      listenerObjectPath: 'Canvas/Panel',
      methodName: 'SetActive',
      staticArgument: true,
    });

    expect(result.content[0].text).toContain('Inferred mode: Bool (6)');
    const payload = JSON.parse(result.content[1].text);
    expect(payload.mode).toEqual({ name: 'Bool', value: 6, index: 6 });
    expect(payload.persistentCall.m_MethodName).toBe('SetActive');
    expect(payload.staticArgument).toBe(true);
  });

  it('preserves an explicit null static argument', async () => {
    const response = successResponse();
    response.staticArgument = null as any;
    sendRequest.mockResolvedValue(response as never);
    const handler = registration()[2];

    await handler({
      instanceId: 10,
      componentName: 'Probe',
      eventFieldName: 'event',
      listenerInstanceId: 20,
      listenerComponentName: 'Receiver',
      methodName: 'ReceiveObject',
      staticArgument: null,
    });

    expect((sendRequest.mock.calls[0] as any)[0].params)
      .toHaveProperty('staticArgument', null);
  });

  it('rejects missing or competing source/listener locators before contacting Unity', async () => {
    const handler = registration()[2];

    await expect(handler({
      componentName: 'Probe',
      eventFieldName: 'event',
      listenerInstanceId: 20,
      methodName: 'Receive',
    })).rejects.toThrow("Source requires exactly one of 'instanceId' or 'objectPath'");

    await expect(handler({
      instanceId: 10,
      objectPath: 'Also/Provided',
      componentName: 'Probe',
      eventFieldName: 'event',
      listenerInstanceId: 20,
      methodName: 'Receive',
    })).rejects.toThrow("Source requires exactly one of 'instanceId' or 'objectPath'");

    await expect(handler({
      instanceId: 10,
      componentName: 'Probe',
      eventFieldName: 'event',
      methodName: 'Receive',
    })).rejects.toThrow("Listener requires exactly one of 'listenerInstanceId' or 'listenerObjectPath'");

    expect(sendRequest).not.toHaveBeenCalled();
  });

  it('promotes Unity method/signature failures as tool execution errors', async () => {
    sendRequest.mockRejectedValue(new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      "Method 'Missing' was not found",
      { unityErrorType: 'method_not_found' },
    ) as never);
    const handler = registration()[2];

    await expect(handler({
      instanceId: 10,
      componentName: 'Probe',
      eventFieldName: 'event',
      listenerInstanceId: 20,
      methodName: 'Missing',
    })).rejects.toMatchObject({
      message: "Method 'Missing' was not found",
      details: { unityErrorType: 'method_not_found' },
    });
  });
});
