# v1.1 — Remote encrypted cache

Status: **In progress** · Branch: dev

## Decisions (user, 2026-10-16)
- Cache only remote (http/https) bundles; local bundles never cached.
- Cache files stay encrypted; names hashed: <root>/<sha(bundleName)>/<sha(hash)>.aaen.
- Enabled by group "Use Asset Bundle Cache" (catalog Hash non-empty).
- Legacy plain copies in Unity native cache cleared by bundle name on first encrypted load (name unchanged by encryption).
- Group rename / bundle-mode change during enabling encryption: accepted, no extra handling.
- GetDownloadSizeAsync: own API `AddressablesEncryption.GetDownloadSizeAsync` (option 1, recommended).
- Bundle file names: hashed at build time (schema toggle, default on), group setting untouched.
- Catalog encryption: not in v1.1 (possible v1.2).

## Todo
- [x] EncryptedBundleCache (store/replace/clear, hashed names, legacy native cache clear)
- [x] Resource: cache hit, download → decrypt → store, download-only fills cache, corrupt cache → redownload once, download status bytes
- [x] AddressablesEncryption.GetDownloadSizeAsync / ClearCache
- [x] Hash bundle file names (ConstructAssetBundleName override + schema toggle)
- [x] EditMode cache tests, PlayMode remote tests (local HTTP server), demo remote group
- [x] Batchmode verify 2026-10-16: build+verify PASS (3 groups, hashed names), EditMode 16/16, PlayMode 9/9 (fixed: cache deleted own file on mixed slashes; zero hash = no cache)
- [ ] Tag 1.1.0 + push (ask user)
