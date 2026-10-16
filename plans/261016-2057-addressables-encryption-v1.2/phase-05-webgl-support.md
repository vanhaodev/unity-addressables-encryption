# Phase 5 — WebGL support

## Context Links
- plan.md "WebGL notes"; prior notes `plans/261016-1427-addressables-encryption-v1/plan.md` "On hold — WebGL"
- `Runtime/Scripts/Providers/EncryptedAssetBundleResource.cs:60-66` `#if !UNITY_WEBGL` around the remote cache. `:150-161` Task.Run (removed by Phase 1)
- Phase 1 stream: `Format/AesCbcDecryptingReadStream.cs`, `Providers/EncryptedBundleStreamOpener.cs`
- `Cache/EncryptedBundleCache.cs` Root :25-30 (persistentDataPath)
- WebGL module installed: `D:/Unity/6000.3.9f1/Editor/Data/PlaybackEngines/WebGLSupport`

## Overview
Priority P2 · Status pending · Depends on Phases 1 and 3. Make encrypted bundles (and the encrypted catalog) load on WebGL. Reuse the Phase 1 stream: the decrypt runs inside `Stream.Read` on whatever thread Unity calls it from (the main thread on WebGL), with no Task.Run. Frame-sliced decrypt is only a fallback. Verified locally: a batchmode WebGL player build of `HanniDemo`, plus a manual browser check.

## Key Insights
- WebGL: no threads for managed code. StreamingAssets are URLs (`Application.streamingAssetsPath` = http(s)), and the existing isUrl branch already routes them through UnityWebRequest. persistentDataPath = IndexedDB-backed Emscripten FS (`/idbfs/...`). System.IO is synchronous, persisted after FS sync (auto-flush after writes [UNVERIFIED for 6000.3]).
- After Phase 1 there is no background thread in the runtime. The open question is `AssetBundle.LoadFromStreamAsync`/`LoadFromStream` on WebGL [UNVERIFIED]. The loader may read the whole stream on the main thread (hitch ≈ size / managed AES speed).
- `System.Security.Cryptography.Aes` under WebGL IL2CPP: managed implementation expected to work [UNVERIFIED].
- Remote load path on WebGL: the browser fetches `Remote.LoadPath`. A different port/origin needs CORS headers, which `python -m http.server` does not send. For the check, use a **same-origin** remote path: the build helper sets the profile `Remote.LoadPath = http://localhost:8000/ServerData/[BuildTarget]` (restored after) and copies `ServerData/WebGL` into `Builds/WebGL/ServerData/WebGL`. One server on :8000 then serves the player, the local `StreamingAssets/aa` and the remote bundles.
- WebGL compression set to Disabled for the check, so a plain static server works (no `Content-Encoding` setup).

## Requirements
- Functional: on WebGL the local Demo group (`Hanni`, StreamingAssets URL) and a remote encrypted group load. Remote cache works in IndexedDB (or is cleanly disabled). Encrypted catalog (Phase 3 toggle on) loads. Same error messages.
- Non-functional: no threads. Frame hitch for a ~16 MB bundle measured and documented.

## Architecture
- Remove `#if !UNITY_WEBGL` at `EncryptedAssetBundleResource.cs:60-66`. The cache is enabled on WebGL. If the IDBFS write/move fails in verification, switch the WebGL path to the MemoryStream (no cache), documented.
- No `#if UNITY_WEBGL` in the stream. Add `#if UNITY_WEBGL` only where behavior differs:
  - `EncryptedBundleWebRequest.WaitForCompletion` spins with `Thread.Sleep` and would hang on WebGL. Under `#if UNITY_WEBGL`, sync loads of not-yet-downloaded bundles fail with "WaitForCompletion is not supported for web requests on WebGL" (stock Addressables has the same limit).
  - `EncryptedBundleCache.CommitTempFile`: copy+delete fallback if `File.Move` misbehaves on IDBFS [UNVERIFIED].
- Fallback (only if LoadFromStream is unsupported): `Providers/FrameSlicedBundleDecryptor.cs` (`#if UNITY_WEBGL`). IUpdateReceiver decrypts ~256 KB per tick within a 4 ms budget, with a PKCS7 check on the last block via `AesCbcBlockMath`, into one byte[], then `LoadFromMemoryAsync`.
- Build helper `Tests/Editor/WebGLPlayerBuildForEncryptionCheck.cs` (editor-only, test asmdef):
  1. `AddressablesEncryptionSetupMenu.Setup(settings)`, key `demo-secret`, Encrypt Catalog on (restored after).
  2. Group `AE WebGL Remote` (encrypted, remote, Use Asset Bundle Cache) with a generated PNG at address `WebGLRemoteTexture` + the 16 MB noise texture for the hitch measurement. Profile `Remote.LoadPath` = `http://localhost:8000/ServerData/[BuildTarget]`.
  3. `AddressableAssetSettings.BuildPlayerContent` → Error must be empty.
  4. `BuildPipeline.BuildPlayer(scenes, target = WebGL, locationPathName = Builds/WebGL | Builds/WebGLCheck)` with `PlayerSettings.WebGL.compressionFormat = Disabled` (restored after).
  5. Copy `ServerData/WebGL` → `<player folder>/ServerData/WebGL`. Restore the profile and remove the temp group in finally. `EditorApplication.Exit(ok ? 0 : 1)`.
