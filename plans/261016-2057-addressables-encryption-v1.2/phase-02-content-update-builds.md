# Phase 2 — Content update builds with encrypted groups

## Context Links
- Throw today: `Editor/Scripts/BuildScriptPackedEncryptedMode.cs:37-39` (`PreviousContentState != null`)
- Provider assign/restore: `BuildScriptPackedEncryptedMode.cs:47,59-63,101-128` (→ `Pipeline/EncryptedBundleProviderAssigner.cs` after Phase 0)
- Addressables 4.1.1 (`Library/PackageCache/com.unity.addressables@046549581c8a/Editor/Build/`):
  - `ContentUpdateScript.cs:758-777` BuildContentUpdate → `settings.ActivePlayerDataBuilder.BuildData` with `PreviousContentState`
  - `ContentUpdateScript.cs:181-198` CachedBundleState = {bundleFileId, data}. **No provider stored.** `:134` CachedAssetState.groupGuid
  - `ContentUpdateScript.cs:1212-1221` CreateContentUpdateGroup: new group gets Bundled + ContentUpdate schemas only, then `MoveEntries`
  - `RevertUnchangedAssetsToPreviousAssetState.cs:407-425` ApplyAssetEntryUpdates: deletes the new bundle file, sets catalog entry InternalId and Data to the previous ones. **Provider untouched.**
  - `RevertUnchangedAssetsToPreviousAssetState.cs:25,95` builtin/monoscripts revert only when Provider == stock `AssetBundleProvider` id

## Overview
Priority P1 · Status pending. "Update a Previous Build" works for encrypted groups. Reverted static bundles keep our provider and their encrypted file. The auto "Content Update" group inherits Encryption. The build fails loudly whenever a plaintext ship is possible.

## Key Insights
- (a) Provider is not persisted in `addressables_content_state.bin`. The catalog entry provider comes from the current group schema, which `AssignBundleProviders` sets before base build. Reverts only swap InternalId/Data, so reverted encrypted bundles keep `EncryptedAssetBundleProvider`. The previous file (encrypted by the previous build, hashed name) is reused as is. `EncryptBundles` already skips missing files (deleted by the revert) and AAEN files.
- Hidden failure: a static group that had Encryption **added after** the shipped build reverts to a plain previous file loaded by our provider ("not encrypted" at runtime). The reverse (Encryption removed) gives an AAEN file with the stock provider. Both are detectable before the build from content state + files on disk.
- (b) The Content Update group is created before our builder runs, without Encryption. The source group can be recovered at build time: `cachedInfos[i].asset.guid` → `groupGuid` (previous group). No editor event carries the source (`AddressableAssetSettings.MoveEntries` :2732-2742 fires EntryMoved after the move). So inherit in the builder.
- Shared/builtin bundles: if `settings.DefaultGroup` is encrypted and static, `unitybuiltinassets`/`_monoscripts` are not reverted (`:95` provider check). Old players would then need a new local bundle they don't have. Fail loudly (Q2 in plan.md).

## Requirements
- Functional: content update builds succeed. New group from Check for Content Update Restrictions gets the Encryption schema (copy `HashBundleFileNames` from the source). Pre-build check rejects: (1) encrypted source entries in a non-encrypted group after inheritance, (2) previous bundle format ≠ current group encryption, (3) encrypted static Default group.
- Non-functional: no change to full builds. Inheritance logged (`Debug.Log` naming group + source).

## Architecture
New `Editor/Scripts/Pipeline/ContentUpdateEncryptionGuard.cs` (internal static):
- `InheritEncryptionSchema(settings, previousState)`: map assetGuid → previous groupGuid. For each group without enabled Encryption, if any entry (GatherAllAssets incl. folder children) maps to a group that **is** encrypted now → `group.AddSchema<EncryptedBundleGroupSchema>()`, copy HashBundleFileNames, `SetDirty`. Returns the list of changed groups.
- `ValidateNoPlaintextShip(settings, previousState, encryptedGroups)`: throws InvalidOperationException listing offenders for rules (1)-(3). Rule (2): for each `cachedBundles` entry whose group (via cachedInfos bundleFileId → groupGuid) is static: previous build path = `bundleFileId.Replace(loadPath, buildPath)` (same logic as `BundleIdToBuildPath` :240-247, which is internal, so reimplement in ~6 lines). If the file exists, `IsEncrypted(header)` must equal `IsEncrypted(group)`. If missing → warning only (Addressables also warns).

