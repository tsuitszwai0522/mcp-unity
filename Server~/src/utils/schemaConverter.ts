import * as z from 'zod';

/**
 * Convert a JSON Schema object to a Zod raw shape for MCP SDK registration.
 * Supports basic types: string (with enum), number, integer, boolean, array, object.
 * Complex/nested schemas fall back to z.any() — Unity C# side does the real validation.
 */
/**
 * Resolve the Zod type for array items based on JSON Schema `items` definition.
 * Uses coerce variants so string-encoded values ("10" → 10) are accepted.
 */
function resolveItemType(items: any): z.ZodTypeAny {
  if (!items?.type) return z.any();
  switch (items.type) {
    case 'string':  return z.string();
    case 'integer': return z.coerce.number().int();
    case 'number':  return z.coerce.number();
    case 'boolean': return z.coerce.boolean();
    default:        return z.any();
  }
}

export function jsonSchemaToZodShape(schema: any): z.ZodRawShape {
  const shape: z.ZodRawShape = {};

  if (!schema?.properties || typeof schema.properties !== 'object') {
    return shape;
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
        zodType = z.coerce.number().int();
        break;
      case 'number':
        zodType = z.coerce.number();
        break;
      case 'boolean':
        zodType = z.coerce.boolean();
        break;
      case 'array':
        zodType = z.array(resolveItemType(prop.items));
        break;
      case 'object':
        zodType = z.record(z.any());
        break;
      default:
        zodType = z.any();
        break;
    }

    if (prop.description) {
      zodType = zodType.describe(prop.description);
    }

    if (!required.has(key)) {
      zodType = zodType.optional();
    }

    shape[key] = zodType;
  }

  return shape;
}
