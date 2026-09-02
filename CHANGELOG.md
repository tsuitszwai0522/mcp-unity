# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [fork-1.17.0] - 2026-09-02

### Changed — BREAKING

- **`recompile_scripts` now refreshes the AssetDatabase before compiling.** It previously only called
  `CompilationPipeline.RequestScriptCompilation()` and never touched `AssetDatabase`, so a `.cs` file
  added on disk, or an edit to an existing `.asmdef`, produced a **silent false green** — the tool
  returned `"Successfully recompiled all scripts with 0 warning(s)"` while the change was not in any
  assembly. Deleting a `.cs` produced a persistent `error CS2001` instead.
  A new `refreshAssets` parameter (**default `true`**) makes the tool call `AssetDatabase.Refresh()`
  first. Order is: subscribe to compilation events → refresh → request compilation only if Unity is
  not already compiling. Subscribing first means compiler messages produced by the refresh-triggered
  compilation are now captured (previously those errors were invisible to both this tool and
  `get_console_logs`).
  Pass `refreshAssets: false` for the old behaviour. Cost measured on a large project: a no-op refresh
  blocks the main thread for ~200–350 ms; ~1 s when there is a new file to discover.
- **`recompile_scripts` responses no longer overstate what was compiled.** New fields `refreshed`,
  `refreshDurationMs` and `compilationWasAlreadyInProgress` (tri-state). Two paths are now explicit
  instead of claiming success: a request that piggybacks on another in-flight request, and a request
  that arrives while Unity was already compiling. Neither can confirm that its own file changes were
  part of the observed compilation, so neither reports `"Successfully recompiled all scripts"`.
- **`recompile_scripts` failures reach the caller with their typed error code.** The Node handler
  previously threw on `success: false`, discarding the payload; it now sets `isError: true` and keeps
  `error_code` plus the refresh metadata.

### Fixed

- **`get_console_logs` reported a fabricated filtered total.** `ConsoleLogsService` stopped counting at
  the pagination early-exit but returned that partial count as the number of entries matching the
  filter, which `GetConsoleLogsResource` printed as `"Retrieved N of M"`. The counter now walks the
  whole buffer; the returned page is unchanged.
- **`recompile_scripts` silently replaced an explicit `logsLimit: 0` with `100`** (`||` instead of `??`).
- **Missing Unity-side response metadata is now reported as `unknown` rather than coerced to `false`**,
  and an explicit `null` from Unity is preserved instead of being misreported as "metadata absent".
- Tool descriptions in the README, the C# tool and the Node wrapper claimed `recompile_scripts`
  recompiled "all scripts in the Unity project"; all three now state what it actually does.

## [fork-1.14.0] - 2026-08-26

### Changed — BREAKING

- **Every registered tool now rejects unknown top-level parameters.** Previously 175 of 176 tools
  silently stripped undeclared parameters and returned success, while advertising
  `additionalProperties: false` that nothing enforced. A central registration seam now converts every
  `server.tool(name, description, shape, callback)` registration into a strict `registerTool` call, so
  the advertised schema and the runtime behaviour finally agree.
  Callers that passed harmless extra parameters now receive an explicit error naming both the
  offending keys and the valid parameter names for that tool.
- **`batch_execute` validates inner operation parameters** against the same schema used for direct
  calls, closing the documented bypass. With `stopOnError: false` an invalid operation is reported as
  failed while the remaining operations still execute; with `stopOnError: true` validation happens
  before any operation runs, so the whole batch is rejected without side effects. Operations whose
  tool is not in the Node registry are forwarded to Unity unvalidated and disclosed in `warnings`.
- **Dynamic tool parameter coercion narrowed.** `z.coerce.boolean()` accepted every value (`"false"`
  and `"0"` both became `true`); it now accepts only real booleans and `"true"`/`"false"` strings.
  `z.coerce.number()` silently turned `null`, `true`, `""` and `[]` into numbers; it now accepts only
  numbers and numeric strings. Emitted JSON Schema is byte-identical to before.

### Fixed

- Registering a top-level `ZodObject` that carries an object-level refinement now fails loudly at
  registration time instead of silently discarding the refinement (zod 4 returns a `ZodObject` from
  `.refine()`, so such schemas previously passed through the seam unnoticed).
- Registration forms the seam cannot make strict (5-argument `tool()` with annotations, plain JSON
  Schema `inputSchema`) now fail loudly rather than silently registering a non-strict tool.
- The tool schema registry is per-server instead of module-global, so separate server instances no
  longer observe each other's tools.
- `batch_execute` no longer injects schema defaults into the payload sent to Unity, which had changed
  established behaviour for tools whose Unity-side default differs from the Node schema default.
  Caller-supplied keys still receive normal parsing so batch and direct calls agree on the wire.
- `batch_execute` summary no longer counts locally rejected operations as executed, verifies that
  Unity returned a result for every forwarded operation, and surfaces warnings in the primary text
  block as other tools already did.

