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
- **Fix**: After writing new `.cs` files via Edit/Write tools, expect to manually trigger an Asset Refresh in Unity (or call a tool that does it) before `recompile_scripts` will pick them up. For tests this is usually a non-issue because the next user interaction with Unity Editor refreshes implicitly.

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
