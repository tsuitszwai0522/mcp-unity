import { beforeEach, describe, expect, it, jest } from '@jest/globals';
import {
  registerReadSerializedFieldsTool,
  registerWriteSerializedFieldsTool,
} from '../tools/serializedFieldTools.js';

const mockMcpUnity = { sendRequest: jest.fn() } as any;
const mockLogger = {
  info: jest.fn(),
  debug: jest.fn(),
  warn: jest.fn(),
  error: jest.fn(),
} as any;
const mockServerTool = jest.fn();
const mockServer = { tool: mockServerTool } as any;

describe('serialized field tool descriptions', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('discloses read enum value and index semantics', () => {
    registerReadSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('value is the underlying enum value');
    expect(description).toContain('index is the enumValueIndex');
  });

  it('discloses write enum and partial-struct semantics', () => {
    registerWriteSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('underlying enum value (not an index)');
    expect(description).toContain('invalid values are rejected with the valid names listed');
    expect(description).toContain('Partial struct writes');
    expect(description).toContain("unmentioned components are the type's default");
  });

  it('discloses array, Generic merge, Array.size, and persistent-call write semantics', () => {
    registerWriteSerializedFieldsTool(mockServer, mockMcpUnity, mockLogger);

    const description = (mockServerTool.mock.calls[0] as any)[1] as string;
    expect(description).toContain('JArray and replace the whole collection');
    expect(description).toContain('shrink discards removed elements with a warning');
    expect(description).toContain('grown elements start from type defaults');
    expect(description).toContain('nested JObject values are partial merges');
    expect(description).toContain('Direct Array.size writes');
    expect(description).toContain('0 through 10000');
    expect(description).toContain("check arrayMetadata before writing it back");
    expect(description).toContain('rollback preserves and reports conflicting current references');
    expect(description).toContain('Non-reference children and array-size changes may remain applied');
    expect(description).toContain('including null clears and affected array resizes');
    expect(description).toContain('not an ownership lock or ABA protection');
    expect(description).toContain('AnimationCurve, Gradient');
    expect(description).toContain('Direct m_PersistentCalls writes');
    expect(description).toContain('prefer wire_unity_event');
  });
});
