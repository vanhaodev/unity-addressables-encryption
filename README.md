# Unity Addressables Encryption

**v1.2.3** · Unity 6 (6000.0+) · Addressables 4.1.1

AES-encrypts Addressables AssetBundles (and optionally the catalog) so asset rippers such as AssetRipper,
AssetStudio or UABE cannot open them. Bundles are decrypted on the fly while Unity reads them, never written
back to disk in plain form and never fully held in memory.

[Install](#install) · [Setup](#setup) · [Encrypt a group](#encrypt-a-group) · [Runtime API](#runtime-api) · [Remote & cache](#remote--cache) · [Content update](#content-update) · [Catalog](#catalog) · [WebGL](#webgl) · [Limitations](#limitations) · [Examples](#examples) · [Sample](#sample)

> Goal: stop ripping tools from extracting assets out of the installed game. This is not DRM: a determined
> attacker can still pull the key out of the binary or dump memory.

---

## Install

Add both packages to `Packages/manifest.json`:

```json
"com.vanhaodev.encryptionmanager": "https://github.com/vanhaodev/unity-encryption-manager.git?path=Exported/com.vanhaodev.encryptionmanager#1.1.0",
"com.vanhaodev.addressablesencryption": "https://github.com/vanhaodev/unity-addressables-encryption.git?path=Packages/com.vanhaodev.addressablesencryption#1.2.3"
```

`com.vanhaodev.encryptionmanager` 1.1.0 or newer is required (it provides the streaming decryptor).

---

## Setup

Once per project:

1. Create Addressables settings: `Window > Asset Management > Addressables > Groups`.
2. `Tools > Addressables Encryption > Setup`. It creates the `AddressablesEncryptionSettings` asset and makes
   **Encrypted Build Script (AssetBundles)** the active build script.
3. Type the build key in that asset. On CI, leave it empty and set the `ADDRESSABLES_ENCRYPTION_KEY`
   environment variable instead, so the key stays out of git.
4. Build as usual: `Build > New Build > Encrypted Build Script`.

The Console warns when the setup and the groups disagree:

- built with the default build script while some group is encrypted: those groups shipped **plain**;
- built with the encrypted build script while no group is encrypted: nothing was encrypted.

Test with Play Mode Script **Use Existing Build**. "Use Asset Database" skips bundles entirely.

---

## Encrypt a group

On each group to protect: Inspector, **Add Schema > Encryption**. Untick or remove the schema to stop
encrypting. Game code does not change either way.

| Schema option | Default | |
|---|---|---|
| Hash Bundle File Names | on | Bundle files are named by hash, so no group or bundle name shows on disk or on the CDN |

Recommended group settings: **LZ4** (or Uncompressed) compression and **Pack Separately** for big assets.

All AudioClip load types work, including **Streaming** (typical for BGM): Unity reads the music through the
decrypting stream while it plays, so a long song never sits decrypted in memory.

---

## Runtime API

```csharp
using vanhaodev.addressablesencryption;

AddressablesEncryption.SetKey("my-secret");   // same secret as the build key
```

Call `SetKey` before loading anything encrypted. With [catalog encryption](#catalog) on, call it **before the
first Addressables call**, because Addressables loads the catalog while it initializes.

| API | |
|---|---|
| `SetKey(string secret)` | Any string, stretched with PBKDF2-SHA256 into a 256-bit key. Matches the settings format `Secret` |
| `SetKey(byte[] key)` | Raw 16, 24 or 32-byte AES key. Matches the settings format `RawKeyBase64` |
| `ClearKey()` / `HasKey` | Forget the key, or check it was set |
| `GetDownloadSizeAsync(key)` | Bytes still to download, for plain and encrypted bundles. Use it instead of `Addressables.GetDownloadSizeAsync` |
| `ClearCache()` | Delete cached encrypted bundles. `Addressables.ClearDependencyCacheAsync` only clears plain bundles |
| `BundleStreamRead` event | **Editor only** debugging: raised on every read Unity makes from an encrypted bundle (bundle, offset, bytes, length). Runs on loader / audio threads |

Errors are explicit: no key, wrong key, bundle not encrypted, truncated file. A wrong key is detected when the
bundle is opened, before Unity reads any data.

---

## Remote & cache

- Remote encrypted bundles download straight into `persistentDataPath/AddressablesEncryption/Cache`. They stay
  **encrypted** on disk, with hashed file names. The group's **Use Asset Bundle Cache** turns this on.
- One version per bundle: a newer version replaces the old file. A corrupted cache file is downloaded again once.
- Local bundles in StreamingAssets are never cached, they are already on the device.
- Encrypted bundles never go into Unity's native cache. If a group shipped unencrypted before, its old plain
  copy is removed from the native cache the first time the encrypted version loads.
- The catalog CRC is checked as for plain bundles: always for fresh downloads, for cached files only when the
  group's "Use Asset Bundle CRC For Cached Bundles" is on.

---

## Content update

"Update a Previous Build" works with encrypted groups.

- Changed assets of a "Prevent Updates" group move to the new **Content Update** group. That group inherits
  the Encryption schema of the group the assets came from, and the Console logs it.
- Unchanged assets keep their previous, already encrypted bundle files.
- The build is refused, with an explanation, when shipping it could break old players or leak plain files:
  - a "Prevent Updates" group turned Encryption on or off since the shipped build;
  - the group holding the shared bundles (built-in shaders, MonoScripts) is encrypted and marked "Prevent Updates".

Addressables itself requires **Build Remote Catalog** for content updates.

---

## Catalog

Tick **Encrypt Catalog** in `AddressablesEncryptionSettings` to encrypt the local `catalog.bin` as well. It
hides asset addresses and the list of bundles. It is off by default.

- `SetKey` must run before Addressables initializes. Without a key, initialization fails with a message saying so.
- Requires the **Binary** catalog provider (the Addressables 4 default).
- The remote catalog, when built, stays plain in this version.

---

## WebGL

WebGL is supported. Decryption runs inside the stream Unity reads from, so it needs no threads.

- Local and remote encrypted bundles load, and the encrypted catalog loads.
- The remote cache lives in IndexedDB, like any `persistentDataPath` file on WebGL.
- `WaitForCompletion` cannot wait for a download in the browser. It fails with a clear message instead of
  freezing the page. Load asynchronously, or download first with `Addressables.DownloadDependenciesAsync`.
- Decryption runs on the main thread. Prefer small bundles (Pack Separately) to keep frames short.

---

## Limitations

- **Memory**: a bundle is decrypted 128 KB at a time while Unity reads it. Loading a 16 MB bundle grows the
  managed heap by about 2.4 MB, mostly Addressables' own allocations.
- **Download size**: `Addressables.GetDownloadSizeAsync` cannot see the encrypted cache. Use
  `AddressablesEncryption.GetDownloadSizeAsync`.
- **LZMA**: use LZ4 or Uncompressed. LZMA bundles are fully decompressed on load.
- **VideoClips** in encrypted groups: test on every target platform.
- **Obfuscators**: the package's runtime types are created by name from the catalog. They carry
  `[Obfuscation(Exclude = true)]`. If your obfuscator ignores that attribute, exclude the
  `com.vanhaodev.addressablesencryption` assembly from renaming.

---

## Examples

**Key from your server**

```csharp
async void Start()
{
    var secret = await MyApi.GetAssetKeyAsync();
    AddressablesEncryption.SetKey(secret);        // before the first Addressables call
    await Addressables.InitializeAsync().Task;
    var hero = await Addressables.InstantiateAsync("Hero").Task;
}
```

**Download before entering a level**

```csharp
long size = await AddressablesEncryption.GetDownloadSizeAsync("level2");
if (size > 0)
    await Addressables.DownloadDependenciesAsync("level2", true).Task;  // fills the encrypted cache
```

**Clear everything downloaded**

```csharp
AddressablesEncryption.ClearCache();                                     // encrypted bundles
await Addressables.ClearDependencyCacheAsync("level2", true).Task;       // plain bundles
```

**Content update flow**

1. Ship a full build. Keep `addressables_content_state.bin`.
2. Change assets, then Groups window: `Tools > Check for Content Update Restrictions`.
3. `Build > Update a Previous Build`. The new Content Update group is encrypted automatically.

---

## Sample

One scene, one picture (and an optional song), one group. Turn encryption on or off on the group and rebuild: the game code stays the same.

**1. Import.** `Window > Package Manager > Addressables Encryption > Samples > Demo > Import`. It lands in
`Assets/Samples/Addressables Encryption/<version>/Demo`.

**2. Set up the library**, once per project.
1. `Window > Asset Management > Addressables > Groups`, then *Create Addressables Settings* if asked.
2. `Tools > Addressables Encryption > Setup`.
3. In `AddressablesEncryptionSettings`: **Format** `Secret`, **Key** `demo-secret`.

**3. Group.** Groups window, `Create > Group > Packed Assets`, rename it **Demo**, then Inspector
**Add Schema > Encryption**. Drag the sample's `Images/hanni_pham.jpg` into it with address **`Hanni`**.
Optional music: drop any song into the same group with address **`Music`** and set its Load Type to
**Streaming**. The demo plays it in a loop, streamed straight out of the encrypted bundle.

**4. Build & play.**
1. Groups window, **Play Mode Script > Use Existing Build**.
2. **Build > New Build > Encrypted Build Script (AssetBundles)**. The Console shows `Encrypted 1 bundle(s)…`.
3. Open `Scenes/HanniDemo` and press **Play**. The picture shows with status *Loaded*.

**Stream monitor (Editor only).** While the demo plays, the bottom half shows how encrypted bundles are read:

- **Bars**, one per bundle, left = start of the file, right = end. **Green** is the decrypted part held in memory
  right now, **red** is what was decrypted earlier and already freed. For a streamed song the green piece stays
  small and slides to the right while red grows behind it: played audio is not kept.
- **Timeline**, newest first. A **green** line is a read by Unity (here 32 KB pieces); a **red** line is a 128 KB
  window dropped from memory. The stream decrypts 128 KB at a time, so about four reads are served from one
  window before it is freed. For the song each line also shows the time range (estimated from the byte position).
- The title shows where the song is actually playing. Reads run a little ahead of it, and the audio decoder
  sometimes steps back a few KB (marked ↩): both are normal.

Untick **Log Stream Reads** on the demo component to hide it. None of this exists in player builds.

**5. Toggle encryption.** Untick the **Encryption** schema on the Demo group and rebuild: the bundle in
`Library/com.unity.addressables/aa/<platform>/<target>/` becomes a plain `UnityFS` file with its normal name.
Tick it again and it is back to an `AAEN` file with a hashed name. Play works either way.

**Rip test.** `File > Build Profiles`, add the scene, **Build**. Point AssetRipper or AssetStudio at
`<exe>_Data/StreamingAssets/aa/`: the plain bundle opens, the encrypted one does not.

> This repository's own project is already set up. `Assets/Samples` is git-ignored, so import the sample
> first: without it the group points at a missing asset and the build logs `Encrypted 0 bundle(s)`.

### Developing this package

```
Packages/com.vanhaodev.addressablesencryption/   the package, installed via ?path=
├── Runtime/Scripts/  Api/ Format/ Providers/ Catalog/ Cache/ Download/
├── Editor/Scripts/   Pipeline/ Settings/ Schema/ Validation/ Menus/
├── Tests/Editor/     EditMode tests (format, stream opener, cache, catalog, content update, audio)
├── Tests/Runtime/    PlayMode tests; they build the content themselves before entering play mode
└── Samples~/Demo/    hidden from Unity, imported into Assets/Samples
```

Close the Editor, then run the tests in batchmode (the Demo sample must be imported):

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults editmode.xml
Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults playmode.xml
```

## How it works

- **Build**: the encrypted build script checks the key and audio, gives encrypted groups the
  `EncryptedAssetBundleProvider` for the build, runs the normal build, then encrypts each bundle file:
  `"AAEN" + version + IV(16) + AES-CBC ciphertext`. With Encrypt Catalog, `catalog.bin` gets the same treatment.
- **Load**: the provider opens the file (or downloads it into the cache), checks the header and the key, and
  hands Unity a seekable stream that decrypts blocks as they are read (`AssetBundle.LoadFromStreamAsync`).
