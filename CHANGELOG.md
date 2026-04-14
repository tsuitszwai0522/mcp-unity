# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [1.12.0] - 2026-04-14

Usability follow-ups driven by external project feedback. Three small, independent changes that close real friction points when agents inspect scenes and localization content.

### Added

- **`get_gameobjects_by_name` tool** — finds ALL GameObjects whose name matches a glob pattern (`*`, `?`). Returns an array of matches with hierarchical `path` fields, component data, and a `truncated` flag. Complements `get_gameobject`, which only returns the first match — use this when multiple instances share a name (e.g. 7 × `CBCardUI(Clone)`). Parameters: `name` (glob), `includeInactive` (default `true`), `maxDepth` (default `0` — target only), `includeChildren` (default `false`), `limit` (default `100`, max `1000`). In prefab editing mode, the search scopes to the prefab root via `PrefabEditingService`. Scene mode uses `Object.FindObjectsByType<GameObject>` with `FindObjectsInactive` respected.
- **`loc_get_entries` — `include_values` parameter** — new optional boolean (default `false`). When `true`, the tool renders each entry as `key: value` lines into the MCP text content. When `false` (default), only the count summary is returned, which saves tokens on large tables. Closes the verification gap where agents writing 20+ keys could only see "Read N entries" in the text payload with no way to inspect actual values. C# side is unchanged — the fix lives entirely in the TypeScript wrapper.
- **`screenshot_game_view` — `force_focus` parameter** — new optional boolean (default `false`). When `true`, the tool force-focuses the Game View tab, repaints, waits one frame via `EditorApplication.delayCall`, then captures. Prevents the common failure mode where `ScreenCapture.CaptureScreenshotAsTexture()` samples whichever EditorWindow is currently focused — if the Scene View was active, the caller got a Scene View render at Game View dimensions instead. The tool is now `IsAsync = true` to accommodate the delayed capture.

### Changed

- **`ScreenshotGameViewTool` lifecycle** — converted from sync `Execute` to async `ExecuteAsync` to support the `force_focus` delay path. The non-`force_focus` (default) path still captures synchronously via `tcs.TrySetResult(CaptureGameView(...))` without any frame delay, so existing callers see no latency regression.
- **`GetGameObjectsByNameTool` Unity-side validation + early-exit** (review fix) — rejects `limit` outside `[1, 1000]` and `maxDepth < -1` with `validation_error` before scanning, closing the bypass where `batch_execute` or a direct WebSocket caller could skip the TS zod schema (negative `limit` previously crashed `RemoveRange`). Scene loop and prefab recursion now stop collecting as soon as `matches.Count >= limit` and set `truncated=true`, so wide patterns over large scenes no longer enumerate every match before slicing.
- **`loc_get_entries` `include_values` output cap + newline escape** (review fix) — new `max_entries` parameter (default `200`, hard max `1000`) caps how many entries are rendered into MCP text content; the full `entries` array still ships in the `data` payload. `\r` and `\n` inside keys/values are escaped to `\\r`/`\\n` so multi-line TMP rich text no longer fragments the `key: value` line format. A truncation hint (`... truncated N entries`) is appended whenever the cap fires.

### Tests

- **+14 Jest tests** (now 110 total across 10 suites):
  - `localizationTools.test.ts` (6) — `include_values` rendering, `\r\n` escaping, `max_entries` cap + default + truncation hint, TS-only param stripping before forwarding to Unity, `data.entries` integrity regardless of cap
  - `getGameObjectTool.test.ts` (4) — `get_gameobjects_by_name` registration, glob param forwarding, JSON text serialization, Unity-failure → `TOOL_EXECUTION` propagation
  - `screenshotTools.test.ts` (4) — `screenshot_game_view`/`scene_view`/`camera` registration, `force_focus` forwarding, image content shape, `force_focus` omission default
- **+7 EditMode tests** (`McpUnity.Tests.GetGameObjectsByNameToolTests`) — validation errors for missing `name`, `limit < 1`, `limit < 0` (negative-limit `RemoveRange` regression), `limit > 1000`, `maxDepth < -1`; truncation respects `limit` and sets `truncated=true`; positive glob match path.