- Two players from the same helper, so the sample stays untouched:
  - default: scenes = [`HanniDemo`] (found via `FindAssets("HanniDemo t:Scene")`) → `Builds/WebGL`. Picture check.
  - `-webglCheck` arg: scenes = [temp `WebGLLoadCheck` scene the helper creates in the gitignored temp folder with `WebGLLoadCheckBehaviour`] → `Builds/WebGLCheck`. Loads `Hanni`, `WebGLRemoteTexture` and the 16 MB texture, logs status + load ms, draws them.

## Related Code Files
- Modify: `Providers/EncryptedAssetBundleResource.cs`, `Download/EncryptedBundleWebRequest.cs`, `Cache/EncryptedBundleCache.cs` (if needed).
- Create: `Tests/Editor/WebGLPlayerBuildForEncryptionCheck.cs` (+ the temp scene script `Tests/Runtime/WebGLLoadCheckBehaviour.cs`: loads `Hanni`, `WebGLRemoteTexture`, the large texture, logs status + ms, draws the textures).
- Fallback only: `Providers/FrameSlicedBundleDecryptor.cs`.

## Implementation Steps
1. Remove the cache guard. Add the WebGL WaitForCompletion guard.
2. Add the build helper + load-check behaviour.
3. Batchmode WebGL build (first run switches the target and reimports; can take 10+ min):
   ```bash
   "$U" -batchmode -quit -projectPath "$PRJ" -buildTarget WebGL \
     -executeMethod vanhaodev.addressablesencryption.Tests.Editor.WebGLPlayerBuildForEncryptionCheck.Run \
     -logFile "$R/webgl-build.log"
   ```
   Check headers: `head -c 4 Builds/WebGL/StreamingAssets/aa/WebGL/*.bundle` and `Builds/WebGL/ServerData/WebGL/*.bundle` → AAEN. `catalog.bin` → AAEN.
4. Serve and open (Python is not installed; node is): `npx --yes http-server "$PRJ/Builds/WebGL" -p 8000 -c-1` → Chrome `http://localhost:8000/` (item 1). Then run step 3 again with `-webglCheck`, serve `Builds/WebGLCheck` the same way on :8000 (same Remote.LoadPath), and check items 2-6. Use `-c-1` (no HTTP cache) so the cache check measures our IndexedDB cache, not the browser's.
5. Manual browser checklist:
   - [ ] Hanni picture shows (local StreamingAssets via UnityWebRequest + LoadFromStream on WebGL)
   - [ ] `WebGLRemoteTexture` loads from `/ServerData/WebGL/` (Network tab, same origin, 200)
   - [ ] reload page: no second request for remote bundles (cache in IndexedDB; DevTools Application > IndexedDB `/idbfs`)
   - [ ] large texture: load ms + longest frame (DevTools Performance) recorded
   - [ ] encrypted catalog loaded (console has no errors). Temporarily build with SetKey removed → console shows the "before Addressables initializes" error
   - [ ] no PlatformNotSupportedException (AES under Wasm)
6. If item 1 fails because LoadFromStream is unsupported → implement the frame-sliced fallback and repeat 3-5.
7. Switch the target back to Windows (`-buildTarget StandaloneWindows64` on the next test run). Run the EditMode + PlayMode suites.
8. Write `plans/reports/webgl-verification-<yyMMdd>.md` (results, numbers) for Phase 6.

## Todo List
- [ ] Remove WebGL cache guard; WebGL sync guard
- [ ] Build helper + load-check scene/behaviour
- [ ] Batchmode WebGL build OK, headers AAEN
- [ ] Browser checklist (6 items)
- [ ] Fallback only if needed
- [ ] Windows suites green after switching back
- [ ] Verification report

## Success Criteria
- Batchmode WebGL build exits 0. All 6 browser items pass (or the documented fallback passes). Windows suites unchanged and green. Report has the hitch numbers.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| LoadFromStream unsupported/broken on WebGL | M×H | Frame-sliced fallback |
| Large bundles freeze the tab during Read | H×M | Measure; README: Pack Separately, < 10 MB bundles |
| IDBFS move/persist quirks | M×M | Copy+delete; disable the WebGL cache if still flaky |
| CORS when remote path is on another origin | M×M | Same-origin path for the check; README: CDN must send CORS headers |
| Target switch leaves the project on WebGL | M×L | Step 7 switches back; tests pass `-buildTarget StandaloneWindows64` |
| Helper changes profile/compression and then crashes | L×M | Restore in finally; temp group/folder gitignored |

## Security Considerations
Bundles and cache stay encrypted. The key in a WebGL build sits in wasm/JS (easier to extract), so README stresses "not DRM".

## Next Steps
Phase 6 docs (WebGL section from the verification report).
