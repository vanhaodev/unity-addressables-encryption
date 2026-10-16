# Unity Addressables Encryption

**v1.1.0** · Unity 6 (6000.0+) · Addressables 4.1.1

AES-encrypts Addressables AssetBundles so asset rippers (AssetRipper, AssetStudio, UABE…) cannot open them.

- **Per group** — add the `Encryption` schema only to the groups you want to protect
- **Your key, your way** — hardcode it or fetch it from your server, then call `SetKey`
- **Drop-in** — same `Addressables.LoadAssetAsync` / `InstantiateAsync` / scenes, nothing changes in game code
- **Fails loudly** — missing key, wrong key or unencrypted bundle gives a clear error, never silent garbage

> v1 goal: stop ripping tools from extracting assets out of the installed game. It is not DRM — a determined
> attacker can still pull the key from the binary or dump memory.

## Install

Add to `Packages/manifest.json`:

```json
"com.vanhaodev.encryptionmanager": "https://github.com/vanhaodev/unity-encryption-manager.git?path=Exported/com.vanhaodev.encryptionmanager#1.0.0",
"com.vanhaodev.addressablesencryption": "https://github.com/vanhaodev/unity-addressables-encryption.git?path=Packages/com.vanhaodev.addressablesencryption#1.1.0"
```

## Setup (once)

1. Create Addressables settings (`Window > Asset Management > Addressables > Groups`).
2. `Tools > Addressables Encryption > Setup` — creates `AddressablesEncryptionSettings` and makes
   **Encrypted Build Script (AssetBundles)** the active build script.
3. Type the build key in the selected `AddressablesEncryptionSettings` asset
   (or set env var `ADDRESSABLES_ENCRYPTION_KEY`, e.g. on CI, to keep it out of git).
4. On each group to protect: **Add Schema > Encryption**. Disable or remove the schema to stop encrypting.
5. Build Addressables as usual (`Build > New Build > Encrypted Build Script`).

## Runtime

```csharp
using vanhaodev.addressablesencryption;

// Before loading anything from an encrypted group:
AddressablesEncryption.SetKey("my-secret");            // same secret as the build key

// or fetched from your server:
var secret = await MyApi.GetAssetKeyAsync();
AddressablesEncryption.SetKey(secret);

// then load as usual
var handle = Addressables.LoadAssetAsync<GameObject>("Hero");
```

| API | |
|---|---|
| `SetKey(string secret)` | Any string, stretched with PBKDF2-SHA256 into a 256-bit key. Matches settings format `Secret` |
| `SetKey(byte[] key)` | Raw 16/24/32-byte AES key. Matches settings format `RawKeyBase64` (key stored as Base64) |
| `ClearKey()` / `HasKey` | Forget the key / check it was set |
| `GetDownloadSizeAsync(key)` | Bytes still to download for key (plain + encrypted bundles). Use instead of `Addressables.GetDownloadSizeAsync` |
| `ClearCache()` | Delete cached encrypted bundles. `Addressables.ClearDependencyCacheAsync` only clears plain bundles |

### Remote bundles & cache

```csharp
long size = await AddressablesEncryption.GetDownloadSizeAsync("level2");
if (size > 0)
    await Addressables.DownloadDependenciesAsync("level2", true).Task; // fills the encrypted cache
```

- Remote encrypted bundles are cached in `persistentDataPath/AddressablesEncryption/Cache` — **still encrypted**,
  with hashed file names (no group or bundle names). Controlled by the group's **Use Asset Bundle Cache**.
- One version per bundle: a content update replaces the old file. A corrupted cache file is downloaded again.
- Local bundles (StreamingAssets) are never cached — they are already on the device.
- Encrypted bundles never go into Unity's native cache. If a group shipped **unencrypted** before, its old plain
  copy is removed from the native cache the first time the encrypted version loads.
- Encrypted groups name bundle files by hash (schema option **Hash Bundle File Names**, on by default).

## Limitations (v1)

- **Streaming AudioClips** (typical BGM) are not allowed in encrypted groups — the build fails and lists them.
  Keep them in a non-encrypted group, or use Load Type `Compressed In Memory`.
- **Memory**: a bundle is fully decrypted in RAM before loading (peak ≈ 3–4× bundle size in managed memory). Prefer small bundles (`Pack Separately`).
- **Download size**: `Addressables.GetDownloadSizeAsync` cannot see the encrypted cache — use `AddressablesEncryption.GetDownloadSizeAsync`.
- **WebGL** not supported yet (needs threads).
- **Content update builds** are refused while any group is encrypted — make a full build. Building with any other build script logs an error, because encrypted groups would ship as plaintext.
- Catalog is not encrypted (asset addresses are visible, asset content is not).
- Use **LZ4** (or Uncompressed) compression for encrypted groups: LZMA bundles loaded from memory are fully decompressed on load.
- **VideoClips** in encrypted groups: test on every target platform (video playback from memory-loaded bundles varies).

## Roadmap

- Stream decryption straight from file (peak memory ≈ 1–2× bundle size)
- Content update builds with encrypted groups
- WebGL: frame-sliced decryption on the main thread
- Play Mode `Use Asset Database` skips bundles entirely; test encryption with `Use Existing Build`.

## How it works

- **Build**: the encrypted build script checks the key and audio, sets the group's bundle provider to
  `EncryptedAssetBundleProvider`, runs the normal build, then encrypts each bundle file of encrypted groups:
  `"AAEN" + version + IV(16) + AES-CBC ciphertext`.
- **Load**: the provider reads the file (or downloads it), decrypts on a background thread and calls
  `AssetBundle.LoadFromMemoryAsync`. Synchronous `WaitForCompletion()` is supported.