> **Consumers pinning `#main` or an unpinned ref**: this release changes call behaviour. Any existing
> call that passes a parameter the tool does not declare will now fail instead of silently succeeding.
> Review call sites before upgrading, or pin an earlier tag.

## [fork-1.13.0] - 2026-08-26

### Added

- Game View screenshots now report whether the captured pixels actually reflect the current scene
  (`frameFresh`, `frameFreshReason`, `cameraRenders`) alongside the cameras that were temporarily
  isolated or merely disclosed (`isolatedCameras`, `contextCameras` and their counts).

### Fixed

- Cameras belonging to orphaned prefab preview scenes are now disabled for the duration of a Game
  View capture and restored afterwards; such cameras composite into the Game View and, with a higher
  depth and a skybox clear, could replace the entire image. Unity's own Prefab Stage and MCP prefab
  session cameras are disclosed but never touched.
- `PlayModeView.RenderView` does not render when called on its own; the capture path now forces a
  synchronous repaint so a stale cached frame is not returned as if it were current.

## [fork-1.12.0] - 2026-08-25

### Added

- Screenshot image responses now disclose their `capturePath`, degradation state and reason, and
  whether the request created a Game View window; the Node tools emit these diagnostics as text
  before the MCP image content block and explicitly identify older Unity packages that omit them.

### Fixed

- Bounded screenshot dimensions to 1–4096 on both Node and Unity, rejected malformed non-string
  image data, and made image-producing operations fail loudly in `batch_execute` without embedding
  base64 payloads.
- Scene View capture after Prefab framing now completes through an editor-update frame counter
  instead of a delay callback that can starve in headless MCP contexts.
- Total Game View capture failures now retain RenderView and ScreenCapture diagnostics inside the
  wire-visible error message, and batch image failures disclose created Game View windows before
  discarding the unsupported image payload.
- Game View exceptions now retain accumulated fallback diagnostics and unwrap reflected RenderView
  failures to their root exception type; Node preserves message metadata precedence and batch window
  side-effect disclosures across mixed-version defensive paths.

## [fork-1.11.0] - 2026-08-22

### Added

- Added SerializedProperty writes for whole-array replacement, nested Generic partial merges, and
  bounded direct `Array.size` updates. Grown JArray elements are cleared to type defaults before
  partial merge; direct `Array.size` growth and all JArray shrink operations emit warnings.
- Added nested object-reference read-back verification with no-undo rollback, rollback read-back,
  missing-reference GUID protection, and explicit disclosure of restored, skipped, and failed paths.
- Added warnings for direct `m_PersistentCalls` edits.
- Reflection-backed enum conversion now accepts the reader-emitted `{value,index,name}` shape, uses
  `value` as authoritative, and warns when the supplied name or index disagrees.

### Breaking

- Removed the public `SerializedPropertyHelper.VerifyObjectReferenceWrite` single-write API. Callers
  must collect and verify the complete nested object-reference write set instead.

### Fixed

- Fixed M-45 so non-truncated Generic and array values returned by `read_serialized_fields` can
  round-trip through SerializedProperty-backed write paths, including `update_component` fallback
  handling. Element-budget-truncated reads are outside this guarantee; callers must inspect
  `arrayMetadata`, and the writer warns before a shrink discards omitted elements.
- Fixed M-54 so reader-shaped enums no longer fail converter-backed component, Prefab, and
  ScriptableObject field writes.
- Grown Quaternion array slots now clear to `Quaternion.identity`, Unity's semantic "no rotation"
  default, instead of the invalid C# default value `(0, 0, 0, 0)`.

## [fork-1.10.0] - 2026-08-22

### Breaking

- `import_texture_as_sprite` now returns `validation_error` for unknown top-level parameters,
  including calls routed through `batch_execute`; these parameters were previously ignored.
- `import_texture_as_sprite` now returns `validation_error` when the legacy `spriteMode`,
  `meshType`, or `compression` parameter is explicitly JSON `null`; `null` previously selected the
  field's default.
- `import_texture_as_sprite` now rejects `spriteBorder` when the effective `spriteMode` is
  `Multiple`. Borders in Multiple mode must be set in each sprite's metadata; the old combination
  returned success even though the requested border was ineffective.

### Added

- Added `manage_asset` for GUID-preserving asset moves and renames, new-GUID copies, and explicit
  single-folder creation without overwrite or implicit parent-folder creation.
- Added optional `wrapMode` and `spriteBorder` writes and persisted read-back to
  `import_texture_as_sprite`, and documented that the tool always forces
  `textureType = TextureImporterType.Sprite`.

### Fixed

- Fixed `add_package` GitHub URLs to preserve `.git` and emit UPM's `?path=...#branch` ordering,
  and guarded completion logging when Package Manager returns no package information.
