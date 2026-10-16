# Phase 0 — Restructure, hygiene, PlayMode test harness

## Context Links
- Prior plan: `plans/261016-1427-addressables-encryption-v1/plan.md`, `phase-v1.1-remote-encrypted-cache.md`
- Deleted tests: `git show 36ec1d1^:Packages/com.vanhaodev.addressablesencryption/Samples~/Demo/Tests/{EncryptedDemoPlayModeTests.cs,EncryptedRemoteCachePlayModeTests.cs,LocalBundleServer.cs,vanhaodev.addressablesencryption.samples.demo.tests.asmdef}`
- Deleted build driver: `git show 36ec1d1^:.../Samples~/Demo/Editor/EncryptedDemoBuildAndVerify.cs` (EnsureGroup / MakeRemote / UsePlayModeExistingBuild / Verify patterns)

## Overview
Priority P1 · Status pending · No behavior change. New folders, files under 200 lines, obfuscation exclusions, and regression tests that later phases rely on.

## Key Insights
- `EncryptedAssetBundleResource.cs` is 319 lines and over the limit. Everything else is under 200 (`BuildScriptPackedEncryptedMode.cs` is 187 and grows in Phases 2-3, so split it now).
- Types created by name from catalog/settings.json: `EncryptedAssetBundleProvider` (catalog provider data, `EncryptedAssetBundleProvider.cs:15`). Phase 3 adds `EncryptedBinaryCatalogProvider` and `EncryptedCatalogInitialization`. Renaming obfuscators would break them, so they need `[Obfuscation(Exclude = true, ApplyToMembers = true)]` plus the existing `[Preserve]`.
- `Downloads.IsEncrypted` compares `typeof(EncryptedAssetBundleProvider).FullName` (`AddressablesEncryption.Downloads.cs:92`). That is consistent even under renaming, but the catalog string was written at build time with the original name, so the exclusion is still needed.
- Old tests depended on 3 demo groups (EncryptedCube/PlainSphere/RemoteCapsule). Now there is 1 group "Demo" (entry `Hanni`, Texture2D, `Assets/AddressableAssetsData/AssetGroups/Demo.asset`) that points into the gitignored `Assets/Samples/` import.
- Tests in an embedded package (`Packages/...`) show up in Test Runner without `testables` (existing `Tests/Editor` already runs, EditMode 16/16). Add `"testables"` to manifest only if Runtime tests don't appear.
- The local scout hook blocks Bash paths with a `build` segment, so the folder is named `Pipeline/`.

## Requirements
- Functional: identical runtime/editor behavior. PlayMode suite restored: local async, sync (WaitForCompletion), reload, wrong key, no key, remote first-load/cache-hit, DownloadDependencies + GetDownloadSizeAsync, corrupted cache redownload, cache files encrypted with hashed names.
- Non-functional: each .cs under 200 lines. Meta GUIDs preserved.

## Architecture
```
Runtime/Scripts/
  AssemblyInfo.cs                     (+ InternalsVisibleTo tests.runtime)
  Api/AddressablesEncryption.cs, Api/AddressablesEncryption.Downloads.cs
  Format/EncryptedBundleFormat.cs
  Providers/EncryptedAssetBundleProvider.cs, Providers/ProvideHandleUtil.cs
  Providers/EncryptedAssetBundleResource.cs            (start, sources, complete/fail)
  Providers/EncryptedAssetBundleResource.Sync.cs       (partial: WaitForCompletion, GetProgress, GetDownloadStatus)
  Cache/EncryptedBundleCache.cs
  Download/EncryptedBundleWebRequest.cs
Editor/Scripts/
  Pipeline/BuildScriptPackedEncryptedMode.cs            (orchestration only)
  Pipeline/EncryptedBundleProviderAssigner.cs           (AssignBundleProviders + Restore, from :101-128, :59-63)
  Pipeline/EncryptedBundleFileEncryptor.cs              (EncryptBundles + DeletePlaintextBundles, from :134-185)
  Pipeline/EncryptedBundleFileNaming.cs                 (ConstructAssetBundleName logic + NameHash, from :71-96)
  Pipeline/NonEncryptedBuildWarning.cs
  Settings/AddressablesEncryptionSettings.cs
  Schema/EncryptedBundleGroupSchema.cs
  Validation/EncryptedBundleBuildValidator.cs
  Menus/AddressablesEncryptionSetupMenu.cs, Menus/AddressablesEncryptionFolderMenu.cs
Tests/Runtime/  (new asmdef com.vanhaodev.addressablesencryption.tests.runtime)
  EncryptedLocalBundlePlayModeTests.cs, EncryptedRemoteCachePlayModeTests.cs,
  LocalBundleServer.cs, PlayModeTestContentBuild.cs (IPrebuildSetup/IPostBuildCleanup, #if UNITY_EDITOR)
```
Test data flow: `PlayModeTestContentBuild.Setup()` (Editor, before play mode):
1. `AddressablesEncryptionSetupMenu.Setup(settings)`. The key must be `demo-secret` (Secret format); warn and fail if different.
2. Assert the Demo group has the `Hanni` entry and its asset exists. If not: fail "Import the Demo sample first".
3. Create `Assets/AddressablesEncryptionTestContent/` (gitignored) holding a generated 256x256 PNG `RemoteTexture.png`.
4. Create group `AE Test Remote` (Encryption + Use Asset Bundle Cache) with address `TestRemoteTexture`. Remote build/load paths; profile `Remote.LoadPath = http://localhost:8765/[BuildTarget]`.
5. Play Mode Script = Use Existing Build, then `BuildPlayerContent`.
6. `Cleanup()` removes the test group and folder, restores the old Remote.LoadPath and play-mode builder index.

