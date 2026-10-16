# WebGL verification (v1.2) — 2026-10-16

Unity 6000.3.9f1, Addressables 4.1.1, Chrome headless (patchright), WebGL compression Disabled, Development build.
Player: `WebGLPlayerBuildForEncryptionCheck.Run` (Tests/Runtime), served with `npx http-server -p 8000 -c-1`.

## Build output
- `StreamingAssets/aa/catalog.bin`: AAEN (Encrypt Catalog on)
- local bundles (Demo `Hanni`, 16 MB `TestLargeTexture`): AAEN, hashed names
- remote bundle (`ServerData/WebGL`, same origin): AAEN

## Browser results
| Check | Result |
|---|---|
| Encrypted catalog loads (SetKey before init) | pass |
| Local encrypted bundle via StreamingAssets URL + LoadFromStream | pass (Hanni picture shown) |
| Remote encrypted bundle | pass, 1 request on first load |
| Reload: remote bundle from IndexedDB cache | pass, 0 requests, picture shown; `/idbfs` DB present |
| 16 MB uncompressed encrypted bundle | pass (shown) |
| AES on WebAssembly | works (no PlatformNotSupportedException) |

## Fixes found by this run
- `UnityEngine.Caching` does not exist on WebGL → legacy native-cache cleanup compiled out (`#if !UNITY_WEBGL`).
- Check helper: objects fetched before `BuildPlayer` are destroyed by the asset reload → re-fetch in `finally`.

## Not measured
- Frame hitch of the 16 MB load (headless software rendering is not representative). Unity log does not reach
  the runner's console capture; results read from screenshots.

## Unresolved
- Remote catalog stays plain (v1.3 candidate).
