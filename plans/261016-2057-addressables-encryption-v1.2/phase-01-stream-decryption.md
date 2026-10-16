# Phase 1 — Stream decryption (LoadFromStream)

## Context Links
- `Runtime/Scripts/Providers/EncryptedAssetBundleResource.cs` (after Phase 0; today `EncryptedAssetBundleResource.cs`): Start :36-72, ReadLocalAndDecrypt :130-133, StartWork/Task.Run :150-161, LoadFromMemoryAsync :195, Unload :75-82
- `EncryptedBundleFormat.cs` header :16-18 (magic 4 + ver 1 = 5 bytes), Decrypt :47-73
- `AddressablesEncryption.cs` `_aes` :24 (AesEncryption, no raw key access)
- `EncryptedBundleWebRequest.cs` downloads to memory `downloadHandler.data` :138
- `EncryptedBundleCache.cs` Store :53-78 (temp + move)

## Overview
Priority P1 · Status pending. Replace the decrypt-all-then-LoadFromMemory path with a seekable decrypting stream fed to `AssetBundle.LoadFromStreamAsync` / `LoadFromStream`. Peak managed memory becomes about the buffer size instead of 3-4x the bundle size.

## Key Insights
- CBC random access: plaintext block i = AES_dec(C_i) XOR C_{i-1}, with C_{-1} = stored IV. Any 16-byte aligned range can be decrypted from file offset `5 + 16 + 16*i`, given the previous ciphertext block.
- Length: cipherLen = fileLen - 21, which must be a positive multiple of 16. Decrypt the last block (IV = second-to-last block, or the stored IV if there is only 1 block). The PKCS7 pad byte p is in 1..16 and all p bytes must equal p, else "wrong key or corrupted". plainLen = cipherLen - p. Doing this once at open gives early wrong-key detection (about 1/256 false pass; then LoadFromStream returns null and the existing "could not be loaded" path runs).
- .NET CBC transforms can't change IV after creation. Keep one `Aes` (Key set once, Mode=CBC, Padding=None) per stream and call `CreateDecryptor(key, iv)` per refill chunk (64-256 KB, so the allocation is per chunk, not per Read).
- Unity may call Read/Seek from a loader thread. The stream is single-reader: guard Read/Seek/Position with one `lock`. Never touch Unity APIs inside it.
- Unity needs the stream open for the bundle's whole lifetime. Dispose it after `AssetBundle.Unload` only.
- Android local bundles are `jar:file://...apk!/assets/aa/...` and not plain files. The existing isUrl branch (`:47-48`) already sends them to UnityWebRequest.
- Raw key needed: `AddressablesEncryption` holds an `AesEncryption`. Add `internal static byte[] CurrentKeyOrThrow(bundleId)` that returns a clone of a stored `volatile byte[] _key` (set in `SetKey(byte[])` :38-42, cleared in ClearKey :45-48). Same "No key set" message as `DecryptBundle` :74-76 (tests assert "No key set").

## Requirements
- Functional: local (plain file), remote (http/https → cache file → stream), remote without cache (hash empty/zero → MemoryStream over downloaded bytes), Android jar: (download → cache file if hash, else MemoryStream). Async + WaitForCompletion. Download-only fills cache. Corrupted cache → delete + redownload once (keep `TryRedownloadCorruptedCache`). Error messages unchanged.
- Non-functional: no `Task.Run`, no full-bundle managed arrays on the file paths. Remote download streams to disk via `DownloadHandlerFile`.

## Architecture
New files (Runtime/Scripts):
- `Format/AesCbcDecryptingReadStream.cs` (Stream: CanRead/CanSeek true, CanWrite false; ctor(Stream source, byte[] key, string bundleId, bool ownsSource); validates header/version/length; Length; Position; Seek; Read into a refill buffer; Dispose closes source + Aes).
- `Format/AesCbcBlockMath.cs` (pure helpers: CipherOffset(block), PlainLength from pad, ValidatePkcs7; unit-testable).
- `Providers/EncryptedBundleStreamOpener.cs`: `OpenFile(path, id)` = FileStream(FileShare.Read, bufferSize 4096, FileOptions.RandomAccess) wrapped; `OpenMemory(bytes, id)`.
- `Download/EncryptedBundleWebRequest.cs`: add a to-file mode, `DownloadHandlerFile(tempPath)` with `removeFileOnAbort = true`. The callback gives the path or the bytes.
- `Cache/EncryptedBundleCache.cs`: add `CommitTempFile(tempPath, finalPath)` (move + delete old versions, same as Store) and `GetTempPath(finalPath)`.

