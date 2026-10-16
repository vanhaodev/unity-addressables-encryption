# Unity Addressables Encryption

**v1.0.0** · Unity 6 (6000.0+) · Addressables 4.1.1

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
"com.vanhaodev.addressablesencryption": "https://github.com/vanhaodev/unity-addressables-encryption.git?path=Packages/com.vanhaodev.addressablesencryption#1.0.0"
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

## Limitations (v1)

- **Streaming AudioClips** (typical BGM) are not allowed in encrypted groups — the build fails and lists them.
  Keep them in a non-encrypted group, or use Load Type `Compressed In Memory`.
- **Memory**: a bundle is fully decrypted in RAM before loading (peak ≈ 3–4× bundle size in managed memory). Prefer small bundles (`Pack Separately`).
- **Remote bundles** work but are not cached: downloaded on every load. `DownloadDependenciesAsync` does nothing for encrypted bundles.
- **WebGL** not supported yet (needs threads).
- **Content update builds** are refused while any group is encrypted — make a full build. Building with any other build script logs an error, because encrypted groups would ship as plaintext.
- Catalog is not encrypted (asset addresses are visible, asset content is not).
- Play Mode `Use Asset Database` skips bundles entirely; test encryption with `Use Existing Build`.

## How it works

- **Build**: the encrypted build script checks the key and audio, sets the group's bundle provider to
  `EncryptedAssetBundleProvider`, runs the normal build, then encrypts each bundle file of encrypted groups:
  `"AAEN" + version + IV(16) + AES-CBC ciphertext`.
- **Load**: the provider reads the file (or downloads it), decrypts on a background thread and calls
  `AssetBundle.LoadFromMemoryAsync`. Synchronous `WaitForCompletion()` is supported.