- `import_texture_as_sprite` now warns when changing `textureType` to Sprite resets omitted importer
  settings to Unity's Sprite defaults; it then writes `spriteMode`, `meshType`, and `compression`
  (using tool defaults when omitted), plus any provided `wrapMode` or `spriteBorder`.

## [fork-1.9.0] - 2026-08-21

### Added

- Added `get_gameobjects_by_component`, which resolves short, full, or assembly-qualified component
  type names, includes derived component matches, returns canonical GameObject paths, and defaults to
  compact output for high-cardinality component queries unless `componentFilter` is provided.

### Breaking

- **`duplicate_gameobject` parenting now defaults to local-transform preservation.** The new optional
  `worldPositionStays` parameter defaults to `false`, carrying the source local position, rotation,
  and scale into the selected parent. This is a breaking correction: older releases always behaved
  like `worldPositionStays: true`; callers that need the old world-pose-preserving behaviour must pass
  `true` explicitly.

### Fixed

- `create_canvas` now rejects an explicit `cameraPath` that is missing or has no `Camera` component
  instead of silently falling back. Its implicit fallback skips preview-scene cameras and selects the
  first enabled, tagged Main Camera in a loaded non-preview scene. Successful responses disclose
  `cameraSource` and canonical `cameraPath`; an unbound World Space canvas succeeds with a warning.
- Full component type names in `componentFilter` now work for both `get_gameobject` and
  `get_gameobjects_by_name`.
- `create_ui_element` now warns that `"Text"` creates legacy `UnityEngine.UI.Text` and directs TMP
  projects to request `"TextMeshPro"`.
- `wire_unity_event` now records the UnityEvent-owning component with Undo before adding a persistent
  listener.

## [fork-1.8.0] - 2026-08-21

### Breaking

- **Ambiguous `objectPath` values now fail instead of selecting the first GameObject.** Responses use
  `object_path_ambiguity_error` and include every candidate's `instanceId`, canonical hierarchy path,
  and scene name. Canonical paths have no leading slash and disambiguate same-name siblings or loaded
  roots with a 0-based `Name[n]` suffix. Literal names ending in a numeric bracket escape that bracket
  as `Name\[n]`, and `/` inside a literal name is encoded as `\/`; one leading slash remains accepted
  on input. Unescaped separators are significant: `Root/Panel/` addresses an empty-name child under
  `Panel`, and `//Player` addresses `Player` under an empty-name root (only the first leading slash is
  ignored). Callers that previously normalized trailing or repeated slashes must now preserve and use
  the returned canonical path exactly.
- **Ambiguous `get_gameobject` plain-name lookup now fails instead of using depth-first first-wins
  fallback.** The tool and resource check a matching loaded root first for compatibility; if no root
  matches, the hierarchy-wide name fallback succeeds only for one candidate and otherwise returns
  `object_path_ambiguity_error` with canonical paths and instance IDs.
- All hierarchy-path responses now use the shared canonical path generator, so a returned path can be
  passed back to an `objectPath` resolver without tool-specific formatting differences.
- Removed the public static `GetGameObjectPath` helpers from `GameObjectToolUtils`,
  `TransformToolUtils`, `UGUIToolUtils`, and `UIAutomationUtils`. Consumers should use
  `GameObjectPathUtils.GetCanonicalPath(GameObject)` or
  `GameObjectPathUtils.GetCanonicalPath(Transform)` instead.

### Fixed

- Hierarchy creation no longer creates a new scene root when its initial root lookup misses but
  nested GameObjects share that segment name. It returns `not_found_error` with their canonical
  candidate paths; genuinely absent root-qualified paths retain the existing create-on-miss behaviour.
- Prefab polling treats a missing Prefab-root prefix as `prefab_context_miss_error`; only a missing
  descendant after the active Prefab root has matched remains a soft polling miss.
- Hierarchy creation accepts canonical empty-name segments, so paths returned for empty-name objects
  round-trip through `update_gameobject` and `create_ui_element` with the same structured contract.
- `PrefabEditingService.FindByPath` retains its nullable public contract for every resolution failure,
  including ambiguity. Callers that need `object_path_ambiguity_error` candidate details should use
  `PrefabSessionScope.TryResolveGameObject`.

### Tests

- Cross-scene ambiguity coverage saves copies of the runner scene (`saveAsCopy:true`, runner scene
  untouched) into a constant folder `Assets/McpUnityObjectPathSceneTests`, opens them additively, and
  empties the copies immediately. SetUp self-heals residue from a previous interrupted run (constant
  folder path makes leftovers claimable); TearDown deletes the folder only after every scene under it
  closed successfully, leaving no consumer-project asset behind on the normal path.
- Supplemental IL wiring guards now include the polling resolver hop and the hierarchy creator used by
  `create_ui_element`, recognize both `call` and `callvirt`, and explicitly avoid claiming reachability
  proof.

## [fork-1.7.0] - 2026-08-21

