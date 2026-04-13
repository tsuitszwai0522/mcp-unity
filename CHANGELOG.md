# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.11.0] - 2026-04-13

### Added

- **`loc_delete_table`** — symmetric counterpart to `loc_create_table`. Deletes a StringTableCollection along with its SharedTableData and every per-locale StringTable via `AssetDatabase.DeleteAsset`, which fires `LocalizationAssetModificationProcessor` for proper cleanup. Returns the deleted collection's name, path, entry count, and locale list.
- **`loc_remove_locale`** — symmetric counterpart to `loc_add_locale`. Unregisters a Locale via `LocalizationEditorSettings.RemoveLocale` and (by default) deletes the underlying asset. Optional `delete_asset: false` keeps the file on disk.
- **`[McpUnityFirstParty]` attribute** (`Editor/Tools/McpUnityFirstPartyAttribute.cs`) — explicit marker for first-party tools that ship hand-written TS wrappers. `McpUnitySocketHandler.HandleListTools` now excludes attributed tools from dynamic registration, with the existing `McpUnity.*` assembly-name prefix as a fallback. All Localization tools are marked.
- **`LocTableHelper.DeleteStringTableCollection`** — production helper used by both `loc_delete_table` and the test fixture cleanup, ensuring the supported "delete via AssetDatabase" path is exercised in both code paths.
- **CLAUDE.md "Adding a First-Party Optional Package Tool" chapter** — documents the sub-assembly + `versionDefines` pattern, the `[McpUnityFirstParty]` marker, the `McpUnity.*` reserved-prefix invariant, and the consumer-side `testables` requirement for running package tests.
- **`doc/lessons/unity-mcp-lessons.md`** — new lessons file covering Localization gotchas (`Locale.CreateLocale` factory, missing `RemoveCollection` API, collection-level `RemoveEntry`, `CultureInfo` strict-check trap), AssetDatabase pitfalls (`AssetPathToGUID` cache, `Directory.CreateDirectory` vs `AssetDatabase.CreateFolder`), and MCP tooling pitfalls (`run_tests` testables requirement, `recompile_scripts` no-refresh, edit→recompile→run sequencing).
- **`InternalsVisibleTo` for tests** — `McpUnity.Localization` exposes internals to `McpUnity.Localization.Tests` so the test suite can call `LocTableHelper` directly.

### Fixed

- **`loc_delete_entry` orphan leak** — previously called `SharedData.RemoveKey` directly, which left an orphan `StringTableEntry` in every per-locale `.asset` file. `loc_get_entries` hid the orphan because it filters via SharedData, so the bug was invisible to tool-level checks but visible in the YAML and after reimport. Now uses `StringTableCollection.RemoveEntry(key)` — the collection-level API that atomically removes both SharedData and per-locale entries AND raises `RaiseTableEntryRemoved`.
- **`loc_set_entries` lost inner error detail** — batch errors were rewrapped as `"entries[i]: invalid key"`, hiding the original validation reason. Now preserves the inner message: `"entries[i]: Key 'foo ' has leading/trailing whitespace"`.
- **`loc_set_entries` partial in-memory pollution** — a mid-batch validation failure could leave the in-memory `SharedData` half-mutated even though the disk state was clean. Pre-flight validates every entry before any mutation, achieving all-or-nothing semantic. Inline comment marks the invariant for future maintainers.

### Changed

- **`loc_create_table` / `loc_add_locale` directory handling** — both tools now route through new `LocTableHelper` helpers:
  - `ValidateAssetPath` rejects paths outside `Assets/` (was silently accepted, with undefined behaviour)
  - `EnsureFolderExists` walks the path via `AssetDatabase.CreateFolder` instead of `Directory.CreateDirectory` + `AssetDatabase.Refresh`, atomically writing `.meta` files
  - `FindLocale` unifies locale lookup by `Identifier.Code` instead of struct-equality on `LocaleIdentifier`
- **`loc_add_locale` invalid-culture handling** — was a hard error if `Locale.CreateLocale` returned null. Now soft-warns when `CultureInfo.GetCultureInfo` and the IETF-tag fallback both fail, but still creates the Locale (Unity Localization accepts identifiers like `zh-Hant` that .NET does not recognise on some runtimes).
- **`loc_set_entry` description** — now nudges callers toward `loc_set_entries` for batches of >5 entries (saves 100x reimports vs single-entry loops).

### Tests

- **40 EditMode tests pass** (`McpUnity.Tests.Localization.LocTests`):
  - 12 original scenario tests now actually run for the first time (latent `LocalizationEditorSettings.RemoveCollection` bug + missing `testables` had been silently dropping the assembly)
  - 16 refactor coverage tests for B1/B2/B3 + C1/C2/C3 with regression-locking assertions (orphan probe via `StringTable.GetEntry(keyId)`, multi-locale variant, all-or-nothing pre-flight, soft-warning vs hard-reject)
  - 7 D4 tests for `loc_delete_table` and `loc_remove_locale` happy + error paths, including `delete_asset: false`
  - 1 idempotent dangling-locale cleanup test
