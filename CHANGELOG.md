# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

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