### Documentation

- **`README.md`** — added `get_gameobjects_by_name` to the GameObjects tools list, added `force_focus` note to `screenshot_game_view`, added `include_values` + `max_entries` notes to `loc_get_entries` with updated example prompt.
- **`AGENTS.md`** — added `get_gameobjects_by_name` to the tool list.
- **`doc/codeReview/Request_20260414_UsabilityImprovements.md` + `Response_20260414_UsabilityImprovements.md`** — code review round-trip for the v1.12.0 usability improvements drop.

### Release metadata

- **Version alignment** — bumped `Server~/package.json`, `Server~/package-lock.json`, and `server.json` (both root and npm package entry) from their stale `1.0.0`/`1.2.1` values to `1.12.0` to satisfy the `AGENTS.md` release/version-bump checklist.

## [1.11.2] - 2026-04-13

Addressables code-review follow-up — aligns tool contracts with the spec, closes the first-use regression gap, and hardens the `addr_init_settings` path-handling attack surface. See `doc/codeReview/Response_20260413_AddressablesTools.md`.

### Added

- **`addr_get_settings` returns `version`** — reads the Addressables package version via `PackageInfo.FindForAssembly(typeof(AddressableAssetSettings).Assembly)` and surfaces it alongside the existing summary fields. Caller can now gate capability on package version (spec required this field; v1.10.0 shipped without it).
- **`addr_add_entries` strict mode (default)** — new `fail_on_missing_asset` parameter, defaults to `true`. In strict mode any unresolvable `asset_path` aborts the batch with `not_found` instead of silently becoming a skip+warning, matching the spec's error contract. Pass `fail_on_missing_asset: false` to opt back into best-effort batching; the lenient response now also carries a `missingAssets` array so callers can act on the skipped paths without parsing warning strings.
- **`addr_init_settings` folder validation** — the `folder` parameter is validated up-front before any filesystem work: must start with `Assets/`, must not contain `..` traversal. Rejected inputs return `validation_error` with no side effect. Closes the `Directory.CreateDirectory` vector where an agent-supplied `../evil` path would create a folder anywhere on disk.
- **`AddrHelper.SettingsProvider` test injection point** — `internal static System.Func<AddressableAssetSettings>` that `TryGetSettings` routes through. Production code keeps the default `AddressableAssetSettingsDefaultObject.GetSettings(false)` closure; tests can swap it to simulate "Addressables not initialised" without tearing down the consumer project's real settings asset.

### Tests

- **+4 new Addressables tests** (`McpUnity.Tests.Addressables.AddrTests`, now 66 total):
  - `A0_Tools_WhenNotInitialized_ReturnNotInitializedError` — uses `SettingsProvider` injection to lock the `not_initialized` contract across five representative tools (`addr_list_groups`, `addr_list_labels`, `addr_create_label`, `addr_add_entries`, `addr_find_asset`). Closes the regression gap flagged by the review: the first-use path is now under test without the blast radius of actually removing the default settings.
  - `A3b_InitSettings_FolderOutsideAssets_ReturnsValidationError` — rejects `/tmp/evil`, `C:/Windows/System32`, `Packages/com.unity.addressables`
  - `A3c_InitSettings_FolderWithParentTraversal_ReturnsValidationError` — rejects `Assets/../evil` and `Assets/foo/../../bar`, asserts no folder side-effect
  - `D5b_AddEntries_InvalidAssetPath_LenientMode_SkippedWithWarning` — covers the opt-in best-effort path end-to-end, asserts `missingAssets` array presence
- **`D5_AddEntries_InvalidAssetPath_*` renamed + flipped** — now `D5_AddEntries_InvalidAssetPath_StrictDefault_ReturnsNotFound`, asserts the new default contract. The lenient behaviour moved to `D5b`.
- **`A1_GetSettings_*` extended** — asserts the new `version` field is populated.

### Documentation