- **`testables` requirement documented** — running these tests requires the consumer project's `Packages/manifest.json` to include `"testables": ["com.gamelovers.mcp-unity"]`.

## [1.10.0] - 2026-04-13

### Added

- **Unity Addressables tool suite** — 15 new tools for managing the Addressables system without leaving the MCP client. Covers the four most common workflows (setup, group management, entry management, label management) and a direct lookup query:
  - `addr_get_settings` — query initialized flag, default group, active profile, profile variables, labels, group/entry counts
  - `addr_init_settings` — bootstrap AddressableAssetSettings (equivalent to the "Create Addressables Settings" button); idempotent
  - `addr_list_groups` — list all groups with entry counts and attached schemas
  - `addr_create_group` — create a new group with default Bundled + ContentUpdate schemas; configurable `packed_mode`, `include_in_build`, `set_as_default`
  - `addr_remove_group` — remove a group; refuses to delete the default group or non-empty groups unless `force=true`
  - `addr_set_default_group` — switch the default group
  - `addr_list_entries` — filter entries by group, label, address glob pattern (supports `*`), asset-path prefix; `limit` guard (default 200) with `truncated` flag
  - `addr_add_entries` — batch-add assets to a group with per-asset optional address/labels; auto-creates missing labels with warnings; single save at the end
  - `addr_remove_entries` — batch-remove entries by guid or asset_path
  - `addr_move_entries` — batch-move entries between groups
  - `addr_set_entry` — partial update on a single entry (address, add_labels, remove_labels)
  - `addr_list_labels` / `addr_create_label` / `addr_remove_label` — label management; remove refuses in-use labels unless `force=true`
  - `addr_find_asset` — direct lookup by asset path, returns group/address/labels
- **Optional-package sub-assembly** — Addressables tools live in a dedicated `McpUnity.Addressables` assembly (`Editor/Tools/Addressables/`) gated by `versionDefines` + `defineConstraints: ["MCP_UNITY_ADDRESSABLES"]` on `com.unity.addressables ≥ 1.19.0`. The entire assembly is skipped from compilation when Addressables is not installed — zero impact on projects that do not use it. Node side always registers the 15 tools; calls fall through to `unknown method` when Unity lacks the package.

## [1.9.0] - 2026-04-13

### Added

- **Unity Localization tool suite** — 7 new tools for operating on Unity Localization StringTable collections without leaving the MCP client:
  - `loc_list_tables` — list all StringTable collections with locales and entry counts
  - `loc_get_entries` — read key/value entries with optional key-prefix filter
  - `loc_set_entry` — add or update a single entry (supports TMP RichText)
  - `loc_set_entries` — batch add/update multiple entries in one save
  - `loc_delete_entry` — remove a key from SharedData (affects all locales)
  - `loc_create_table` — create a new StringTable collection; warns and skips locales that are not yet configured in Localization Settings (never auto-creates locales)
  - `loc_add_locale` — explicit project bootstrap helper that creates a `Locale` asset and registers it via `LocalizationEditorSettings.AddLocale`
- **Optional-package sub-assembly pattern** — Localization tools live in a dedicated `McpUnity.Localization` assembly (`Editor/Tools/Localization/`) gated by `versionDefines` + `defineConstraints: ["MCP_UNITY_LOCALIZATION"]`, so the entire assembly is skipped from compilation when `com.unity.localization` is not installed. Zero impact on projects that do not use Unity Localization
- **EditMode NUnit test suite** — `Editor/Tests/Localization/LocTests.cs` with `[OneTimeSetUp]` locale bootstrap, ordered end-to-end scenario, and independent error-path tests, gated by `UNITY_INCLUDE_TESTS + MCP_UNITY_LOCALIZATION`

### Changed

- **`HandleListTools` first-party sub-assembly exclusion** — `McpUnitySocketHandler.HandleListTools` now also excludes tools from any assembly whose name starts with `McpUnity.` (in addition to the main `McpUnity.Editor` assembly). This reserves that namespace for first-party extensions that ship hand-written TypeScript wrappers, preventing the dynamic-registration path from double-registering them

## [1.8.2] - 2026-04-01

### Fixed

- **Schema converter coerce support** — `z.number()` → `z.coerce.number()` for integer/number/boolean types in `schemaConverter.ts`, fixing MCP clients that pass parameters as strings (e.g. `"10"` instead of `10`)
- **Array items type resolution** — `z.array(z.any())` now reads `items` definition from JSON Schema and applies coerce, fixing array parameters like `material_card_ids: [8, 11]` that failed validation