Asset write honesty. Every tool that writes an asset now proves where it writes, refuses to touch a
read-only target, leaves nothing behind when it fails, and reports values it read back rather than
values it was asked for.

### Breaking

- **Asset paths are validated by containment, not by prefix, and invalid paths are rejected instead of
  rewritten.** `create_prefab.prefabName`, `save_as_prefab.savePath`,
  `import_texture_as_sprite.assetPath` and `create_sprite_atlas.savePath` / `folderPath` must resolve —
  after `Path.GetFullPath` normalization — inside this project's `Assets/` directory. Bare relative
  paths (previously written to the project root, or silently prefixed with `Assets/`), absolute paths,
  and `Assets/../..` escapes now return `validation_error` and create nothing. A prefix test alone did
  not stop escapes: `Assets/../../x.prefab` passes `StartsWith("Assets/")` and was measured writing a
  full asset outside the Unity project.
- **`import_texture_as_sprite` rejects unrecognised `spriteMode` / `meshType` / `compression` values**
  with `validation_error` listing the valid names, before touching the importer. They previously fell
  through to a silent default while the response echoed the request. Node's enum schema already blocked
  direct calls; `batch_execute` does not validate per-tool params, so the path was reachable.
- **`update_component` reports failure when the component could not be added.** `Undo.AddComponent`
  returning `null` previously produced `success: true` with an empty field list while still marking the
  GameObject dirty. It now returns `success: false` with a `failedFields` entry and does not call
  `EditorUtility.SetDirty`.
- **`create_sprite_atlas` returns `folderPath` read back from the saved atlas's packables** rather than
  the requested value.

### Fixed

- **Prefab saves no longer overwrite read-only targets.** All three save call sites — `create_prefab`,
  `save_as_prefab` and `save_prefab_contents` — refuse an existing read-only target before calling Unity.
  Measured previous behaviour: the file was replaced *and* its read-only permission bit cleared, and the
  tool reported success. No source-control checkout or attribute clearing is performed; the contract is
  failure with zero mutation.
- **Failure paths no longer leave artifacts behind.** `create_prefab` destroys its temporary GameObject
  in a `finally` (a save throw previously left an orphan in the open scene); `save_as_prefab` and
  `create_sprite_atlas` remove only the directories and assets this call created.
- **Cleanup can no longer delete an unrelated asset's `.meta`.** A path segment that is an existing
  *file* is no longer mistaken for a directory this call is about to create. Directory ownership is
  recorded only after `Directory.CreateDirectory` succeeds, so a failed write never passes a foreign
  path to recursive cleanup. Previously `savePath: "Assets/Images/tomato.png/Foo.prefab"` deleted
  `tomato.png.meta`, destroying that asset's GUID and importer settings, while the tool reported failure.
- **`import_texture_as_sprite` restores the importer when its post-write readback fails**, reimporting
  with `ForceUpdate | ForceSynchronousImport` so the restore is not skipped as up-to-date. Rollback
  failure is reported in the response rather than only logged.
- **`save_as_prefab` no longer walks off the project.** Containment is checked before
  `Directory.CreateDirectory`, which previously ran on the raw path.

### Changed

- `import_texture_as_sprite` reports `assetPath`, `spriteMode`, `meshType` and `compression` read back
  from the persisted importer after reimport.
- `create_prefab` reports the saved asset's path from `AssetDatabase.GetAssetPath`. Name collisions
  inside `Assets/` keep the existing `_1`, `_2`, … behaviour and are still reported in `prefabPath`.
- Unity-side and Node-side tool descriptions state the read-only, containment and failure contracts.

### Added

- `Editor/Utils/AssetPathUtils.cs` — shared containment normalization, read-only inspection, and
  owned-directory create/delete used by every call site above.
- `McpUnity.Tests.AssetWriteHonestyTests` — 21 EditMode tests. Node gains 4 tests (227 total).

## [fork-1.6.1] - 2026-08-20

Patch release. `fork-1.6.0` shipped a broken EditMode test class; no production tool behaviour changed.

### Fixed

- **Reverted the standalone UnityEvent test probe.** `fork-1.6.0` moved `UnityEventWiringProbe` into
  its own same-named file so that Unity would create a `MonoScript` asset for it and a wired component
  could survive a prefab round-trip. Measured effect was the opposite: with the probe in its own file
  `AddComponent<UnityEventWiringProbe>()` returns `null`, leaving `McpUnity.Tests.UnityEventWiringTests`
  at **2/15** in `fork-1.6.0`. Verified across four configurations — inline vs standalone, hand-written
  vs Unity-generated `.meta`, ad-hoc `PackageCache` deployment vs a real package re-resolve, and with the
  probe's helper types co-located — the split is the trigger and the deployment method is not. The probe
  is inline again and the class is back to **15/15**.
