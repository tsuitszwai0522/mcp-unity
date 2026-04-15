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
// addr_get_settings
// ============================================================================

export function registerAddrGetSettingsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_get_settings';
  const description = 'Query Unity Addressables settings state (initialized flag, default group, active profile, labels)';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to query Addressables settings');

    const text = response.message || 'Addressables settings fetched';
    return {
      content: [{ type: 'text', text }],
      data: {
        initialized: response.initialized,
        defaultGroup: response.defaultGroup,
        activeProfile: response.activeProfile,
        profileVariables: response.profileVariables,
        groupCount: response.groupCount,
        entryCount: response.entryCount,
        labels: response.labels,
        version: response.version,
      },
    };
  });
}

// ============================================================================
// addr_init_settings
// ============================================================================

export function registerAddrInitSettingsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_init_settings';
  const description = 'Initialize Unity Addressables (creates default settings asset and group). Safe to call when already initialized.';
  const schema = z.object({
    folder: z.string().optional().describe('Settings folder path (default "Assets/AddressableAssetsData")'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to initialize Addressables');

    return {
      content: [{ type: 'text', text: response.message || 'Addressables initialized' }],
      data: {
        created: response.created,
        settingsPath: response.settingsPath,
        defaultGroup: response.defaultGroup,
      },
    };
  });
}

// ============================================================================
// addr_list_groups
// ============================================================================

export function registerAddrListGroupsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_list_groups';
  const description = 'List all Unity Addressables groups with entry counts and schemas';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to list Addressables groups');

    const groups: Array<any> = response.groups || [];
    const text = groups.length === 0
      ? 'No Addressables groups found.'
      : groups.map((g) => `- ${g.name}${g.isDefault ? ' (default)' : ''}  entries=${g.entryCount}  schemas=[${(g.schemas || []).join(', ')}]`).join('\n');

    return {
      content: [{ type: 'text', text }],
      data: { groups },
    };
  });
}

// ============================================================================
// addr_create_group
// ============================================================================

export function registerAddrCreateGroupTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_create_group';
  const description = 'Create a new Unity Addressables group with default Bundled + ContentUpdate schemas';
  const schema = z.object({
    name: z.string().describe('Group name (must be unique)'),
    set_as_default: z.boolean().optional().describe('Set as default group (default false)'),
    packed_mode: z
      .enum(['PackTogether', 'PackSeparately', 'PackTogetherByLabel'])
      .optional()
      .describe('Bundle packing mode (default PackTogether)'),
    include_in_build: z.boolean().optional().describe('Include this group in the build (default true)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'name' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to create group');

    return {
      content: [{ type: 'text', text: response.message || 'Group created' }],
      data: {
        created: response.created,
        name: response.name,
        isDefault: response.isDefault,
      },
    };
  });
}

// ============================================================================
// addr_remove_group
// ============================================================================

export function registerAddrRemoveGroupTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_remove_group';
  const description = 'Remove a Unity Addressables group. Refuses to delete default group or non-empty groups unless force=true.';
  const schema = z.object({
    name: z.string().describe('Group name'),
    force: z.boolean().optional().describe('Force delete even if the group has entries (default false)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'name' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to remove group');

    return {
      content: [{ type: 'text', text: response.message || 'Group removed' }],
      data: {
        deleted: response.deleted,
        name: response.name,
        removedEntryCount: response.removedEntryCount,
      },
    };
  });
}

// ============================================================================
// addr_set_default_group
// ============================================================================

export function registerAddrSetDefaultGroupTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_set_default_group';
  const description = 'Set the Unity Addressables default group';
  const schema = z.object({
    name: z.string().describe('Group name'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.name) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'name' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set default group');

    return {
      content: [{ type: 'text', text: response.message || 'Default group updated' }],
      data: {
        defaultGroup: response.defaultGroup,
        previousDefault: response.previousDefault,
      },
    };
  });
}

// ============================================================================
// addr_list_entries
// ============================================================================