Builder flow (`BuildDataImplementation`):
```
if PreviousContentState != null:
    ContentUpdateEncryptionGuard.InheritEncryptionSchema(...)
encryptedGroups = groups.Where(IsEncrypted)   // after inheritance
if encryptedGroups > 0: key, streaming-audio check,
    if PreviousContentState != null: ValidateNoPlaintextShip(...)
assign providers → base build (reverts happen inside) → EncryptBundles (skip missing/AAEN) → restore providers
```
Header check helper: `EncryptedBundleFormat.IsEncryptedFile(path)` reads 5 bytes. It replaces `File.ReadAllBytes` in `EncryptBundles`'s `DeletePlaintextBundles` too (cheaper).

## Related Code Files
- Modify: `Pipeline/BuildScriptPackedEncryptedMode.cs` (remove the throw, call the guard), `Format/EncryptedBundleFormat.cs` (+`IsEncryptedFile`), `Pipeline/EncryptedBundleFileEncryptor.cs` (use IsEncryptedFile).
- Create: `Pipeline/ContentUpdateEncryptionGuard.cs`, `Tests/Editor/ContentUpdateEncryptedBuildTests.cs`.

## Implementation Steps
1. `IsEncryptedFile(path)` + unit test.
2. Guard class: Inherit + Validate (rules 1-3), clear messages ("make a full build" / "remove Encryption from Default group or set it Can Change Post Release").
3. Wire into the builder; delete the throw (`:35-39` comment updated).
4. EditMode test (batchmode-friendly; backs up and restores `Assets/AddressableAssetsData/Windows/addressables_content_state.bin` and the active builder):
   - Temp folder with 2 generated PNGs A, B. Group `AE CU Test`: Encryption, Pack Separately, static (ContentUpdateGroupSchema.StaticContent = true), local paths.
   - Full build via `AddressableAssetSettings.BuildPlayerContent`. Record the bundle paths of A and B (AAEN).
   - Modify A (rewrite pixels, `AssetDatabase.ImportAsset`). `ContentUpdateScript.GatherModifiedEntriesWithDependencies` → `CreateContentUpdateGroup(settings, keys, "Content Update")`.
   - `ContentUpdateScript.BuildContentUpdate(settings, statePath)`. Assert no error. New group has an enabled Encryption schema. The new A bundle exists and starts with AAEN. B's previous bundle file still exists and is AAEN.
   - "Old one still loads": read B's file, decrypt with `EncryptedBundleFormat.Decrypt` (build key), `AssetBundle.LoadFromMemory` non-null, contains B's texture, unload.
   - Negative: remove Encryption from `AE CU Test`, content update → result.Error contains "previous bundle" (rule 2).
   - TearDown: remove groups and folder, restore state file, `AssetDatabase.Refresh`.
5. Run EditMode + PlayMode (regression).

## Todo List
- [ ] IsEncryptedFile
- [ ] ContentUpdateEncryptionGuard (inherit + 3 rules)
- [ ] Builder wiring, throw removed
- [ ] EditMode content update test (+ negative)
- [ ] Full suite green

## Success Criteria
- Content update test passes in batchmode. Full-build tests unchanged.
- Manual: Groups window > Check for Content Update Restrictions > the created group shows Encryption after Build > Update a Previous Build. Console logs the inheritance line.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Inheritance encrypts a group the user meant plain | L×M | Only on content-update builds, only for entries whose previous group is encrypted; logged |
| Guard misses a revert path (builtin duplicate `_remote.bundle` :254-284) | M×M | That copy is the builtin bundle of the Default group, covered by rule 3 |
| Test pollutes dev project state | M×M | Backup/restore content state; unique temp names; TearDown in finally |
| Path mapping differs from Addressables internal | L×M | Missing file → warning only; the test covers the local path case |

## Security Considerations
Rules 1-2 prevent plaintext shipping of formerly encrypted assets. No key in content state (only hashed bundle ids + options).

## Next Steps
Phase 3 (catalog) builds on the same builder entry points.