- **Reworked the causal diagnostic** (`WireUnityEvent_RuntimeOnlyGateIsCausallyVerified`, formerly
  `..._AfterPrefabRoundTrip_...`). The prefab save/reload half cannot work without a `MonoScript`, so it
  and the `MonoScript` assertions are gone. The attributable part is kept in full: one listener is invoked
  under `RuntimeOnly` and asserted **not** to fire, then the same listener is flipped to `EditorAndRuntime`
  and asserted to fire with the preserved method, mode and static argument. Serialization fidelity stays
  covered by the `SerializedObject` read-back assertions elsewhere in the class.

## [fork-1.6.0] - 2026-08-20

### Added

- **`wire_unity_event`** — adds persistent UnityEvent listeners from source/listener locators, an event field, a method name, and an optional static argument. The tool derives `PersistentListenerMode` from the event and method signatures, strictly rejects unknown inputs, rejects missing/ambiguous methods and duplicate matching component instances, and returns the inferred mode plus the serialized persistent call read back from Unity.
- **Recursive serialized-field reads** — `read_serialized_fields` now expands Generic fields, arrays, Lists, and UnityEvent persistent calls. `maxDepth` defaults to 8 and may be narrowed to reduce payload size before the 20,000-character transport cap is applied; depth-truncated branches are explicit.

### Fixed

- **Localization tests restore consumer Addressables state** — the fixture tracks ownership of `Assets/Tests`, removes only groups and labels created during the run through Addressables' cache-invalidating APIs, rebuilds and verifies `AddressableAssetSettings.currentHash` against the pre-fixture value **in memory**, and no longer deletes a pre-existing `xx-NOSUCH` locale as a one-off cleanup side effect. **Known residue:** the on-disk `m_currentHash` field still differs after a run even though labels, orphan group assets, and `Assets/Tests` are fully restored; the in-memory assertion passes but the value written by a later project save does not match the pre-run baseline. Tracked separately.
- **`wire_unity_event` failure envelope** — every validation, locator, prefab-session, component, method, write, and verification failure now returns `success: false`, a top-level message, and a typed nested error; component ambiguity details include every candidate instance ID.
- **Bounded serialized array reads** — `read_serialized_fields` now accepts a narrowing-only global `maxElements` budget, reports `total`, `returned`, `truncated`, and truncation causes in per-property `arrayMetadata`, and repeats the truncation summary in the scalar message retained by transport-level payload truncation.
- **Mutation-sensitive Localization ownership guard** — teardown locale removal and its test share one ownership-gated cleanup seam; the test creates a non-fixture-owned locale and proves both its registration and asset survive that path.

### Tests

- Added EditMode coverage for UnityEvent mode inference, runtime invocation, method guards, component ambiguity, and recursive/depth- and aggregate-width-limited reads.
- Added Jest coverage for strict `wire_unity_event` input rejection, forwarding/read-back behavior, real transport-rejected Unity errors, recursive read bounds, and scalar truncation-summary preservation.
- Added an attributable EditMode diagnostic control that proves a RuntimeOnly listener does not fire, then flips only that listener to EditorAndRuntime and proves the same method, inferred mode, and static argument fire correctly; also added array-width truncation metadata coverage.
- Added mutation-sensitive Addressables label/hash restoration coverage that treats the pre-test label set as consumer-owned, creates and removes only one later fixture label, invalidates the outer hash cache, and proves the derived hash rebuilds to the snapshot value without introducing a synthetic consumer sentinel.

## [fork-1.5.0] - 2026-08-20

Third-batch rollout of the MCP write contract established in `fork-1.2.0` and `fork-1.3.0`.
**The contract changes and package API removal below are breaking.**

### Changed