export function registerAddrListEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_list_entries';
  const description = 'List Unity Addressables entries with optional group/label/address/path filters';
  const schema = z.object({
    group: z.string().optional().describe('Only list entries in this group'),
    label_filter: z.string().optional().describe('Only list entries containing this label'),
    address_pattern: z.string().optional().describe('Glob-style pattern on entry address (supports *)'),
    asset_path_prefix: z.string().optional().describe('Only list entries whose assetPath starts with this prefix'),
    limit: z.coerce.number().int().optional().describe('Max entries to return (default 200)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to list entries');

    const entries: Array<any> = response.entries || [];
    const header = `Total matched=${response.total}  returned=${entries.length}${response.truncated ? ' (truncated)' : ''}`;
    const lines = entries.map((e) => `- [${e.group}] ${e.address}  ${e.assetPath}  labels=[${(e.labels || []).join(', ')}]`);
    const text = [header, ...lines].join('\n');

    return {
      content: [{ type: 'text', text }],
      data: {
        total: response.total,
        truncated: response.truncated,
        entries,
      },
    };
  });
}

// ============================================================================
// addr_add_entries
// ============================================================================

export function registerAddrAddEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_add_entries';
  const description = 'Batch-add Unity Addressables entries to a group with optional address/labels per asset. Saves once at the end. Defaults to strict mode: missing assets abort the batch with not_found.';
  const schema = z.object({
    group: z.string().describe('Target group name (must exist)'),
    assets: z
      .array(
        z.object({
          asset_path: z.string().describe('Asset path (e.g. Assets/Prefabs/Foo.prefab)'),
          address: z.string().optional().describe('Address override (default = asset path)'),
          labels: z.array(z.string()).optional().describe('Labels to apply (auto-created if missing)'),
        }),
      )
      .min(1)
      .describe('Assets to add'),
    fail_on_missing_asset: z
      .boolean()
      .optional()
      .describe('Default true: abort the whole call with not_found if any asset_path does not resolve. Set false for best-effort batches.'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.group) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'group' must be provided");
    }
    if (!Array.isArray(params.assets) || params.assets.length === 0) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'assets' must be a non-empty array");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to add entries');

    let text = response.message || 'Entries added';
    if (Array.isArray(response.warnings) && response.warnings.length > 0) {
      text += `\nWarnings:\n  - ${response.warnings.join('\n  - ')}`;
    }

    return {
      content: [{ type: 'text', text }],
      data: {
        added: response.added,
        skipped: response.skipped,
        entries: response.entries,
        warnings: response.warnings,
        missingAssets: response.missingAssets,
      },
    };
  });
}

// ============================================================================
// addr_remove_entries
// ============================================================================

export function registerAddrRemoveEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_remove_entries';
  const description = 'Batch-remove Unity Addressables entries (identified by guid or asset_path)';
  const schema = z.object({
    entries: z
      .array(
        z.object({
          guid: z.string().optional(),
          asset_path: z.string().optional(),
        }),
      )
      .min(1)
      .describe('Entries to remove'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!Array.isArray(params.entries) || params.entries.length === 0) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'entries' must be a non-empty array");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to remove entries');

    return {
      content: [{ type: 'text', text: response.message || 'Entries removed' }],
      data: {
        removed: response.removed,
        notFound: response.notFound,
      },
    };
  });
}

// ============================================================================
// addr_move_entries
// ============================================================================

export function registerAddrMoveEntriesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_move_entries';
  const description = 'Batch-move Unity Addressables entries into a different group';
  const schema = z.object({
    target_group: z.string().describe('Destination group name (must exist)'),
    entries: z
      .array(
        z.object({
          guid: z.string().optional(),
          asset_path: z.string().optional(),
        }),
      )
      .min(1)
      .describe('Entries to move'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.target_group) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'target_group' must be provided");
    }
    if (!Array.isArray(params.entries) || params.entries.length === 0) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'entries' must be a non-empty array");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to move entries');

    return {
      content: [{ type: 'text', text: response.message || 'Entries moved' }],
      data: {
        moved: response.moved,
        targetGroup: response.targetGroup,
        notFound: response.notFound,
      },
    };
  });
}

// ============================================================================
// addr_set_entry
// ============================================================================