- **`doc/requirement/feature_addressables_mcp.md`** — `addr_add_entries` chapter rewritten to document the strict/lenient split (`fail_on_missing_asset`, two response shapes, explicit error semantics). Resolves the internal inconsistency flagged by the review (the previous doc simultaneously showed `skipped` in the success shape and listed `not_found` as the asset-missing error).
- **`doc/requirement/feature_addressables_mcp_tests.md`** — removed the self-contradiction in the coverage goals. The `not_initialized` error branch is now explicitly covered via `SettingsProvider` injection; the deferred list now only carries the `addr_get_settings` `initialized:false` **happy-path** shape (which is a non-error branch that still needs tearing down real settings to trigger).
- **`doc/codeReview/Request_20260413_AddressablesTools.md` + `Response_20260413_AddressablesTools.md`** — code review round-trip for the v1.11.1 Addressables drop.

## [1.11.1] - 2026-04-13

### Added

- **`[McpUnityFirstParty]` markers on all 15 Addressables tools** — keeps the dynamic `list_tools` path from double-registering them; the hand-written TypeScript wrappers in `Server~/src/tools/addressablesTools.ts` remain the canonical entry point.
- **`AddrHelper` `InternalsVisibleTo`** — `McpUnity.Addressables` exposes internals to `McpUnity.Addressables.Tests` so the test fixture can reach shared helpers directly.

### Tests

- **62 EditMode tests pass** (`McpUnity.Tests.Addressables.AddrTests`) covering the entire 1.10.0 Addressables tool suite:
  - 4 Settings tests (A1–A4): `addr_get_settings` field shape, `addr_init_settings` idempotency, custom-folder param handling on the idempotent path
  - 18 Group tests (B1–B18): create with all schema variants (`PackTogether`/`PackSeparately`/`PackTogetherByLabel`, `include_in_build`), default-group handling, in-use protection, validation errors, default-group deletion guard
  - 10 Label tests (C1–C10): create/list/remove, idempotency, space/bracket/empty validation, in-use protection, force-strip
  - 26 Entry tests (D1–D26): batch add/remove/move with mixed identifiers (guid + asset_path), glob filters, address pattern matching, `truncated` flag, partial `set_entry` update, auto-label-creation warnings, mixed valid/invalid batch reporting
  - 3 Query tests (E1–E3): `addr_find_asset` for addressable / non-addressable / non-existent paths
  - 1 Golden Path scenario (F1, `[Order(999)]`): 18-step end-to-end agent workflow exercising every tool in realistic order, with a `step` counter embedded in every assertion message for fast failure localisation
- **Self-contained dummy assets** — fixture creates `AddrTestDummySO` ScriptableObjects in `Assets/Tests/AddressablesTests/` at `[OneTimeSetUp]` and removes them at `[OneTimeTearDown]`. No dependency on any specific asset existing in the consumer Unity project. The `AddrTestDummySO` type lives inside the test assembly only and never ships in runtime builds.
- **Default-group restoration** — `[OneTimeSetUp]` snapshots `_originalDefaultGroup`; per-test `[TearDown]` restores it before cleaning up test groups, so tests that mutate the default (B5, B11) cannot leak state into the consumer project or sibling tests.
- **Defensive cleanup** — `CleanupTestArtifacts` scrubs any residual entry on the dummy-asset paths regardless of which group it landed in, then removes any `McpAddrTest_*`-prefixed groups and labels. Survives mid-test crashes from previous failed runs.
- **`testables` requirement** — same as Localization: running these tests requires the consumer project's `Packages/manifest.json` to include `"testables": ["com.gamelovers.mcp-unity"]`.

### Documentation

- **`doc/lessons/unity-mcp-lessons.md`** — two new lessons:
  - "The Unity project running tests is the **consumer project**, not the package source folder" — explains why `AssetDatabase.AssetPathToGUID` returns empty for files that clearly exist in the package's repo Assets folder, with diagnosis tip via `get_editor_state`'s `Current Scene` path.
  - "`mcp__mcp-unity__run_tests` with broad filters fails the WebSocket payload size limit" — for test classes with > ~30 tests, the response payload exceeds the WebSocket frame buffer; fix is to filter one test at a time even though it's tedious.
- **`doc/requirement/feature_addressables_mcp_tests.md`** — full 4-stage test plan document that drove this implementation, including fixture design, test inventory, deferred-test rationale, and risk register.

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
