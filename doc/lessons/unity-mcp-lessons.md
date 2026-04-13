# Unity MCP Lessons Learned

This file accumulates pitfalls, undocumented behaviours, and confirmed-working approaches discovered while working with Unity APIs and MCP Unity tooling. Per `.claude/rules/unity-mcp-learning.md`, append new entries when something surprising shows up; never duplicate.

---

## Unity Localization

### [Pitfall] `Locale.CreateLocale` is the only correct factory for new Locales
- **Date**: 2026-04-13
- **Context**: Implementing `loc_add_locale` to bootstrap a project's Localization settings.
- **Issue**: `create_scriptable_object` of type `Locale` produces an empty asset whose `m_Identifier` is never set, leaving Unity Localization with a phantom locale that has no `Code` and no `CultureInfo`.
- **Fix**: Always use `UnityEngine.Localization.Locale.CreateLocale(new LocaleIdentifier(code))`. The factory wires up `m_Identifier` and `m_CultureInfo` correctly. Then `AssetDatabase.CreateAsset(locale, path)` + `LocalizationEditorSettings.AddLocale(locale, false)`.

### [Undocumented] `LocalizationEditorSettings` has no public `Remove*Collection` API in 1.x
- **Date**: 2026-04-13
- **Context**: Writing test cleanup + implementing `loc_delete_table`.
- **Issue**: `LocalizationEditorSettings.RemoveCollection(...)` does not exist in `com.unity.localization` 1.5.x (and likely never did publicly). Old code that calls it compiles only when the test assembly is silently dropped — the bug is latent until something else triggers compilation.
- **Fix**: Delete the underlying assets via `AssetDatabase.DeleteAsset` (collection asset, SharedTableData, every per-locale StringTable). Unity's `LocalizationAssetModificationProcessor.OnWillDeleteAsset` hook catches the deletion and calls `RemoveCollectionFromProject()` automatically. Wrap in a helper — `LocTableHelper.DeleteStringTableCollection` is the canonical version.

### [Better Way] Use `StringTableCollection.RemoveEntry(TableEntryReference)` for orphan-safe key removal
- **Date**: 2026-04-13
- **Context**: Fixing the `loc_delete_entry` orphan leak (B1 in code review Response_20260413_LocalizationTools.md).
- **Issue**: Calling `SharedData.RemoveKey(key)` directly only removes the key from SharedTableData. Each per-locale `StringTable` still contains an orphan `StringTableEntry` referencing the now-deleted `keyId`. `loc_get_entries` hides the orphan (because it filters via SharedData), so the bug is invisible to tool-only verification — it only shows up when reading the per-locale `.asset` YAML directly or after a project reimport.
- **Fix**: Use `collection.RemoveEntry(key)` (the collection-level API on `StringTableCollection`, available since 1.x). Internally it iterates `StringTables`, calls `table.RemoveEntry(keyId)` on each, then `SharedData.RemoveKey(entry.Key)`, AND raises `LocalizationEditorSettings.EditorEvents.RaiseTableEntryRemoved`. The manual iteration approach works but misses the editor event and is more code.

### [Edge Case] `.NET` `CultureInfo` rejects valid Unity Localization identifiers
- **Date**: 2026-04-13
- **Context**: Adding a soft pre-check for invalid locale codes in `loc_add_locale` (C3 in code review).
- **Issue**: Unity Localization accepts identifiers that .NET does not — e.g. `zh-Hant`, `zh-Hans` throw `CultureNotFoundException` on some Mono/.NET runtimes despite being legal IETF tags. A strict `CultureInfo.GetCultureInfo(code)` pre-check would block these.
- **Fix**: Soft warning, never hard reject. Try `CultureInfo.GetCultureInfo(code)` then `CultureInfo.GetCultureInfoByIetfLanguageTag(code)`; if both throw, attach a `warnings` field to the response but still create the locale. `Locale.CreateLocale` itself accepts arbitrary identifiers.

---

## Unity AssetDatabase