- **Breaking: `update_gameobject` accounts for every supplied field** — every `gameObjectData` key now appears in either `updatedFields[]` or `failedFields[{field,reason}]`. This includes empty/null names, unregistered tags, invalid or mistyped layers, mistyped booleans, duplicate legacy aliases, and unknown fields; any failure produces `success:false`, while independently valid fields may still be applied. The Canvas-without-RectTransform advisory is exposed through `warnings[]`, and the Node wrapper returns partial results as `isError:true` with a structured payload instead of discarding the field-level details. This release makes only this tool's nested `gameObjectData` schema strict so unknown keys fail validation before dispatch; it does not establish repo-wide strictness for other Zod schemas.
- **Breaking: `add_asset_to_scene` position defaults to world space** — new `positionSpace:"world"|"local"` defaults to `"world"`, so a parented instance now keeps the caller's requested final world position. Callers relying on the previous effective local-space behavior must pass `positionSpace:"local"`. Responses contain Transform-read-back `worldPosition` and `localPosition`, both preserved by the Node wrapper.
- **Breaking: ambiguous component names no longer resolve first-wins** — fully-qualified matches take priority; ambiguous short or partial namespace names are narrowed only when exactly one candidate's **exact runtime type** is attached to the target GameObject, so a derived component is not also counted as every matching base type. The selected and alternate full names are disclosed in `warnings[]`. Otherwise `update_component`, `remove_component`, `read_serialized_fields`, and `write_serialized_fields` return `component_ambiguity_error` listing every candidate and require a fully-qualified name. Component types are indexed once per loaded assembly set so ambiguity detection does not rescan every type on this hot path. The public one-argument `ComponentTypeResolver.FindComponentType(string)` API was removed because it could only report ambiguity to the Unity console; package consumers must use the overload that explicitly returns warning and ambiguity details.
- **Breaking: `create_sprite_atlas.atlasName` is now a filename assertion** — `atlasName` must exactly match the extensionless filename in `savePath` or the tool returns `validation_error` before creating an asset. `.spriteatlas` and `.spriteatlasv2` extensions are recognized case-insensitively. Successful responses read `atlasName`, `includeInBuild`, `allowRotation`, and `tightPacking` back from the saved SpriteAtlas, and the Node wrapper preserves that read-back payload.
- **Caller-visible tool metadata documents the new contracts** — the affected C# and Node tool descriptions now state `add_asset_to_scene.positionSpace`'s world-space default and local-space option, `update_gameobject`'s per-field partial-failure behavior, component-name ambiguity handling, and the SpriteAtlas filename assertion so MCP clients see the constraints in `tools/list` rather than only in this changelog.

### Tests

- Added EditMode coverage for all four contracts, including both `positionSpace` modes, every ambiguous component call site, exact-type target narrowing across inheritance, ordinary Unity short-name resolution, complete supplied-key accounting, case-insensitive SpriteAtlas extensions, atlas no-side-effect validation, and mutation-sensitive SpriteAtlas settings read-back.
- Added Node tests for field-level `isError` payloads and strict schema metadata, position schema/default/read-back forwarding, valid SpriteAtlas request/read-back forwarding, the filename constraint in `tools/list`, component ambiguity guidance, and warning preservation.

## [fork-1.4.0] - 2026-08-20

S6 — Prefab contents editing sessions become a hard, recoverable boundary. **Breaking for callers that relied on
silent scene fallback during Prefab editing, or on the removed no-owner converter overloads.**

### Changed

- **Breaking package API change: GameObject resolver helpers now surface structured errors** — `MaterialToolUtils.FindGameObject` now returns a `JObject` error and writes the resolved `GameObject` through an `out` parameter. `GameObjectToolUtils.FindGameObject` keeps its existing structured-error signature, but now enforces the same Prefab-session boundary. Package consumers calling these public helpers must handle the returned error before using the object.
- **Breaking package API change: serialized-reference conversion requires an explicit ownership choice** — the three public `SerializedFieldConverter.ConvertJTokenToValue` overloads that silently omitted `referenceOwner` were removed. Pure-value consumers can move to the equivalent, explicitly named `ConvertJTokenToValueWithoutReferenceOwner` overloads; serialized writers must call the six-argument `ConvertJTokenToValue` overload with the actual owner. During an active Prefab session the no-owner path fails closed for preview-object references, whereas earlier package versions resolved and wrote them without validating the write target.
- **Scene lookup is consistently multi-scene, inactive-aware, and preview-safe** — with no Prefab contents session open, the shared resolver, `GameObjectHierarchyCreator`, `get_gameobjects_by_name`, and UGUI Canvas/EventSystem discovery use loaded non-preview scenes only. Unity-owned Prefab Stage and other preview scenes are deliberately excluded, so ordinary scene tools cannot enumerate or mutate unsaved preview contents. This intentionally broadens tools that previously stopped at `GameObject.Find`, and prevents `update_gameobject` from creating a duplicate root that `update_component` could already resolve.
- **Prefab contents are an explicit UI-authoring boundary** — `create_canvas` fails while Prefab contents are open, and `create_ui_element` requires an existing Canvas unless `requireCanvas=false`. MCP Unity no longer auto-writes Canvas or EventSystem objects into a Prefab asset.
- **Removed unsafe legacy overloads** — the unused throwing `GameObjectHierarchyCreator.FindOrCreateHierarchicalGameObject(string)` wrapper and nullable-root `PrefabSessionScope.CreatePathContextMissError(string, GameObject = null)` overload were removed so typed scope errors cannot be silently collapsed or treated as success.
- **Screenshot fallback is context-bound** — without a Prefab session, `screenshot_camera` without locators and a failed Edit Mode `screenshot_game_view` capture may still use the loaded scene `Camera.main`. During an active session, `screenshot_camera` requires an enabled `MainCamera`-tagged Camera inside the Prefab contents, while `screenshot_game_view` refuses its scene-Camera fallback; neither silently photographs an unrelated scene Camera.
- **`batch_execute atomic=true` is rejected for Prefab-session work** — atomic batches remain Undo-backed outside Prefab editing, but preview-scene create/delete operations intentionally bypass Unity Undo and cannot honestly promise rollback. Calls are rejected when a session is already active or when the batch contains `open_prefab_contents`, which would activate one mid-batch. Callers must save/discard the session or explicitly use `atomic=false`.
- **Unity error types use one client-visible key across tools and resources** — JSON-RPC failures expose the original Unity type as `details.unityErrorType` on both tool and `unity://gameobject/{idOrName}` resource errors. Resource classification remains `resource_fetch_error`, with the wrapper classification retained as `details.upstreamErrorType`.