## [1.8.0] - 2026-04-01

### Changed

- **`batch_execute` returns full tool result data** — each operation result now includes a complete `data` field with the tool's full JSON response, enabling AI clients to programmatically access all returned data (previously only returned summary status)
- **Dynamic tools register before `server.connect()`** — startup sequence reordered (`mcpUnity.start()` → `registerDynamicTools()` → `server.connect()`) so external tools appear in the first `tools/list` query without relying on `sendToolListChanged()`
  - Graceful fallback when Unity Editor is not running: server starts with built-in tools only, no crash

### Added

- **Test external tools** — `test_echo` and `test_get_time` tools in `Assets/Editor/McpTestTools/` for verifying dynamic tool discovery and batch_execute data return

## [1.7.0] - 2026-03-23

### Added

- **Play Mode transparent reconnection** — `set_editor_state("play"/"stop")` now waits for WebSocket reconnection after Domain Reload and returns a verified result in a single call, eliminating the need for manual wait + check loops
- **Dynamic external tool discovery** — external projects can now register MCP tools by simply inheriting `McpToolBase` in their own assemblies; tools are auto-discovered via assembly scanning at startup
  - `McpToolBase.ParameterSchema` virtual property for self-describing JSON Schema parameters
  - `list_tools` internal method returns external tool definitions to Node.js
  - `McpUnity.waitForConnection()` utility for awaiting connection restoration
  - `jsonSchemaToZodShape()` converter for dynamic MCP SDK registration
  - `server.sendToolListChanged()` notification after dynamic registration

### Changed

- `set_editor_state` handler uses `queueIfDisconnected: false` to prevent unintended replay of play/stop commands

## [1.6.0] - 2026-03-23

### Added

- **UGUI Automation Testing Primitives** — 6 new Play Mode tools for AI agent UI testing:
  - `get_interactable_elements` — scan scene for all interactable UI elements (Button, Toggle, InputField, Slider, Dropdown, ScrollRect, etc.) with filtering and scope control
  - `simulate_pointer_click` — full pointer click event sequence (PointerEnter → PointerDown → PointerUp → PointerClick → PointerExit) on UI elements
  - `simulate_input_field` — fill text into InputField / TMP_InputField with onValueChanged and onEndEdit/onSubmit event triggers
  - `get_ui_element_state` — query runtime state of a single UI element (works in both Edit and Play Mode)
  - `wait_for_condition` — wait for conditions (active, inactive, exists, text_equals, text_contains, interactable, component_enabled) with configurable timeout and polling
  - `simulate_drag` — simulate drag gestures with delta or target-based movement, multi-frame interpolation, and IDropHandler support
- **`UIAutomationUtils` shared utility class** — Play Mode guards, GameObject lookup, state extraction, TMP reflection, and screen position helpers

### Fixed

- Fix `GetDisplayText()` returning placeholder text instead of InputField `.text` value when child Text component was a Placeholder

## [1.5.0] - 2026-03-19

### Added

- **`set_sibling_index` tool** — adjust sibling order (render order) of GameObjects, essential for UI element layering
- **`read_serialized_fields` / `write_serialized_fields` tools** — read and write Unity serialized fields via `SerializedProperty` API with bidirectional `m_` prefix mapping (e.g., `color` ↔ `m_Color`)
- **`requireCanvas` parameter** for `create_ui_element` — set to `false` to skip Canvas validation in prefab editing mode

### Fixed

- Fix `reparent_gameobject` losing children in prefab editing mode (use `SetParent` directly instead of `Undo.SetTransformParent` in `LoadPrefabContents` environment)
- Fix `screenshot_scene_view` capturing stale frame in prefab mode — converted to async with `EditorApplication.delayCall` to ensure `FrameSelected`/`Repaint` completes before capture
- Fix `update_component` failing for serialized field names like `m_Color` — added `SerializedProperty` fallback with bidirectional `m_` prefix mapping
- Fix `EnsureRectTransformHierarchy` being a no-op — now walks parent chain and adds `RectTransform` where missing (prefab-mode aware)
- Fix `enumNames` obsolete warning in Unity 2022.3 — use `enumDisplayNames` with `enumNames` fallback under `#pragma warning disable`

### Changed

- Extract `SerializedPropertyHelper` utility (`FindProperty` + `SetValue`) to eliminate ~250 lines of duplication across `UpdateComponentTool`, `ReadSerializedFieldsTool`, and `WriteSerializedFieldsTool`
- `UpdateComponentTool` now caches `SerializedObject` per component in batch operations instead of recreating per field
- Unified structured `ObjectReference` keys — both `assetPath` and `objectPath` now accepted in all tools
- Updated `update_component` and `write_serialized_fields` TS descriptions to clarify tool selection guidance
- `batch_execute` now returns full tool result data (complete JSON) for each operation, not just summary fields

