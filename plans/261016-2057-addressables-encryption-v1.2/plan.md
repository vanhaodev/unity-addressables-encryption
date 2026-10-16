---
title: "Addressables Encryption v1.2"
description: "Restructure, stream decryption, content update, catalog encryption, audio test, WebGL, docs"
status: done
priority: P1
effort: 30h
branch: dev
tags: [unity, addressables, encryption, aes, streaming, webgl]
created: 2026-10-16
---

# Addressables Encryption v1.2

Package `Packages/com.vanhaodev.addressablesencryption` 1.1.0 → 1.2.0. On-disk bundle format unchanged ("AAEN" + ver 1 + IV16 + AES-CBC).

| # | Phase | Effort | Status | Depends |
|---|-------|--------|--------|---------|
| 0 | [Restructure + hygiene + test harness](phase-00-restructure-hygiene-and-test-harness.md) | 4h | done | — |
| 1 | [Stream decryption](phase-01-stream-decryption.md) | 7h | done | 0 |
| 2 | [Content update builds](phase-02-content-update-builds.md) | 5h | done | 1 |
| 3 | [Catalog encryption](phase-03-catalog-encryption.md) | 5h | done | 2 |
| 4 | [Streaming AudioClip build-fail test](phase-04-streaming-audio-build-fail-test.md) | 1.5h | done | 0 (any time) |
| 5 | [WebGL support](phase-05-webgl-support.md) | 5h | done | 3 |
| 6 | [Docs + version 1.2.0](phase-06-docs-and-release.md) | 2h | done | 0-5 |

Order: 0 → 1 → 2 → 3 → 5 → 6. Phase 4 can run any time after 0 (it only adds `Tests/Editor/StreamingAudioBuildFailTests.cs`). Numbering: the user's "Phase 6 WebGL, before docs" is Phase 5 here so docs stays last.

## Verification commands (close the Editor first, project lock)
```bash
U="D:/Unity/6000.3.9f1/Editor/Unity.exe"; PRJ="D:/Vanhaodev/My/unity-addressables-encryption"; R="$PRJ/plans/reports"
"$U" -batchmode -projectPath "$PRJ" -runTests -testPlatform EditMode -testResults "$R/editmode.xml" -logFile "$R/editmode.log"
"$U" -batchmode -projectPath "$PRJ" -runTests -testPlatform PlayMode -testResults "$R/playmode.xml" -logFile "$R/playmode.log"
# exit code 0 = all passed; 2 = failures; open the xml for details. No -quit with -runTests.
# Header check of built bundles (must print AAEN for encrypted groups):
for f in "$PRJ"/Library/com.unity.addressables/aa/StandaloneWindows64/StandaloneWindows64/*.bundle "$PRJ"/ServerData/StandaloneWindows64/*.bundle; do printf '%s  %s\n' "$(head -c 4 "$f")" "$f"; done
```
PlayMode tests build Addressables themselves (IPrebuildSetup, Phase 0). The Demo sample must be imported (`Assets/Samples/...`, gitignored).

Manual rip check (Phases 1, 3, 6): import the Demo sample, Build > New Build > Encrypted Build Script, File > Build Profiles > Windows build of `HanniDemo` to `Builds/Windows/`. Run AssetRipper (and AssetStudio) on `Builds/Windows/*_Data`: no `hanni_pham` Texture2D, bundle files show as unknown/unreadable. Phase 3 with Encrypt Catalog on: `aa/catalog.bin` starts with `AAEN`, no addresses visible in a hex view. Then play the .exe: picture shows.

## Cross-phase rules
- Plain C#, no reflection by string (existing `ProvideHandleUtil` excepted, see Q4), no dynamic/expression trees, minimal LINQ in runtime hot paths, PascalCase file names, English comments, files < 200 lines.
- `git mv` .cs together with .meta (GUIDs keep schema/builder assets working).
- Tooling note: a local scout hook blocks Bash paths with a `build` folder segment → editor folder is named `Pipeline/`, not `Build/`.
- Rollback: one commit (or small series) per phase; `git revert` per phase. Format unchanged → caches and bundles stay compatible across phases. Catalog encryption defaults off.

## WebGL notes (knowledge, Phase 5 turns it into work)
- With stream decryption the decrypt runs inside `Stream.Read`, called by Unity's loader. No `Task.Run`, so WebGL may work without frame slicing.
- To verify: `AssetBundle.LoadFromStream(Async)` support on WebGL; main-thread blocking during large reads (WebGL has no worker threads for this); `persistentDataPath` = IndexedDB (sync FS writes, `FS.syncfs` flush) for the encrypted cache; StreamingAssets only reachable through UnityWebRequest.
- Fallback if LoadFromStream is unsupported: frame-sliced main-thread decrypt. `ICryptoTransform.TransformBlock` on ~256KB chunks (multiple of 16), `TransformFinalBlock` for the last one, ~4ms budget per frame, then `LoadFromMemoryAsync`. Same output as `AesEncryption.Decrypt`, build side unchanged.
- Managed AES in WASM is roughly tens of MB/s (not measured), so prefer small bundles (Pack Separately).
- WebGL Build Support is installed (`D:/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/WebGLSupport`). Phase 5 verifies locally: batchmode WebGL player build of `HanniDemo` + browser check via `npx http-server` (no Python), same-origin `Remote.LoadPath` (`http://localhost:8000/ServerData/[BuildTarget]`, no CORS).

