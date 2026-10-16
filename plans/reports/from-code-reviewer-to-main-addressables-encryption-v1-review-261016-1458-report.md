# Code Review: Addressables Encryption v1

## Scope
- Package: `Packages/com.vanhaodev.addressablesencryption` (Runtime 5 files, Editor 5 files, Tests 1). About 900 LOC.
- Checked against Addressables 4.1.1 source (scratchpad copy, which matches `Library/PackageCache/com.unity.addressables@046549581c8a/Editor`).
- Accepted v1 decisions were not reported (no cache, DL no-op, WebGL, sync unload, catalog/key exposure, CRC, BundleSize).

## Overall
The runtime state machine is solid. The `_completed`, `_request == request` and `_loadRequest` guards correctly cover async/sync interleavings, and the normal full-build path is correct. The real defects are all in the **content-update pipeline** and in **builder switching**. Two of them silently ship plaintext bundles, which defeats the anti-ripper goal.

---

## High

### H1. Content update with an encrypted group crashes mid-encryption and leaves plaintext bundles in ServerData
- Stock `RevertUnchangedAssetsToPreviousAssetState.ApplyAssetEntryUpdates` calls `File.Delete(operation.CurrentBuildPath)` (RevertUnchangedAssetsToPreviousAssetState.cs ~L416) for bundles whose entries revert to the previous build. This is the normal case for "Prevent Updates" (static) groups with unchanged assets.
- That path is still listed in `AssetBundleBuildResults`. The entry is added in `BundledAssetSchemaBuilder.PostProcessBundles` L795, before the revert runs.
- `BuildScriptPackedEncryptedMode.EncryptBundles` L84 `File.ReadAllBytes(path)` then throws `FileNotFoundException`. `BuildScriptBase.BuildData` catches it and returns an error result. By then the catalog is already written and **every bundle after the missing one in the list is still plaintext** in the remote build path. If CI or a person uploads ServerData anyway, players get plaintext bundles that the catalog routes to the encrypted provider ("missing header" failures), and the assets are exposed.
- Fix:
  - Skip missing files with `if (!File.Exists(path)) continue;`.
  - Do a pre-pass so encryption is all-or-nothing.
  - On any exception, delete the plaintext outputs of encrypted groups rather than leaving them on disk.

### H2. "Check for Content Update Restrictions" moves encrypted entries into an unencrypted group, so they ship as plaintext
- `ContentUpdateScript.CreateContentUpdateGroup` (ContentUpdateScript.cs L1212-1221) creates a new remote group with only `BundledAssetGroupSchema` and `ContentUpdateGroupSchema`. It does not add `EncryptedBundleGroupSchema`.
- Result: modified assets from an encrypted static group are rebuilt into a **plain** bundle. `AssignBundleProviders` gives that group the stock provider, so loading works and nobody notices the leak.
- Fix: in `BuildDataImplementation`, when `builderInput.PreviousContentState != null`, look up each entry's `cachedInfos[guid].groupGuid`. If that group was encrypted and the entry's current group is not, fail the build (or warn). The schema could also be auto-added through `settings.OnModification` on `EntryMoved`.

### H3. Switching to the stock "Default Build Script" ships plaintext bundles that are wired to the encrypted provider
- `AssignBundleProviders` writes `EncryptedAssetBundleProvider` into the `BundledAssetGroupSchema` permanently (L65), and only the encrypted builder ever reverts it.
- One click in the Groups window (Build > New Build > Default Build Script), or a CI script that calls `BuildScriptPackedMode` directly, then produces plaintext bundles whose catalog entries still point to `EncryptedAssetBundleProvider`. Every load fails with "missing header", and the plaintext ships.
- Fix: assign the provider in a try/finally and restore the original type after `base.BuildDataImplementation`. Add an `[InitializeOnLoad]` hook on `BuildScript.buildCompleted` that errors when groups with an enabled Encryption schema were built by a builder that is not `BuildScriptPackedEncryptedMode`.

## Medium

### M1. Content update with an encrypted shared-bundle group skips the builtin/monoscripts revert
- `RevertBundleByNameContains` only matches catalog entries whose `Provider == "UnityEngine...AssetBundleProvider"` (L25, L95). If it finds no match, it does `return true` silently.
- The builtin and monoscripts bundles take the provider of `GetSharedBundleGroup()` (GenerateLocationListsTask L178/L353), and that group is usually the Default Local Group. If that group is encrypted and static, the revert is skipped. When the monoscripts or builtin-shader bundle hash changes (a script was added, or a shader is newly used), the update catalog points at a new **local** bundle that the shipped player does not have. Every dependent load fails.
- Fix: document "do not encrypt the shared bundle group", or warn when `GetSharedBundleGroup()` is encrypted.