## [1.4.0] - 2026-03-06

### Added

- **Screenshot tools** — `screenshot_game_view`, `screenshot_scene_view`, `screenshot_camera` for capturing Unity Editor visuals as PNG images, enabling AI to visually verify scenes and UI layouts
- **Editor state tools** — `get_editor_state` to query play mode, compilation, and platform status; `set_editor_state` to control play/pause/stop
- **`get_selection` tool** — read the current Unity Editor selection (GameObjects in hierarchy and/or assets in Project window)
- **Play Mode server persistence** — MCP server now stays alive during Play Mode (zero downtime when Domain Reload is disabled; auto-restart when enabled), unlocking all tools for runtime inspection

### Changed

- `screenshot_game_view` falls back to Camera.main render when `ScreenCapture` is unavailable in Edit Mode

## [1.3.0] - 2026-03-05

### Added

- **`update_scriptable_object` tool** — update field values on existing ScriptableObject assets without recreating them
- **`create_scriptable_object` tool** — create ScriptableObject assets with optional field values
- **`import_texture_as_sprite` / `create_sprite_atlas` tools** — sprite workflow support
- **`save_as_prefab` tool** — save scene GameObjects as Prefab assets
- **`open_prefab_contents` / `save_prefab_contents` tools** — Prefab Edit Mode support
- **`remove_component` tool** — remove components from GameObjects
- **`batch_execute` tool** — batch multiple tool calls in a single request for 10-100x performance improvement
- **UGUI tools** — `create_canvas`, `create_ui_element`, `set_rect_transform`, `add_layout_component`, `get_ui_element_info` for Unity UI creation and manipulation
- **Material tools** — `create_material`, `assign_material`, `modify_material`, `get_material_info`
- **Transform tools** — `move_gameobject`, `rotate_gameobject`, `scale_gameobject`, `set_transform`
- **GameObject operations** — `duplicate_gameobject`, `delete_gameobject`, `reparent_gameobject`
- **Scene management** — `create_scene`, `delete_scene`, `load_scene`, `save_scene`, `get_scene_info`, `unload_scene`
- **`recompile_scripts` tool** — trigger and await script recompilation with concurrent request support
- **`unity://shaders` resource** — query available shaders in the project
- **Prefab Variant support** in prefab creation tools
- **Asset reference support** in `update_component` — set Sprite, Material, Font, and other asset fields by path or GUID
- **Connection resilience** — auto-reconnect with heartbeat, command queuing during disconnection
- **Codex CLI support** with TOML configuration
- **Google Antigravity AI assistant** support
- **Claude Desktop support**
- **Multiplayer Play Mode** — auto-skip server startup in clone instances
- **Batch mode detection** — skip initialization in Unity Cloud Build / headless builds
- **AI agent skills** — `unity-mcp-workflow`, `unity-ui-builder`, `unity-test-debug`, `unity-figma-sync` skill documents for Claude Code, Codex, and Antigravity

### Fixed

- Replace deprecated APIs in UGUITools
- Prioritize prefab context over scene when resolving `objectPath` in tools and hierarchy creator
- Defer reconnect backoff reset until connection is stable
- Iterate over all loaded scenes in `GetScenesHierarchyResource` (#114)
- Prevent false positives in Multiplayer Play Mode clone detection (#113)
- Prevent file descriptor exhaustion from WebSocket reconnect loop (#110)
- Fix array serialization and TMP composite UI elements
- Fix 6 MCP tool issues: scene refs, TMP alpha, Canvas RectTransform, hierarchy depth limit, namespace resolve, duplicate component
- Fix component namespace resolution
- Fix `activeSelf` property name mismatch in `UpdateGameObjectTool`
- Add missing `GetShadersResource.cs.meta` causing CS0246 compile error
- Support project paths containing spaces
- Treat invalid, cancelled, and exception-failing tests as failures in `run_tests`
- Improve graceful shutdown handling for MCP server
- Restart MCP server when Unity Editor is unfocused during domain reload
- Fix render pipeline detection in material tools
- Apply `logsLimit` to compilation errors, remove useless timestamps
- Fix macOS homebrew Node.js path detection

### Changed

- Enrich serialization with `SerializedFieldConverter` supporting Vector2/3/4, Color, Quaternion, Bounds, Rect, enums, arrays, Lists, nested `[Serializable]` structs, and UnityEngine.Object references
- Prefab Edit Mode fallback added to all relevant tools
- Improved `get_gameobject` information output

## [1.2.0] - Previous release