### [Pitfall] `AssetDatabase.AssetPathToGUID` is cached after deletion
- **Date**: 2026-04-13
- **Context**: Writing tests that assert "asset gone after delete".
- **Issue**: `AssetDatabase.AssetPathToGUID(path)` returns the cached GUID even after `AssetDatabase.DeleteAsset(path)` has run successfully. Asserting `== string.Empty` fails for genuinely-deleted assets.
- **Fix**: Use `AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) == null` to verify deletion. This actually probes the on-disk state and returns `null` once the asset is gone.

### [Pitfall] Use `AssetDatabase.CreateFolder`, not `Directory.CreateDirectory`
- **Date**: 2026-04-13
- **Context**: Auto-creating asset directories in `loc_create_table` / `loc_add_locale`.
- **Issue**: `Directory.CreateDirectory` creates the folder on disk but Unity doesn't know about it until `AssetDatabase.Refresh()`, which is two steps and leaves a window where `.meta` files don't exist. Anything that touches the folder GUID in that window gets an empty result.
- **Fix**: Walk the path and call `AssetDatabase.CreateFolder(parent, leaf)` for each missing segment. This atomically creates the folder + `.meta` and registers the GUID. See `LocTableHelper.EnsureFolderExists`.

---

## MCP Unity Tooling

### [Pitfall] `mcp__mcp-unity__run_tests` returns 0/0 when the consuming project lacks `testables`
- **Date**: 2026-04-13
- **Context**: Trying to run `LocTests` after adding 16 new test cases.
- **Issue**: Unity Test Framework does **not** discover tests inside packages by default. Both Test Runner UI and `run_tests` report 0 tests — even when the test asmdef compiles cleanly. The MCP tool returns a placeholder `"TestUnityMcp"` result with `testCount: 1, passCount: 0` which makes it look like a tool bug.
- **Fix**: In the consumer project's `Packages/manifest.json`, add the package to `testables`:
  ```json
  {
    "dependencies": { ... },
    "testables": [
      "com.gamelovers.mcp-unity"
    ]
  }
  ```
  After Unity reimports, both Test Runner UI and `run_tests` discover the package's test fixtures normally.

### [Pitfall] `recompile_scripts` does not trigger `AssetDatabase.Refresh`
- **Date**: 2026-04-13
- **Context**: Adding a new C# tool file to an existing sub-assembly.
- **Issue**: `mcp__mcp-unity__recompile_scripts` only recompiles already-known `.cs` files. New files added on disk after Unity started are invisible until `AssetDatabase.Refresh()` runs (e.g. via clicking the Project window or saving from Unity).
- **Fix**: After writing new `.cs` files via Edit/Write tools, call `mcp__mcp-unity__execute_menu_item` with `menuPath: "Assets/Refresh"` before `recompile_scripts`. This is the in-process equivalent of Ctrl+R and makes Unity scan new on-disk files.

### [Pitfall] New `.asmdef` files trigger phantom "0/0 tests" until `Assets/Refresh` runs
- **Date**: 2026-04-13
- **Context**: Creating `McpUnity.Addressables.Tests.asmdef` via the Write tool, then immediately running `run_tests` with filter on the new namespace.
- **Issue**: `recompile_scripts` compiled the new test `.cs` file without errors (because Unity saw it as "source in an unknown assembly" and folded it elsewhere), but `run_tests` returned the `TestUnityMcp` placeholder with `testCount: 1, passCount: 0` — the exact same symptom as "missing `testables`". Confusing because `testables` was correctly configured and the sibling `McpUnity.Localization.Tests` assembly was still running fine.
- **Root cause**: A newly-written `.asmdef` file is not registered as an assembly until `AssetDatabase.Refresh` scans it. `recompile_scripts` alone does not trigger that scan. The compiler ran, but Unity's assembly graph never learned the new test assembly existed.
- **Fix**: After writing a new `.asmdef`, call `execute_menu_item Assets/Refresh` **before** `recompile_scripts`. Only then does Unity register the new assembly and the test runner can discover its fixtures.
- **Diagnosis tip**: If `run_tests` returns the `TestUnityMcp` placeholder for a brand-new test assembly but an existing sibling assembly (e.g. `McpUnity.Localization.Tests`) works, the cause is almost certainly the new-asmdef-not-refreshed path, not missing `testables`.