### M2. Enabling or disabling encryption on an already-shipped group is not content-update safe
- `ApplyAssetEntryUpdates` reverts the catalog `InternalId` and `Data` to the previous bundle but keeps the **current** provider. If you add the schema to a group that already shipped (static, unchanged entries), the old plaintext bundle in the player is loaded by `EncryptedAssetBundleProvider` and fails. Removing the schema causes the reverse failure.
- Fix: on a content-update build, fail when an encrypted group's encryption state differs from the previous content state.

### M3. `Timeout` is applied as a total request timeout; stock treats it as a no-progress timeout
- `EncryptedAssetBundleResource.cs:87` sets `request.timeout = _options.Timeout`. Stock `AssetBundleResource` (L394-408, L965-1007) times out only after N seconds **without progress**.
- With the default group Timeout (for example 30 s), any remote bundle that takes more than 30 s to download on a slow network always fails, even while it is still downloading. Retries restart from zero and fail the same way.
- Fix: track `downloadedBytes` changes per frame (an `IUpdateReceiver` or the progress callback) and abort on stall, matching stock.

### M4. Remote requests bypass `WebRequestQueue` and retry on any error
- Stock goes through `WebRequestQueue.QueueRequest` (`MaxConcurrentWebRequests`). Here, N parallel encrypted remote loads mean N simultaneous UWRs, each holding the whole bundle in memory, which stacks with M5.
- `OnRequestDone` (L119) retries 404/403 errors too. Stock uses `ShouldRetryDownloadError`.

### M5. The memory peak is higher than the plan's "2-3x", and the managed heap does not shrink
- Local load: `ReadAllBytes`, then the `payload` copy (EncryptedBundleFormat.cs:57), then the `ciphertext` copy inside `AesEncryption.Decrypt`, then the plaintext. That is about 4N of managed LOH allocations at the same time. Remote adds `downloadHandler.data` plus the native buffer.
- Unity's Boehm GC rarely gives pages back to the OS, so one 300 MB bundle can permanently grow the managed heap by more than 1 GB. On mobile that is likely OOM.
- Separately, `LoadFromMemoryAsync` keeps the whole bundle resident natively for its lifetime. `LoadFromFile` pages on demand.
- Cheap fix: decrypt directly from the file using a `FileStream` positioned at offset 5, reading the IV, then `CryptoStream` into a single pre-sized `byte[]`. That gives about 1N managed.
- v2: AES-CTR plus `AssetBundle.LoadFromStreamAsync` to avoid full residency.

### M6. `GetDownloadSizeAsync` never reaches 0 for remote encrypted bundles
This follows from the accepted no-cache decision, but the effect may not be obvious. `AssetBundleRequestOptions.ComputeSize` (L341-349) returns `BundleSize` whenever `Caching` has no entry, and these bundles are never in it. The standard pattern "size > 0 → show download prompt → DownloadDependenciesAsync → re-check" loops or always prompts. This should be documented loudly, or `ComputeSize` should return 0 via an options subclass.

## Low
- **L1:** A TPL continuation (`ContinueWith`, L142) swallows any exception thrown in `OnDecrypted` (for example from `LoadFromMemoryAsync`). The operation would then hang forever. Wrap the body in try/catch and call `Fail(e)`.
- **L2:** Exiting play mode while a decrypt task is pending runs the continuation in edit mode and loads a bundle with no owner. The next play session can then hit "AssetBundle already loaded". Stock handles this in `OnPlayModeStateChanged`. Fix: guard with `Application.isPlaying` / an RM-alive check.
- **L3:** A file shorter than 5+16 bytes passes `IsEncrypted`. `AesEncryption.Decrypt` then does `new byte[negative]` and throws `OverflowException`, which gives an unclear error. Add a length check.
- **L4:** `AssignBundleProviders` reverts non-encrypted groups to stock `AssetBundleProvider` instead of `settings.AssetBundleProviderType`, which drops a custom settings-level provider. It also silently overrides a custom provider on an encrypted group; a warning would help.
- **L5:** If the base build returns an `Error` after the bundles were moved, the plaintext bundles of encrypted groups stay in the BuildPath. This is the same remedy as H1.

---

## Answers to the specific questions

**Q1. Can the pipeline leave bundles unencrypted or encrypt the wrong file?**
- Full build, "Build Addressables on Player Build" (`AddressablesPlayerBuildProcessor`), and `BuildPlayerContent` are all fine. They use `ActivePlayerDataBuilder`, and a builder error raises `BuildFailedException`.
- Build cache and bundle move are fine. SBP output goes to `bundleBuildPath` fresh. `MoveFileToDestinationWithTimestampIfDifferent` (L603-631) skips only when the timestamps are equal, and the encryption write always changes the dest mtime, so a stale encrypted file is never kept over a new plaintext one. The `IsEncrypted` skip is purely defensive.
- NoHash naming is fine because `FilePath` is already stripped and the move callbacks run inside the base build.
- **Content update is broken (H1, H2, M1, M2). Switching builders is broken (H3).**

