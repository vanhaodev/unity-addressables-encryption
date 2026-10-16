# Addressables Encryption v1 (anti-ripper)

Branch: `dev` · Unity 6000.3.9f1 · Status: **In progress — code written, awaiting Unity compile + demo verify**

Goal: AES-encrypt Addressables bundles per group so AssetRipper/AssetStudio can't extract assets.
Package: `Packages/com.vanhaodev.addressablesencryption` (embedded, git URL `?path=Packages/...`, no `Exported/` copy).
Deps: `com.unity.addressables` 4.1.1 (latest, Unity 6000.0+), `com.vanhaodev.encryptionmanager` 1.0.0.

## Decisions (confirmed)
- Key: runtime `AddressablesEncryption.SetKey(string|byte[])` — hardcoded or fetched from server, before first encrypted load.
- Build key: Editor-only settings asset, env `ADDRESSABLES_ENCRYPTION_KEY` overrides (CI). Must match runtime key.
- Own `AesEncryption` instance — never touches `EncryptionManager` global AES slot.
- Per-group opt-in via `EncryptedBundleGroupSchema` (schema enabled = encrypt; schema IsEnabled toggle).
- Streaming AudioClip (BGM) in encrypted group → build fails with offending clip list.
- File format: magic `AAEN` + version byte + [IV 16B + ciphertext].
- Remote scope: option 2 (remote works, no cache; DownloadDependenciesAsync = no-op). Addressables 4.1.1.
- Load: local file read or UnityWebRequest bytes → decrypt on background thread → `AssetBundle.LoadFromMemoryAsync`. Remote = no cache (v1).

## Todo
### Phase 1 — Setup
- [x] Install `com.unity.addressables` 4.1.1
- [x] Create package skeleton: `package.json`, Runtime/Editor asmdef (refs Addressables, ResourceManager, encryptionmanager)

### Phase 2 — Runtime
- [x] `AddressablesEncryption` — SetKey(string/byte[]), HasKey, internal decrypt
- [x] `EncryptedBundleFormat` — write/read header, IsEncrypted check, clear errors (no key / wrong key / not encrypted)
- [x] `EncryptedAssetBundleResource` — UWR download → decrypt → LoadFromMemoryAsync, progress, Unload
- [x] `EncryptedAssetBundleProvider : ResourceProviderBase` — Provide/Release (+ EditMode tests for format/key)

### Phase 3 — Editor
- [x] `AddressablesEncryptionSettings` — Editor-only key asset + env override
- [x] `EncryptedBundleGroupSchema` — presence+enabled = encrypt; provider assigned by builder at build time (CRC ignored by provider)
- [x] `EncryptedBundleBuildValidator` — key present, no Streaming AudioClip (entries + deps)
- [x] `BuildScriptPackedEncryptedMode : BuildScriptPackedMode` — validate → base build → encrypt group bundles (skip already-encrypted)
- [x] Setup menu `Tools/Addressables Encryption/Setup` — register builder + set active, create settings asset

### Phase 4 — Demo & verify
- [ ] `Assets/Demo`: 1 encrypted group + 1 plain group, scene loads both
- [ ] Verify: bundle bytes unreadable (no `UnityFS` header), loads OK in "Use Existing Build", wrong key → clear error, streaming audio → build fail
- [ ] Compile check, code review

### Phase 5 — Docs
- [ ] README (install, SetKey, group toggle, limitations)

## On hold — WebGL (no plan yet, notes only)
- No threads on WebGL → `Task.Run` decrypt unusable; v1 decrypts on background thread only.
- Approach when resumed: frame-sliced AES-CBC decrypt on main thread — `ICryptoTransform.TransformBlock` per ~256KB chunk (multiple of 16B), `TransformFinalBlock` for last chunk, ~4ms budget/frame then yield.
- Output identical to `AesEncryption.Decrypt` (Aes.Create defaults CBC+PKCS7, IV 16B prefix) → build side unchanged.
- Perf: managed AES in WASM ~tens MB/s (estimate, unmeasured) → large bundles take seconds.
- Memory: WebGL heap limited + 2–3x peak → recommend Pack Separately / small bundles.
- Remote: WebGL nearly always remote; `persistentDataPath` = IndexedDB → custom cache needs special handling (or rely on browser HTTP cache).
- Planned file: `EncryptedBundleDecryptor` (thread path + frame-sliced path).

## Open questions
- Remote scope v1: (1) local only, remote throws · (2) local + remote no cache · (3) local + remote with own encrypted disk cache (+ GetDownloadSize/DownloadDependencies integration).

## Known limitations v1
- RAM peak ~2–3x bundle size at load · catalog not encrypted · key extractable from binary/memory · remote bundles not cached.

## Review fixes (report: plans/reports/from-code-reviewer-to-main-addressables-encryption-v1-review-261016-1458-report.md)
- [x] H1/H2/M1/M2: content update builds refused when encrypted groups exist (fail loudly, no plaintext leak)
- [x] H1: encryption all-or-nothing, skip missing files, delete plaintext outputs on failure
- [x] H3: provider assigned only for the build (restored in finally) + error log when built with another builder
- [x] M3: Timeout = no-progress watchdog (IUpdateReceiver), M4: no retry on 4xx (except 408/429) — `EncryptedBundleWebRequest`
- [x] Low: continuation exceptions → Fail; play-mode-exit leak; truncated file message; fallback to settings provider
- [ ] M5 memory (~4x peak): needs decision — stream-decrypt via CryptoStream bypasses EncryptionManager.Decrypt
- [ ] Content update support for encrypted groups (v1.x)