export function registerAddrSetEntryTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_set_entry';
  const description = 'Update a single Unity Addressables entry — change address and/or add/remove labels (partial update). Identify by guid or asset_path.';
  const schema = z.object({
    guid: z.string().optional().describe('Entry guid (or asset_path)'),
    asset_path: z.string().optional().describe('Asset path (or guid)'),
    new_address: z.string().optional().describe('New address'),
    add_labels: z.array(z.string()).optional().describe('Labels to add (auto-created if missing)'),
    remove_labels: z.array(z.string()).optional().describe('Labels to remove'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.guid && !params.asset_path) {
      throw new McpUnityError(ErrorType.VALIDATION, "Either 'guid' or 'asset_path' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to update entry');

    let text = response.message || 'Entry updated';
    if (Array.isArray(response.warnings) && response.warnings.length > 0) {
      text += `\nWarnings:\n  - ${response.warnings.join('\n  - ')}`;
    }

    return {
      content: [{ type: 'text', text }],
      data: {
        guid: response.guid,
        assetPath: response.assetPath,
        address: response.address,
        labels: response.labels,
        group: response.group,
        warnings: response.warnings,
      },
    };
  });
}

// ============================================================================
// addr_list_labels
// ============================================================================

export function registerAddrListLabelsTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_list_labels';
  const description = 'List all Unity Addressables labels';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to list labels');

    const labels: string[] = response.labels || [];
    const text = labels.length === 0 ? 'No labels found.' : `Labels: ${labels.join(', ')}`;

    return {
      content: [{ type: 'text', text }],
      data: { labels },
    };
  });
}

// ============================================================================
// addr_create_label
// ============================================================================

export function registerAddrCreateLabelTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_create_label';
  const description = 'Register a new Unity Addressables label (idempotent). Labels may not contain spaces or brackets.';
  const schema = z.object({
    label: z.string().describe('Label name (no spaces or brackets)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.label) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'label' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to create label');

    return {
      content: [{ type: 'text', text: response.message || 'Label created' }],
      data: {
        created: response.created,
        label: response.label,
      },
    };
  });
}

// ============================================================================
// addr_remove_label
// ============================================================================

export function registerAddrRemoveLabelTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_remove_label';
  const description = 'Remove a Unity Addressables label. Refuses when still in use unless force=true (which also strips it from affected entries).';
  const schema = z.object({
    label: z.string().describe('Label name'),
    force: z.boolean().optional().describe('Strip the label from entries before removal (default false)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.label) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'label' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to remove label');

    return {
      content: [{ type: 'text', text: response.message || 'Label removed' }],
      data: {
        deleted: response.deleted,
        label: response.label,
        affectedEntries: response.affectedEntries,
      },
    };
  });
}

// ============================================================================
// addr_get_group_schema
// ============================================================================

export function registerAddrGetGroupSchemaTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_get_group_schema';
  const description = 'Read the current BundledAssetGroupSchema values for an Addressables group';
  const schema = z.object({
    group: z.string().describe('Group name (must exist)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.group) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'group' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to get group schema');

    const values = response.values || {};
    const lines = [
      `Group '${response.group}' BundledAssetGroupSchema:`,
      ...Object.entries(values).map(([k, v]) => `  ${k}: ${v}`),
    ];
    return {
      content: [{ type: 'text', text: lines.join('\n') }],
      data: {
        group: response.group,
        values,
      },
    };
  });
}

// ============================================================================
// addr_set_group_schema
// ============================================================================

export function registerAddrSetGroupSchemaTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_set_group_schema';
  const description =
    "Partial update of an Addressables group's BundledAssetGroupSchema — only provided values change. Supports dry_run. Fields: compression, include_in_build, packed_mode, bundle_naming, use_asset_bundle_cache, use_unitywebrequest_for_local_bundles, retry_count, timeout, build_path (profile variable name), load_path (profile variable name).";
  const schema = z.object({
    group: z.string().describe('Group name (must exist)'),
    dry_run: z.boolean().optional().describe('If true, return the diff without saving (default false)'),
    values: z
      .object({
        compression: z.enum(['Uncompressed', 'LZ4', 'LZMA']).optional(),
        include_in_build: z.boolean().optional(),
        packed_mode: z.enum(['PackTogether', 'PackSeparately', 'PackTogetherByLabel']).optional(),
        bundle_naming: z.enum(['AppendHash', 'NoHash', 'OnlyHash', 'FileNameHash']).optional(),
        use_asset_bundle_cache: z.boolean().optional(),
        use_unitywebrequest_for_local_bundles: z.boolean().optional(),
        retry_count: z.number().int().optional(),
        timeout: z.number().int().optional(),
        build_path: z
          .string()
          .optional()
          .describe('Profile variable name, e.g. Local.BuildPath or Remote.BuildPath'),
        load_path: z
          .string()
          .optional()
          .describe('Profile variable name, e.g. Local.LoadPath or Remote.LoadPath'),
      })
      .describe('Partial set of schema fields to apply'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.group) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'group' must be provided");
    }
    if (!params.values || Object.keys(params.values).length === 0) {
      throw new McpUnityError(ErrorType.VALIDATION, "Parameter 'values' must be a non-empty object");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set group schema');

    return {
      content: [{ type: 'text', text: response.message || 'Group schema updated' }],
      data: {
        group: response.group,
        dryRun: response.dryRun,
        changed: response.changed,
        diff: response.diff,
      },
    };
  });
}

