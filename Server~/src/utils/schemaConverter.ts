import * as z from 'zod';

const NUMERIC_STRING = /^[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eE][+-]?\d+)?$/;

const preprocessNumber = (value: unknown): unknown => {
  if (typeof value !== 'string') return value;
  const trimmed = value.trim();
  return trimmed !== '' && NUMERIC_STRING.test(trimmed) ? Number(trimmed) : value;
};

const preprocessBoolean = (value: unknown): unknown => {
  if (typeof value !== 'string') return value;
  const normalized = value.trim().toLowerCase();
  if (normalized === 'true') return true;
  if (normalized === 'false') return false;
  return value;
};

const numberSchema = (): z.ZodTypeAny => z.preprocess(preprocessNumber, z.number());
const integerSchema = (): z.ZodTypeAny => z.preprocess(preprocessNumber, z.number().int());
const booleanSchema = (): z.ZodTypeAny => z.preprocess(preprocessBoolean, z.boolean());

/**
 * Convert a JSON Schema object to a Zod raw shape for MCP SDK registration.
 * Supports basic types: string (with enum), number, integer, boolean, array, object.
 * Complex/nested schemas fall back to z.any() — Unity C# side does the real validation.
 */
/**
 * Resolve the Zod type for array items based on JSON Schema `items` definition.
 * Accepts string-encoded number and boolean values without coercing unrelated JSON values.
 */
function resolveItemType(items: any): z.ZodTypeAny {
  if (!items?.type) return z.any();
  switch (items.type) {
    case 'string':  return z.string();
    case 'integer': return integerSchema();
    case 'number':  return numberSchema();
    case 'boolean': return booleanSchema();
    default:        return z.any();
  }
}

export function jsonSchemaToZodShape(schema: any): z.ZodRawShape {
  const shape: Record<string, z.ZodTypeAny> = {};

  if (!schema?.properties || typeof schema.properties !== 'object') {
    return shape as z.ZodRawShape;
  }

  const required = new Set<string>(Array.isArray(schema.required) ? schema.required : []);

  for (const [key, prop] of Object.entries<any>(schema.properties)) {
    let zodType: z.ZodTypeAny;

    switch (prop.type) {
      case 'string':
        if (Array.isArray(prop.enum) && prop.enum.length > 0) {
          zodType = z.enum(prop.enum as [string, ...string[]]);
        } else {
          zodType = z.string();
        }
        break;
      case 'integer':
        zodType = integerSchema();
        break;
      case 'number':
        zodType = numberSchema();
        break;
      case 'boolean':
        zodType = booleanSchema();
        break;
      case 'array':
        zodType = z.array(resolveItemType(prop.items));
        break;
      case 'object':
        zodType = z.record(z.string(), z.any());
        break;
      default:
        zodType = z.any();
        break;
    }

    if (prop.description) {
      zodType = zodType.describe(prop.description);
    }

    zodType = required.has(key)
      ? zodType.nonoptional()
      : zodType.optional();

    shape[key] = zodType;
  }

  return shape as z.ZodRawShape;
}
