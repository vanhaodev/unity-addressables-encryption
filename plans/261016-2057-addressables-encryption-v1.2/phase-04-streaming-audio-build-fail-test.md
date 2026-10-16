# Phase 4 — Streaming AudioClip build-fail EditMode test

## Context Links
- `Editor/Scripts/EncryptedBundleBuildValidator.cs:20-47` (→ `Validation/` after Phase 0): ValidateNoStreamingAudio, IsStreaming (platform override aware)
- Builder call site `BuildScriptPackedEncryptedMode.cs:42`, inside BuildDataImplementation before the base build
- Prior plan noted "Streaming-audio build fail: not run yet" (`plans/261016-1427-addressables-encryption-v1/plan.md`, Phase 4)

## Overview
Priority P3 · Status pending · Independent: needs only Phase 0 (Tests/Editor asmdef refs to the editor assemblies). Owns `Tests/Editor/StreamingAudioBuildFailTests.cs` exclusively.

## Key Insights
- The validator throws before any bundle is written, so a failed test leaves no plaintext output.
- Running the real builder (not only the validator) covers the wiring. Error surface: `BuildData` turns the exception into `result.Error` (comment at `BuildScriptPackedEncryptedMode.cs:31`) and logs it. Use `LogAssert.Expect(LogType.Error, new Regex("Streaming AudioClips"))` or `LogAssert.ignoreFailingMessages`.
- WAV generation: 44-byte RIFF header + PCM16 mono 22050 Hz, 0.25 s silence or sine. The importer accepts it without any external file.

## Requirements
- Test 1: a Streaming clip in a temp encrypted group → the build fails, the error contains the clip asset path and group name.
- Test 2: the same clip set to CompressedInMemory → the build succeeds (no Error) and the bundle is AAEN.
- Clean up everything (assets, group, build-time state) even on failure.

## Architecture
```
SetUp: Assets/AddressablesEncryptionTestContent/Audio/ae_test_tone.wav (File.WriteAllBytes + ImportAsset)
       AudioImporter.defaultSampleSettings.loadType = Streaming|CompressedInMemory → SaveAndReimport
       group "AE Audio Test" + EncryptedBundleGroupSchema, CreateOrMoveEntry(guid)
Act:   var builder = settings.DataBuilders.OfType<BuildScriptPackedEncryptedMode>() (ensure via Setup(settings))
       result = builder.BuildData<AddressablesPlayerBuildResult>(new AddressablesDataBuilderInput(settings))
Assert/TearDown: RemoveGroup, DeleteAsset folder, Refresh
```
Use `AddressablesDataBuilderInput` directly (no change to the active builder, no settings mutation beyond the temp group).

## Related Code Files
- Create: `Tests/Editor/StreamingAudioBuildFailTests.cs` (+ `Tests/Editor/TestWavWriter.cs` if > 200 lines combined; expected ~120, so a single file).
- Read only: validator, builder, schema.

## Implementation Steps
1. WAV writer helper (private static, ~25 lines).
2. Fixture SetUp/TearDown with try/finally cleanup. Key: reuse the settings key (`demo-secret`) or set env `ADDRESSABLES_ENCRYPTION_KEY` for the test and restore it.
3. Test 1 (Streaming → Error contains path + "Streaming AudioClips").
4. Test 2 (CompressedInMemory → no Error; the built bundle for the group starts with AAEN via `result.AssetBundleBuildResults`).
5. Batchmode EditMode run.

## Todo List
- [ ] WAV writer
- [ ] Fixture with cleanup
- [ ] Streaming fails, names clip
- [ ] CompressedInMemory builds, AAEN
- [ ] EditMode green

## Success Criteria
Both tests pass in batchmode. `git status` is clean afterwards (no leftover assets/groups). The prior plan's "Streaming-audio build fail: not run yet" item is closed.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Full build in EditMode is slow (~10-30 s) | M×L | Only 2 builds; tiny assets |
| Test build overwrites the dev build output | M×L | Acceptable (Library output); PlayMode prebuild rebuilds anyway |
| Platform override on the importer | L×L | Test sets defaults only; validator already handles overrides |

## Security Considerations
Uses the test key only. No secrets added.

## Next Steps
None blocking. If Q3 (streaming audio with LoadFromStream) is accepted later, flip Test 1's expectation.