Remote tests: `LocalBundleServer` serves `ServerData/<BuildTarget>/` on :8765 and counts requests (unchanged code).

## Related Code Files
- Move (git mv + .meta): all 9 runtime .cs and 7 editor .cs listed above.
- Modify: `Runtime/Scripts/AssemblyInfo.cs`, `Tests/Editor/com.vanhaodev.addressablesencryption.tests.editor.asmdef` (add refs `com.vanhaodev.addressablesencryption.editor`, `Unity.Addressables`, `Unity.Addressables.Editor`, `Unity.ResourceManager`, `Unity.ScriptableBuildPipeline`, so Phases 2 and 4 need no asmdef edits), `Editor/Scripts/...` (InternalsVisibleTo the tests.editor asmdef via a new `Editor/Scripts/AssemblyInfo.cs`), `.gitignore` (+`/Assets/AddressablesEncryptionTestContent*`).
- Create: `Providers/EncryptedAssetBundleResource.Sync.cs`, 3 Pipeline helper files, `Tests/Runtime/*` (4 .cs + asmdef). asmdef refs: runtime, `Unity.Addressables`, `Unity.ResourceManager`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `Unity.Addressables.Editor`, `com.vanhaodev.addressablesencryption.editor`, nunit, `UNITY_INCLUDE_TESTS`.
- Add `[System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]`: `EncryptedAssetBundleProvider` (`EncryptedAssetBundleProvider.cs:15`). Same rule for Phase 3 types.

## Implementation Steps
1. `git mv` runtime files into Api/Format/Providers/Cache/Download (with .meta). Create folder .meta by letting Unity import, or write them with new GUIDs.
2. `git mv` editor files into Pipeline/Settings/Schema/Validation/Menus.
3. Split `EncryptedAssetBundleResource` into a partial pair. Move lines 264-317 (progress, status, WaitForCompletion) to `.Sync.cs`.
4. Extract the 3 Pipeline helpers from `BuildScriptPackedEncryptedMode` as internal static classes. The builder only calls them.
5. Add the Obfuscation attribute. Add the comment "created by type name from the catalog; exclude from renaming obfuscators".
6. Restore the PlayMode tests: rename the namespace to `vanhaodev.addressablesencryption.Tests.Runtime`. Local fixture uses `Hanni` (`LoadAssetAsync<Texture2D>`). The plain-group assertion is dropped (no plain group). Remote fixture uses `TestRemoteTexture`. Add `[PrebuildSetup(typeof(PlayModeTestContentBuild))]` and `[PostBuildCleanup(...)]` on both fixtures.
7. Compile (open Editor or batchmode EditMode run), then run EditMode + PlayMode.

## Todo List
- [ ] git mv runtime (9) + editor (7) with .meta
- [ ] Split resource partial; extract 3 Pipeline helpers
- [ ] Obfuscation attribute on provider
- [ ] Tests/Editor asmdef refs; Editor AssemblyInfo
- [ ] Tests/Runtime asmdef + 4 files; .gitignore
- [ ] EditMode 16/16, PlayMode 9/9 green

## Success Criteria
- `git status` shows renames (R), not delete+add, for every moved file. `Demo_EncryptedBundleGroupSchema.asset` and `BuildScriptPackedEncryptedMode.asset` still resolve (no "missing script").
- `wc -l` on all package .cs: max < 200.
- EditMode passes (16). PlayMode passes (5 local + 4 remote) from a clean `Library/com.unity.addressables` with a single command.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Moving files breaks serialized refs | L×H | git mv .meta along; verify assets in inspector |
| IPrebuildSetup building Addressables in batch is slow or flaky | M×M | Same calls as old BuildAndVerify; log the build error and fail fast |
| Prebuild changes the dev project's profile or groups | M×L | Cleanup restores them; test content in a gitignored folder |
| Runtime test asmdef references editor assemblies | L×M | `#if UNITY_EDITOR` around the prebuild class; `UNITY_INCLUDE_TESTS` keeps it out of players |

## Security Considerations
Test key `demo-secret` only. No real keys in tests. Obfuscation exclusion only affects names, not key handling.

## Next Steps
Phase 1 (stream decryption) relies on these tests. Phase 4 can start now.