Load flow:
```
Start ─ local file ─────────────► OpenFile(path) ─┐
      ─ remote, cache hit ──────► OpenFile(cache) ─┤
      ─ remote/jar, cache path ─► UWR→temp file → header check → Commit → OpenFile(cache)
      ─ remote/jar, no cache ───► UWR→bytes → OpenMemory(bytes) ─┤
                                                                 ▼
          stream ctor validates (header, version, last-block pad) → throws = Fail / TryRedownload
          AssetBundle.LoadFromStreamAsync(stream) → OnLoaded → Complete(this)
          WaitForCompletion: if request pending → read .assetBundle (sync finish), or use LoadFromStream when nothing started
          Unload(): bundle.Unload(true) → stream.Dispose()
```
- Download-only: download to temp → validate magic only → commit. No stream.
- Stream open happens on the main thread (cheap: one 16 B read + 1 block decrypt). No background work, so `_workTask` and `StartWork` are removed.
- If loading fails (bundle null) → dispose the stream, then the existing retry/fail path runs.

## Related Code Files
- Modify: `Providers/EncryptedAssetBundleResource.cs`, `.Sync.cs`, `Api/AddressablesEncryption.cs` (raw key holder), `Download/EncryptedBundleWebRequest.cs`, `Cache/EncryptedBundleCache.cs` (keep `Store(byte[])` for the no-file path? No: delete it if unused, YAGNI).
- Create: `Format/AesCbcDecryptingReadStream.cs`, `Format/AesCbcBlockMath.cs`, `Providers/EncryptedBundleStreamOpener.cs`, `Tests/Editor/AesCbcDecryptingReadStreamTests.cs`, `Tests/Runtime/EncryptedBundleMemoryPlayModeTests.cs`.
- Keep: `EncryptedBundleFormat.Decrypt` (EditMode tests + Phase 3 catalog use).

## Implementation Steps
1. Raw key holder in `AddressablesEncryption` (thread-safe reference swap, clone on read).
2. Write `AesCbcBlockMath` and the stream. Read(buf, off, count): clamp to Length. Compute block = pos/16. If not in the current buffer, refill: read prevBlock (or IV) + up to BufferSize bytes of ciphertext, CreateDecryptor(key, prev), TransformBlock. Copy out. Seek supports Begin/Current/End.
3. EditMode tests: for sizes {1, 15, 16, 17, 4095, 4096, 65536, 65537, 300000} random bytes, encrypt with `EncryptedBundleFormat.Encrypt` and compare a full sequential read, random Seek+Read pairs (500 iterations, fixed seed), and Read past end. Wrong key throws `CryptographicException` with "wrong key". Truncated / bad header / bad version produce the existing messages.
4. Web request to-file mode + cache commit. Validate the magic of the downloaded temp file before commit (not encrypted → existing "not encrypted" message, temp deleted).
5. Rewrite resource Start/OnDownloaded/OnLoaded/Unload around the streams. Remove `Task.Run`, `_workTask`, `FromCurrentSynchronizationContext`. Keep the play-mode-exit guard (`:186-193`) before LoadFromStreamAsync.
6. WaitForCompletion: download wait (existing), then open the stream, then if no request yet `AssetBundle.LoadFromStream` (sync) else read `.assetBundle`.
7. PlayMode memory test: the prebuild adds group `AE Test Large` (local, encrypted, Uncompressed bundle) with a generated 2048x2048 RGBA32 noise texture (~16 MB, incompressible, import as uncompressed). Measure `Profiler.GetMonoUsedSizeLong()` after GC before load, peak sampled each frame during load, and after. Assert peak delta < 4 MB. (Before this phase it would be ~32+ MB.)
8. Run EditMode + PlayMode. Manual rip check (plan.md).

## Todo List
- [ ] Raw key holder
- [ ] BlockMath + DecryptingReadStream + EditMode tests
- [ ] UWR to-file + cache CommitTempFile
- [ ] Resource rewrite (async + sync), Task.Run removed
- [ ] Memory PlayMode test (+ prebuild large group)
- [ ] All tests green; rip check fails for AssetRipper

## Success Criteria
- Stream tests: byte-identical to `AesEncryption.Decrypt` output for all sizes and random seeks.
- PlayMode: Phase 0 suite + memory test pass. Managed peak delta < 4 MB for a 16 MB bundle.
- `grep -rn "Task.Run\|LoadFromMemory" Runtime/` returns nothing.
- AssetRipper still can't extract `hanni_pham` from a Windows player.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Unity reads from a worker thread while Unload disposes | M×H | lock in stream; Dispose after Unload(true) returns (Unity finishes reads before unloading) |
| Random small reads → many CreateDecryptor allocations | M×M | 128 KB buffer, refill only on a miss; profile; ECB+XOR reusable transform as a later optimization |
| Middle-of-file corruption undetected | L×M | Unity's header/LZ4 checks; Q5 in plan.md |
| FileStream kept open blocks cache replace (Windows sharing) | M×M | FileShare.Read|Delete; Store of a new version uses a new hash name, so the old file is deleted only when not open (TryDelete logs on failure) |
| Android jar path without cache hash loads into memory (1x size) | L×L | Documented. Still better than 3-4x |

## Security Considerations
Plaintext never written to disk and never fully held in managed memory (only the 128 KB buffer). Key bytes cloned per stream and zeroed on Dispose (`Array.Clear`). Cache stays encrypted.

## Next Steps
Phase 2 (content update), Phase 5 (WebGL builds on this stream).