### Fixed

- **Prefab contents sessions now survive domain reloads and same-domain preview loss** — `open_prefab_contents` persists the Prefab GUID, display path, and preview-root instance ID in `SessionState`; state is re-evaluated against that record instead of a one-shot managed-reference latch. A destroyed or unrepairable root remains `Lost` with its recovery record intact until `save_prefab_contents discard=true` acknowledges it. Lost acknowledgement never resolves an already-unloaded stale instance ID again, and moved assets accept either retained or canonical preview paths while saving to the current GUID-resolved path.
- **Prefab sessions are now a hard GameObject-addressing boundary** — object paths, names, and scene-object instance IDs resolve only inside the active Prefab contents. Missing or cross-scene targets return `prefab_context_miss_error` instead of falling back to an open scene, including serialized `objectPath` reference values.
- **Prefab child reparenting cannot create unsaved preview roots** — `reparent_gameobject` rejects moving a Prefab-contents child to the preview-scene root level because `SaveAsPrefabAsset` serializes only the active Prefab root tree; the tool no longer reports success for edits that unload would silently discard.
- **Failed Prefab saves preserve recoverable edits** — `save_prefab_contents` uses the `SaveAsPrefabAsset` overload with an explicit success result and keeps the preview scene plus session state open when saving fails. In Unity 2022.3, probed failures throw or return true, so the defensive `success=false` branch is coverable only through the injected save delegate. If saving succeeds but unload fails, the tool reports `prefab_cleanup_error` with `saveCompleted=true` instead of claiming the save failed.
- **Prefab cleanup errors now report state-correct recovery** — `prefab_cleanup_error.details.sessionStatus` distinguishes `Active` from `Lost`. Active sessions instruct callers to save again so post-error edits are preserved; Lost sessions instruct callers to acknowledge the recovery record with `discard=true`.

## [1.14.1] - 2026-05-15

Upstream merge — cherry-picked four stability/compatibility commits from `CoderGamester/mcp-unity` covering MCP client schema compatibility, request-timeout reconnect behavior, and concurrent client support. No new tools or parameters; behavior changes only.

### Fixed

- **Transform tools no longer emit local JSON pointer refs** (upstream `4828b85`) — `move_gameobject`, `rotate_gameobject`, `scale_gameobject`, and `set_transform` now build a fresh nested Vector3 schema per field via `createVector3Schema()` instead of reusing a single `vector3Schema` constant. The previous shared-schema pattern caused `zod-to-json-schema` to emit `#/properties/position` refs that some MCP clients (notably ones that rejected `KeyError: 'position'` during tool initialization) could not resolve. Fix is TS-only; C# side unaffected.
- **Request timeouts no longer trigger reconnect cascades** (upstream `265545f`) — a single tool-call timeout previously cascaded into a full WebSocket reconnect attempt, which would tear down every other in-flight request on the same connection. The Node-side WebSocket client now distinguishes per-request timeouts from connection-level failures and leaves the socket open. Includes regression tests in `Server~/src/__tests__/mcpUnity.test.ts` and `unityConnection.test.ts`.

### Changed

- **Multiple concurrent MCP clients are now supported** (upstream `7d505c3`) — previously, opening a second MCP client (e.g. two Claude Code instances against the same Editor) would kick the first client's WebSocket session, which the Node side would then attempt to reconnect, producing an infinite reconnection loop. `McpUnityServer.Clients` is now a `ConcurrentDictionary<string, string>` and `McpUnitySocketHandler.OnClose` uses `TryRemove` (the OnClose merge also preserves the v1.14.0 in-flight-tracking clear from commit `2293747` so OnError still gets attribution context). Connect/disconnect log lines now include the total client count.
- **Stale WebSocket sessions are cleaned up on new connection** (upstream `a163961`) — `OnOpen` now queries WebSocketSharp's `InactiveIDs` and removes only dead sessions, preserving active connections from other MCP clients. Prevents file descriptor accumulation from crashed Node.js processes without re-introducing the single-client kick from before `7d505c3`. See upstream issue #110 for the FD accumulation history.

## [1.14.0] - 2026-05-07

`run_tests` gains an `assemblyNames` filter so callers can scope a run to specific test assemblies — and, more importantly, **exclude broken third-party test assemblies** without having to embed and patch the offending package.

### Added