**Q2. Re-entering a released or pooled ProvideHandle: is it safe?**
- Yes. `ProvideHandle.IsValid` is version-based (IResourceProvider.cs:42), and the version only increments in `ProviderCompleted`.
- A running op holds its own reference until completion (`AsyncOperationBase.Start` L549, `IncrementReferenceCount`), so a user release cannot destroy an in-flight provider op. Release-during-load goes through completion, then `Destroy`, then `Provider.Release`, then `Unload`, which is correct.
- The `else Unload()` branch in `OnLoaded` is effectively unreachable but harmless.
- Pooling (`LRUCacheAllocationStrategy` is opt-in; the default does not pool) cannot reuse an op with a stale handle that matches, because reuse only happens after completion, which has already bumped the version.
- Fail, sync-after-async, and retry ordering are all guarded correctly.

**Q3. "AssetBundle already loaded" on reload?**
- No for the same provider. `Unload(true)` is synchronous and finishes before `Release` returns, so there is no in-flight unload window and no need for something like `s_UnloadingBundles`.
- Only cross-provider cases can collide: a stock-provider `UnloadAsync` still in flight for the same bundle content while the encrypted provider loads it (for example after a content update that toggled encryption on a NoHash group), and the edit-mode leak in L2.

**Q4. AssetBundleProviderType persistence and interactions?**
- It persists. The setter calls `SetDirty(true)` (BundledAssetGroupSchema.cs L767-775), so group assets show as modified after the first encrypted build.
- The getter ignores `UseDefaultSchemaSettings`, so there is no conflict.
- The settings-level `AssetBundleProviderType` setter or inspector (`UpdateAssetBundleProviderType`, AddressableAssetSettings.cs L1010-1020) overwrites the provider on all enabled groups, including encrypted ones. The next encrypted build corrects it, so this is harmless.
- `ForceUniqueProvider` works (id `FullName_guid`, and the base `Initialize` sets it).
- The real hazard is persistence plus a non-encrypting builder (H3), and the revert target (L4).

## Positive
- The ordering is careful: the key and streaming-audio checks fail before any bundle is written, and providers are assigned before the catalog is generated.
- The volatile `_aes` swap with a per-call snapshot is thread-safe, and `Aes.Create()` per call avoids shared transform state.
- Request identity checks handle both retry and the sync-wait race. The sync path uses `assetBundle` access to force completion, the same as stock.
- The `[Preserve]` attribute on the provider and its constructor protects against IL2CPP stripping.

## Recommended actions (priority order)
1. H1: make `EncryptBundles` skip missing files, encrypt all-or-nothing, and clean up plaintext on failure.
2. H3: assign and restore providers with try/finally, and add a `BuildScript.buildCompleted` guard against non-encrypting builders.
3. H2 and M2: add content-update checks (an entry left an encrypted group; a group's encryption state changed against the previous state).
4. M3: switch to a stall-based timeout. M4: use `WebRequestQueue` and `ShouldRetryDownloadError`.
5. M5: stream-decrypt into one buffer.
6. M1 and M6: document them, or warn at build time.
7. L1 to L3: add a try/catch in the continuation, a play-mode guard, and a length check.

## Metrics
- Compile: clean (per caller). Tests: 8 EditMode format/key tests, not run. There is no test coverage for the provider, the builder, or content update.
- Linting: n/a.

## Unresolved questions
- Will the package tests run without a `testables` entry in `Packages/manifest.json`? This is the embedded-package case; please verify in Test Runner.
- VideoClips in encrypted groups: does VideoPlayer work with bundles loaded via `LoadFromMemory` on all target platforms? It may need a validator rule like the one for streaming audio.
- Does `LoadFromMemoryAsync` of an LZMA-compressed bundle recompress or decompress fully in native memory on 6000.3? This affects the M5 estimate. Recommend LZ4 for encrypted groups.

**Status:** DONE_WITH_CONCERNS
**Summary:** The runtime provider is correct for the async, sync and release paths (Q2 and Q3 are safe). The build pipeline is correct for full builds, but content-update builds and builder switching can leave or ship plaintext bundles (H1-H3).
**Concerns/Blockers:** H1-H3 undermine the anti-ripper goal silently, so fix them before release. M3 (total timeout) will break large remote bundles on slow networks.
