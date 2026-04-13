import * as z from 'zod';
import { Logger } from '../utils/logger.js';
import { McpUnity } from '../unity/mcpUnity.js';
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { McpUnityError, ErrorType } from '../utils/errors.js';
import { CallToolResult } from '@modelcontextprotocol/sdk/types.js';

// Common helper to wrap registration boilerplate
function wrap(
  server: McpServer,
  mcpUnity: McpUnity,
  logger: Logger,
  name: string,
  description: string,
  shape: z.ZodRawShape,
  handler: (params: any) => Promise<CallToolResult>,
) {
  logger.info(`Registering tool: ${name}`);
  server.tool(name, description, shape, async (params: any) => {
    try {
      logger.info(`Executing tool: ${name}`, params);
      const result = await handler(params);
      logger.info(`Tool execution successful: ${name}`);
      return result;
    } catch (error) {
      logger.error(`Tool execution failed: ${name}`, error);
      throw error;
    }
  });
}

function ensureSuccess(response: any, fallbackMessage: string) {
  if (!response || response.success !== true) {
    throw new McpUnityError(
      ErrorType.TOOL_EXECUTION,
      response?.message || response?.error?.message || fallbackMessage,
    );
  }
}

// ============================================================================
// loc_list_tables
// ============================================================================

export function registerLocListTablesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_list_tables';
  const description = 'Lists all Unity Localization StringTable collections with their locales and entry counts';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to list StringTables');

    const tables: Array<{ name: string; locales: string[]; entryCount: number }> = response.tables || [];
    const text = tables.length === 0
      ? 'No StringTable collections found.'
      : tables.map((t) => `- ${t.name}  locales=[${t.locales.join(', ')}]  entries=${t.entryCount}`).join('\n');

    return {
      content: [{ type: 'text', text }],
      data: { tables },
    };
  });
}

// ============================================================================
// loc_get_entries
// ============================================================================

export function registerLocGetEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_get_entries';
  const description = 'Reads key/value entries from a Unity Localization StringTable, with optional key-prefix filter';
  const schema = z.object({
    table_name: z.string().describe('StringTable collection name (e.g. "CB_Tooltip")'),
    locale: z.string().optional().describe('Locale code (default "zh-TW")'),
    filter: z.string().optional().describe('Optional key-prefix filter (e.g. "cb_ext_")'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.table_name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'table_name' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to read StringTable entries');

    const entries: Array<{ key: string; value: string }> = response.entries || [];
    return {
      content: [{ type: 'text', text: response.message || `Read ${entries.length} entries` }],
      data: {
        table: response.table,
        locale: response.locale,
        entries,
      },
    };
  });
}

// ============================================================================
// loc_set_entry
// ============================================================================

export function registerLocSetEntryTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_set_entry';
  const description = 'Sets a Unity Localization StringTable entry value. Creates the key if it does not exist. Supports TMP RichText (e.g. <color=#88CCFF>...</color>). For batches of >5 entries, prefer loc_set_entries (single SaveAssets at the end).';
  const schema = z.object({
    table_name: z.string().describe('StringTable collection name'),
    locale: z.string().optional().describe('Locale code (default "zh-TW")'),
    key: z.string().describe('Entry key'),
    value: z.string().describe('Entry value (supports TMP RichText)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.table_name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'table_name' must be provided");
    }
    if (!params.key) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'key' must be provided");
    }
    if (params.value === undefined || params.value === null) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'value' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set entry');

    return {
      content: [{ type: 'text', text: response.message || 'Entry set' }],
      data: {
        action: response.action,
        key: response.key,
        value: response.value,
      },
    };
  });
}

// ============================================================================
// loc_set_entries
// ============================================================================

export function registerLocSetEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_set_entries';
  const description = 'Batch sets multiple Unity Localization StringTable entries in one operation. Saves once at the end.';
  const schema = z.object({
    table_name: z.string().describe('StringTable collection name'),
    locale: z.string().optional().describe('Locale code (default "zh-TW")'),
    entries: z
      .array(
        z.object({
          key: z.string().describe('Entry key'),
          value: z.string().describe('Entry value'),
        }),
      )
      .min(1)
      .describe('Array of {key, value} entries'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.table_name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'table_name' must be provided");
    }
    if (!Array.isArray(params.entries) || params.entries.length === 0) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'entries' must be a non-empty array");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set entries');

    return {
      content: [{ type: 'text', text: response.message || 'Entries set' }],
      data: {
        created: response.created,
        updated: response.updated,
        total: response.total,
      },
    };
  });
}

// ============================================================================
// loc_delete_entry
// ============================================================================

export function registerLocDeleteEntryTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_delete_entry';
  const description = 'Deletes a Unity Localization entry key from a StringTable collection (affects all locales)';
  const schema = z.object({
    table_name: z.string().describe('StringTable collection name'),
    key: z.string().describe('Entry key to delete'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.table_name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'table_name' must be provided");
    }
    if (!params.key) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'key' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to delete entry');

    return {
      content: [{ type: 'text', text: response.message || 'Entry deleted' }],
      data: {
        deleted: response.deleted,
        key: response.key,
      },
    };
  });
}

// ============================================================================
// loc_create_table
// ============================================================================

export function registerLocCreateTableTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_create_table';
  const description = 'Creates a new Unity Localization StringTable collection. Locales must already be configured in Localization Settings — use loc_add_locale to bootstrap missing locales first.';
  const schema = z.object({
    table_name: z.string().describe('New StringTable collection name'),
    locales: z.array(z.string()).optional().describe('Locale codes to include (default ["zh-TW"])'),
    directory: z.string().optional().describe('Asset directory to save into (default "Assets/Localization/Tables")'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.table_name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'table_name' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to create StringTable');

    let text = response.message || 'StringTable created';
    if (Array.isArray(response.warnings) && response.warnings.length > 0) {
      text += `\nWarnings:\n  - ${response.warnings.join('\n  - ')}`;
    }

    return {
      content: [{ type: 'text', text }],
      data: {
        created: response.created,
        name: response.name,
        path: response.path,
        warnings: response.warnings,
      },
    };
  });
}

// ============================================================================
// loc_add_locale
// ============================================================================

export function registerLocAddLocaleTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'loc_add_locale';
  const description = "Registers a Locale (by code, e.g. 'zh-TW') with Unity Localization. Creates the Locale asset if missing. Use this to bootstrap a fresh project before loc_create_table.";
  const schema = z.object({
    code: z.string().describe("Locale identifier code (e.g. 'zh-TW', 'en', 'ja')"),
    directory: z.string().optional().describe('Asset directory for the Locale asset (default "Assets/Localization/Locales")'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.code) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'code' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to add locale');

    return {
      content: [{ type: 'text', text: response.message || `Locale '${params.code}' registered` }],
      data: {
        action: response.action,
        code: response.code,
        path: response.path,
      },
    };
  });
}