- **`run_tests` — `assemblyNames` parameter** — optional `string[]` forwarded as-is to Unity Test Framework's `Filter.assemblyNames`. Each entry matches the test's assembly name (without `.dll`); prefix an entry with `!` to exclude that assembly. Multiple entries with `!` are AND-combined (a test must not match any exclusion); inclusion entries are OR-combined; mixed inclusion+exclusion is `(OR of includes) AND (AND of NOT excludes)` per NUnit's standard filter semantics. Combines with the existing `testFilter` (testNames) — both filters are AND-applied.

  Motivating use case: Multiplayer Tools 1.1.1 ships a broken `Unity.Multiplayer.Tools.Adapters.Tests.Ngo1WithUtp2.Ngo1WithUtp2AdapterInitializerTests` whose asmdef declares `includePlatforms: ["Editor"]` but whose body calls `NetworkManager.StartHost()` — the test floods EditMode runs with `EditMode test can only yield null`. Pass `assemblyNames: ["!Unity.Multiplayer.Tools.Adapters.Tests"]` (or any other broken third-party assembly) to skip the offender without touching the package source.

  Example: `run_tests testMode=EditMode assemblyNames=["!Unity.Multiplayer.Tools.Adapters.Tests"]`

## [1.13.0] - 2026-04-15

Addressables group schema and profile management tools — the Small-Client / CDN workflow now has first-class MCP surface for reading and flipping `BundledAssetGroupSchema` fields and switching profile variables without opening the Addressables window.

### Added

- **`addr_get_group_schema` tool** — read all 10 `BundledAssetGroupSchema` fields plus two resolved helpers (`build_path_value` / `load_path_value`) showing the profile-token-expanded URL. Lets agents verify Remote CDN wiring without manually substituting profile variables.
- **`addr_set_group_schema` tool** — partial update of `BundledAssetGroupSchema` with `dry_run` support and a `diff` payload showing `from` / `to` per changed field. Supports `compression`, `include_in_build`, `packed_mode`, `bundle_naming`, `use_asset_bundle_cache`, `use_unitywebrequest_for_local_bundles`, `retry_count`, `timeout`, `build_path`, `load_path`. **Validate-all then apply**: every field is validated before any mutation, so a failing field aborts the request with zero in-memory side effects — the tool never returns `validation_error` with the schema half-updated.
- **`addr_list_profiles` tool** — list every Addressables profile with its resolved variable map and an `isActive` flag.
- **`addr_get_active_profile` tool** — query the currently active profile and its variables.
- **`addr_set_active_profile` tool** — switch the active profile by name (persisted via `SetDirty` + `SaveAssets`).
- **`addr_set_profile_variable` tool** — set a profile variable value (e.g. `Remote.LoadPath` on the `Default` profile). Pass `create_if_missing=true` to create the variable at the profile-settings level; **newly-created variables are added to ALL profiles**, not only the named profile — documented explicitly in the tool description and README to prevent accidental global state.

### Changed

- **Addressables schema parsers hardened** — `TryParseEnum` now rejects numeric strings (e.g. `"999"` for `compression`) and runs a final `Enum.IsDefined` check, closing a gap where `Enum.TryParse` would accept undefined enum values via numeric coercion. `TryParseBool` now requires `JTokenType.Boolean` — no string / integer coercion. `TryParseNonNegativeInt` replaces the old `TryParseInt` and rejects both non-integer JSON tokens and negative values for `retry_count` / `timeout`. All three close the `batch_execute` / direct-WebSocket bypass where the Node-side zod schema would not run.
- **`AddrSetGroupSchemaTool.Execute` refactored to plan-then-apply** — phase 1 walks every field and builds a `List<SchemaChange>` with captured `Apply` delegates; phase 2 runs every `Apply` only if phase 1 completed cleanly. A single profile-variable-names snapshot is reused across field validation, so `build_path` and `load_path` errors reference a consistent candidate list.

### Tests

- **+5 EditMode regression tests** (`G10`–`G14`) covering the failure modes that motivated this release:
  - `G10` — multi-field payload where a later field is invalid must not leave earlier fields mutated in memory (read-back confirms `compression` is still the primed value).
  - `G11` — `"999"` for `compression` returns `validation_error` (numeric-string enum bypass closed).
  - `G12` — `"true"` string for `include_in_build` returns `validation_error` (bool coercion closed).
  - `G13` — `-1` for `retry_count` returns `validation_error` (non-negative range enforced).
  - `G14` — `"5"` string for `retry_count` returns `validation_error` (integer coercion closed).

### Docs

- **`AGENTS.md` current tools list** — added all 21 Addressables tools and 9 Localization tools that had accumulated across v1.11.x and this release. The list was previously capped at the v1.10 snapshot; running through the repo's own update policy identified the drift.
- **`README.md`, `AddrSetProfileVariableTool` C# / TS descriptions** — explicitly call out that `create_if_missing=true` adds the variable to every profile, not only the named one.

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