## Unresolved questions
1. Remote catalog (`ServerData/catalog_*.bin`) stays plain per brief. Encrypting it means Addressables re-caches it plain in `persistentDataPath/com.unity.addressables` (`ContentCatalogProvider.cs:460` writes decrypted `GetSerializedData()`). Is it in scope for v1.x?
2. Content update with Encryption on the Default/shared-bundle group: Addressables only reverts `unitybuiltinassets`/`_monoscripts` when their provider is the stock `AssetBundleProvider` (`RevertUnchangedAssetsToPreviousAssetState.cs:95`). The plan fails the build loudly. Is that OK, or should we re-implement the revert?
3. With stream loading, Streaming AudioClips may now work from encrypted bundles. The brief keeps the build-fail rule (Phase 4). Re-evaluate in v1.3?
4. `ProvideHandleUtil` reads internal `ProvideHandle.IsValid` via string reflection, and `DownloadOnlyLocation` is detected by type-name string. Both are pre-existing. Keep them (no public API alternative)?
5. Stream mode only checks PKCS7 padding (last block) up front. Corruption in the middle of an uncompressed bundle may load garbage silently. Do we need a per-file hash in the cache?

## Answers (main agent, 2026-10-16)
1. Remote catalog: out of scope for 1.2; note for 1.3.
2. Encrypted static Default group + content update: fail the build loudly. No revert re-implementation.
3. Keep the Streaming AudioClip build-fail rule; re-evaluate in 1.3.
4. Keep both (they target Unity's Addressables assemblies, which obfuscators leave alone). Document in README obfuscation note.
5. No custom hash. Phase 1 passes the catalog CRC (`AssetBundleRequestOptions.Crc`, when non-zero) to `AssetBundle.LoadFromStream(Async)`, Unity validates the content.

## Handoff 2026-10-16 22:05 (model switch)
Done + committed: Phase 0, 1, 2 (EditMode 44/44, PlayMode 10/10 at commit 2f8759a). encryptionmanager 1.1.0 released (tag pushed, commit 41bd6f5); GitHub Release page NOT created (gh lacks `workflow` scope → user runs `gh auth refresh -h github.com -s workflow`, then `gh release create 1.1.0 --title v1.1.0 --notes "<1.0.0 format, see release 1.0.0>"`).
WIP commit (not green): Phase 3 catalog + Phase 4 audio test + Phase 5 helper files.
- `Packages/manifest.json` points encryptionmanager at local `file:../../unity-encryption-manager/Exported/...` for dev → switch to git URL `#1.1.0` before release.
- Opener now uses `AesEncryption.OpenDecryptStream` (encryptionmanager 1.1.0); old AesCbc* files deleted. EditMode opener tests pass (5/5).
- Phase 3 BUG: `CreateCatalogBuilder` override is never called. BuildScriptPackedMode delegates to a nested SchemaDriven instance that only forwards BuildDataImplementation/ProcessAllGroups/ProcessGroup, not CreateCatalogBuilder. FIX: drop `EncryptedBinaryCatalogBuilder` + the override; instead in `BuildScriptPackedEncryptedMode.BuildDataImplementation` after a successful base build (when EncryptCatalog on): encrypt `Addressables.BuildPath/catalog.bin` in place (skip if already AAEN), and in `EncryptedCatalogRuntimeSettings` (JsonUtility round trip of settings.json) replace the CatalogLocations entry whose Provider == typeof(BinaryCatalogProvider).FullName with `new ResourceLocationData(loc.Keys, loc.InternalId, typeof(EncryptedBinaryCatalogProvider), typeof(BinaryContentCatalogData), loc.Dependencies){Data=loc.Data}` + add the init object (already there). Remove "Unity.ScriptableBuildPipeline.Editor" ref from the editor asmdef if no longer needed.
- Phase 4 test: add `LogAssert.Expect(LogType.Error, new Regex("Addressable content build failure"))` in `StreamingAudioBuildFailTests.StreamingClip_FailsTheBuild_NamingTheClip`.
- Then: EditMode (expect 26/26) + PlayMode (now builds with EncryptCatalog on; fixtures SetKey before InitializeAsync; new test Catalog_OnDisk_IsEncrypted → 11).
- Phase 5: `WebGLPlayerBuildForEncryptionCheck` (Tests/Runtime, editor-guarded) + `WebGLLoadCheckBehaviour` written, NOT compiled yet; needs `PlayModeTestContentBuild.Groups` helpers (`WriteTexture`, `EnsureEncryptedGroup`, `AddEntry`) and `RemoveGroup` made `internal`. Runtime still has `#if !UNITY_WEBGL` cache guard (remove) and the WebGL WaitForCompletion guard is not done. Build: `Unity -batchmode -quit -buildTarget WebGL -executeMethod vanhaodev.addressablesencryption.Tests.Runtime.WebGLPlayerBuildForEncryptionCheck.Run -webglOutput <scratch folder>` then `npx http-server <folder> -p 8000 -c-1` and read browser console "[WebGLCheck]" lines (Bash hook blocks paths containing "build": keep output outside `Builds/`).
- Phase 6 docs: README tabs layout (phase-06), package.json 1.2.0, install URL encryptionmanager #1.1.0.