// ============================================================================
// addr_list_profiles
// ============================================================================

export function registerAddrListProfilesTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_list_profiles';
  const description = 'List all Unity Addressables profiles with their variable values';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to list profiles');

    const profiles: Array<any> = response.profiles || [];
    const text = profiles.length === 0
      ? 'No Addressables profiles found.'
      : profiles.map((p) => `- ${p.name}${p.isActive ? ' (active)' : ''}`).join('\n');

    return {
      content: [{ type: 'text', text }],
      data: {
        activeProfile: response.activeProfile,
        activeProfileId: response.activeProfileId,
        variableNames: response.variableNames,
        profiles,
      },
    };
  });
}

// ============================================================================
// addr_get_active_profile
// ============================================================================

export function registerAddrGetActiveProfileTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_get_active_profile';
  const description = 'Get the currently active Unity Addressables profile with its resolved variable values';
  const schema = z.object({});

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (_params) => {
    const response = await mcpUnity.sendRequest({ method: name, params: {} });
    ensureSuccess(response, 'Failed to get active profile');

    return {
      content: [{ type: 'text', text: response.message || `Active profile: ${response.name}` }],
      data: {
        id: response.id,
        name: response.name,
        variables: response.variables,
      },
    };
  });
}

// ============================================================================
// addr_set_active_profile
// ============================================================================

export function registerAddrSetActiveProfileTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_set_active_profile';
  const description = 'Switch the active Unity Addressables profile by name';
  const schema = z.object({
    profile: z.string().describe('Profile name (must exist)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.profile) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'profile' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set active profile');

    return {
      content: [{ type: 'text', text: response.message || 'Active profile updated' }],
      data: {
        changed: response.changed,
        activeProfile: response.activeProfile,
        previousProfile: response.previousProfile,
      },
    };
  });
}

// ============================================================================
// addr_set_profile_variable
// ============================================================================

export function registerAddrSetProfileVariableTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_set_profile_variable';
  const description =
    'Set an Addressables profile variable (e.g. Remote.LoadPath) on a named profile. Pass create_if_missing=true to create the variable at the profile-settings level; newly-created variables are added to ALL profiles, not only the named profile.';
  const schema = z.object({
    profile: z.string().describe('Profile name (must exist)'),
    variable: z.string().describe('Variable name (e.g. Remote.LoadPath)'),
    value: z.string().describe('New value (may contain [BuildTarget] tokens)'),
    create_if_missing: z
      .boolean()
      .optional()
      .describe(
        'Create the variable at profile-settings level if missing. WARNING: newly-created variables are added to ALL profiles, not only the named profile. Default false.'
      ),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.profile) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'profile' must be provided");
    }
    if (!params.variable) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'variable' must be provided");
    }
    if (params.value === undefined || params.value === null) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'value' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to set profile variable');

    return {
      content: [{ type: 'text', text: response.message || 'Profile variable updated' }],
      data: {
        profile: response.profile,
        variable: response.variable,
        previousValue: response.previousValue,
        value: response.value,
        created: response.created,
      },
    };
  });
}

// ============================================================================
// addr_find_asset
// ============================================================================

export function registerAddrFindAssetTool(server: McpServer, mcpUnity: McpUnity, logger: Logger) {
  const name = 'addr_find_asset';
  const description = 'Look up a Unity Addressables entry by asset path — returns group, address, labels';
  const schema = z.object({
    asset_path: z.string().describe('Asset path (e.g. Assets/Prefabs/Foo.prefab)'),
  });

  wrap(server, mcpUnity, logger, name, description, schema.shape, async (params) => {
    if (!params.asset_path) {
      throw new McpUnityError(ErrorType.VALIDATION, "Required parameter 'asset_path' must be provided");
    }

    const response = await mcpUnity.sendRequest({ method: name, params });
    ensureSuccess(response, 'Failed to look up asset');

    return {
      content: [{ type: 'text', text: response.message || (response.found ? 'Entry found' : 'Entry not found') }],
      data: {
        found: response.found,
        entry: response.entry,
      },
    };
  });
}