### [Pitfall] `run_tests` runs against the *currently compiled* assembly, not the latest source
- **Date**: 2026-04-13
- **Context**: Iterating on a failing test — fixing source, re-running tests immediately.
- **Issue**: If you call `run_tests` between `Edit` and `recompile_scripts`, the runner uses the previously-compiled DLL and reports the same failure as before, masking the fix.
- **Fix**: Always sequence as `Edit → recompile_scripts → run_tests`. Don't trust a "still failing" result without a fresh recompile in between.

### [Confirmed] First-party sub-assembly auto-discovery requires `McpUnity.*` namespace prefix
- **Date**: 2026-04-13
- **Context**: Implementing the Localization sub-assembly pattern as a template for future optional-package integrations.
- **Finding**: `McpUnitySocketHandler.HandleListTools` excludes assemblies whose name starts with `McpUnity.*` from dynamic registration. This lets first-party sub-assemblies (which ship hand-written TS wrappers) coexist with third-party plugin assemblies (which use dynamic JSON Schema → Zod registration). The prefix is therefore a reserved namespace — third-party plugin authors must not use `McpUnity.*`.
- **See**: `CLAUDE.md` § "Adding a First-Party Optional Package Tool".

### [Pitfall] The Unity project running tests is the **consumer project**, not the package source folder
- **Date**: 2026-04-13
- **Context**: Writing Addressables fixture tests that referenced `Assets/Images/ginger.png` from the mcp-unity package's own `Assets/` folder.
- **Issue**: `AssetDatabase.AssetPathToGUID("Assets/Images/ginger.png")` returned empty even though `ls` showed the .png + .meta on disk. The fixture failed with "test asset does not exist" for every test. The cause: when mcp-unity is consumed as a package, the Unity project running the tests is a **separate consumer project** with its own `Assets/` folder. The package's own `Assets/Images/` is invisible to that Unity instance — it's just files in a sibling directory. Diagnosed by `get_editor_state` returning `Current Scene: Assets/Scenes/SampleScene.unity`, a path that doesn't exist in the mcp-unity repo.
- **Fix**: Tests that need data assets must **create their own dummy assets at fixture setup time** inside the consumer project's `Assets/` folder (e.g. `Assets/Tests/AddressablesTests/`), and delete them in teardown. Define the dummy `ScriptableObject` type inside the test assembly so it never ships in runtime builds. Reference: `AddrTestDummySO` in `Editor/Tests/Addressables/AddrTests.cs`.
- **Diagnosis tip**: If `AssetDatabase.AssetPathToGUID("Assets/whatever")` returns empty for a file that clearly exists on disk in the package repo, the file lives in the package's repo Assets folder — not in the consumer Unity project's Assets folder. The package's repo Assets folder is not part of any Unity project at runtime.

### [Pitfall] `mcp__mcp-unity__run_tests` with broad filters fails the WebSocket payload size limit
- **Date**: 2026-04-13
- **Context**: Running 61 Addressables tests via `testFilter: "McpUnity.Tests.Addressables"` to verify a stage milestone.
- **Issue**: First run completed and returned 60/61 passing. Subsequent runs (after fixing the one failure) timed out with `[MCP Unity] WebSocket error: An error has occurred in sending data` repeating in the Unity console every 18-30 seconds. Single-test runs (`testFilter: "...AddrTests.A1_..."`) worked fine on the same Unity session. The Unity test runner finishes cleanly but the response payload — even with `returnOnlyFailures: true, returnWithLogs: false` — exceeds whatever buffer the WebSocket layer can handle in one frame, so the result never reaches the Node side and the MCP request times out.
- **Fix**: For test classes with more than ~30 tests, run them **one at a time** by using fully-qualified `testFilter` (e.g. `McpUnity.Tests.Addressables.AddrTests.D17_SetEntry_RemoveLabels_StripsSpecified`). Tedious but reliable. Don't waste time retrying the broad filter — the failure is deterministic for large suites.
- **Diagnosis tip**: If `run_tests` times out but `get_editor_state` responds fine immediately afterwards, the WebSocket itself is healthy — it's the test response payload that's the issue. Drop to single-test filters.
