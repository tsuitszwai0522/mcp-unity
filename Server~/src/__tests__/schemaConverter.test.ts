import { describe, expect, it, jest } from '@jest/globals';
import * as z from 'zod';
import { registerAddrListEntriesTool } from '../tools/addressablesTools.js';
import { jsonSchemaToZodShape } from '../utils/schemaConverter.js';

const requiredProperty = (type: 'number' | 'integer' | 'boolean') =>
  jsonSchemaToZodShape({
    type: 'object',
    properties: { value: { type } },
    required: ['value'],
  }).value;

const arrayProperty = (type: 'number' | 'integer' | 'boolean') =>
  jsonSchemaToZodShape({
    type: 'object',
    properties: { values: { type: 'array', items: { type } } },
    required: ['values'],
  }).values;

const schemaBytes = (schema: z.ZodType): string => JSON.stringify(z.toJSONSchema(schema));

describe('safe JSON-schema coercion', () => {
  it('accepts only numbers and pure numeric strings for number fields', () => {
    const schema = requiredProperty('number');
    for (const [input, output] of [
      [42, 42],
      [3.5, 3.5],
      ['42', 42],
      ['3.5', 3.5],
      [' 7 ', 7],
    ] as const) {
      expect(schema.safeParse(input)).toEqual({ success: true, data: output });
    }

    for (const input of ['', null, true, false, [], {}, 'abc']) {
      expect(schema.safeParse(input).success).toBe(false);
    }
  });

  it('accepts only booleans and case-insensitive true/false strings for boolean fields', () => {
    const schema = requiredProperty('boolean');
    for (const [input, output] of [
      [true, true],
      [false, false],
      ['true', true],
      ['false', false],
      ['TRUE', true],
      ['False', false],
      [' true ', true],
      ['\tfalse\n', false],
      [' True ', true],
    ] as const) {
      expect(schema.safeParse(input)).toEqual({ success: true, data: output });
    }

    for (const input of ['', '0', '1', 'no', 'yes', 0, 1, 42, null, [], {}]) {
      expect(schema.safeParse(input).success).toBe(false);
    }
  });

  it('preserves undefined so optional number and boolean properties remain optional', () => {
    const shape = jsonSchemaToZodShape({
      type: 'object',
      properties: {
        numberValue: { type: 'number' },
        booleanValue: { type: 'boolean' },
      },
    });

    expect(shape.numberValue.safeParse(undefined)).toEqual({ success: true, data: undefined });
    expect(shape.booleanValue.safeParse(undefined)).toEqual({ success: true, data: undefined });
  });

  it.each([
    ['number', '3.5', 3.5],
    ['integer', '42', 42],
    ['boolean', 'FALSE', false],
  ] as const)('uses the same guarded conversion for %s array items', (type, input, output) => {
    const schema = arrayProperty(type);

    expect(schema.safeParse([input])).toEqual({ success: true, data: [output] });
    expect(schema.safeParse([null]).success).toBe(false);
  });

  it('keeps integer validation after guarded string conversion', () => {
    const schema = requiredProperty('integer');

    expect(schema.safeParse('42')).toEqual({ success: true, data: 42 });
    expect(schema.safeParse('3.5').success).toBe(false);
    for (const input of ['', null, true, false, [], {}, 'abc']) {
      expect(schema.safeParse(input).success).toBe(false);
    }
  });

  it('applies the guarded integer conversion to addr_list_entries limit', () => {
    const tool = jest.fn();
    registerAddrListEntriesTool(
      { tool } as any,
      { sendRequest: jest.fn() } as any,
      { info: jest.fn(), debug: jest.fn(), warn: jest.fn(), error: jest.fn() } as any,
    );
    const limit = (tool.mock.calls[0][2] as z.ZodRawShape).limit;

    expect(limit.safeParse(' 42 ')).toEqual({ success: true, data: 42 });
    for (const input of ['', null, true, false, [], {}, 'abc', '3.5']) {
      expect(limit.safeParse(input).success).toBe(false);
    }
    expect(limit.safeParse(undefined)).toEqual({ success: true, data: undefined });
  });
});

describe('safe coercion JSON Schema compatibility', () => {
  it('keeps number, integer, and boolean schemas byte-identical to z.coerce output', () => {
    expect(schemaBytes(requiredProperty('number'))).toBe(schemaBytes(z.coerce.number()));
    expect(schemaBytes(requiredProperty('integer'))).toBe(schemaBytes(z.coerce.number().int()));
    expect(schemaBytes(requiredProperty('boolean'))).toBe(schemaBytes(z.coerce.boolean()));
  });

  it('keeps the addr_list_entries limit schema byte-identical to its prior schema', () => {
    const tool = jest.fn();
    registerAddrListEntriesTool(
      { tool } as any,
      { sendRequest: jest.fn() } as any,
      { info: jest.fn(), debug: jest.fn(), warn: jest.fn(), error: jest.fn() } as any,
    );
    const limit = (tool.mock.calls[0][2] as z.ZodRawShape).limit;
    const previous = z.coerce.number().int().optional()
      .describe('Max entries to return (default 200)');

    expect(schemaBytes(limit)).toBe(schemaBytes(previous));
  });
});
