# Phase 3 — Catalog encryption (opt-in)

## Context Links (Addressables 4.1.1, `Library/PackageCache/com.unity.addressables@046549581c8a/`)
- `Editor/Build/DataBuilders/BuildScriptBase.cs:531-539`: `protected virtual ICatalogBuilder CreateCatalogBuilder(settings)`, the override point
- `Editor/Build/CatalogBuilders/BaseCatalogBuilder.cs:51-63` CreateForProvider (TypeCache over BaseCatalogBuilder subclasses)
- `Editor/Build/CatalogBuilders/BinaryCatalogBuilder.cs:32-88` GenerateCatalog. `:106-111` writes `Addressables.BuildPath/<RuntimeCatalogFilename>.bin` + `.hash`. `:129-134` adds the catalog ResourceLocationData with **hardcoded** `typeof(BinaryCatalogProvider)`. `:164` remote catalog = same plaintext bytes
- `Runtime/ResourceLocators/ResourceLocationData.cs:44-47` Provider has no setter (replace the instance via ctor `:109`)
- `Runtime/ResourceProviders/BinaryCatalogProvider.cs:27-34` CreateInnerCatalogLocation → provider id `BinaryContentCatalogData.kBinaryAssetProviderId`
- `Runtime/ResourceManager/ResourceProviders/BinaryDataProvider.cs:197` `public virtual object Convert(Type, byte[])`. `:150-162` exceptions in Convert are logged (unless IgnoreFailures) and the result is null
- `Runtime/ResourceManager/ResourceProviders/BinaryAssetProvider.cs:11` internal generic stock parser (Convert = parse)
- `Runtime/Initialization/InitializationOperation.cs:48-56` stock providers registered. Init objects run before catalogs load (`:61-69` groupOp is a dependency of initOp → Execute `:94` loads catalogs)
- `Runtime/ResourceProviders/ContentCatalogProvider.cs:460` remote catalog cached to persistentDataPath as **decrypted** `GetSerializedData()`
- `Editor/Settings/AddressableAssetSettings.cs:1613-1680` InitializationObjects list

## Overview
Priority P2 · Status pending. Toggle "Encrypt Catalog" (default off) on `AddressablesEncryptionSettings`. When on, the local `catalog.bin` in the player is AAEN-encrypted with the build key and decrypted at load by a custom catalog provider. Remote catalog `.bin` + all `.hash` stay plain (per brief).

## Key Insights
- Minimal override: subclass `BinaryCatalogProvider` and override only `CreateInnerCatalogLocation` to point at our data provider. The data provider subclasses public `BinaryDataProvider` and overrides `Convert`: if AAEN, decrypt and hand the plain bytes to the stock `BinaryAssetProvider<Serializer>` instance's public `Convert` (found by ProviderId in `ResourceManager.ResourceProviders` at registration). No reflection, no internal types.
- Registration before catalog load: an Addressables init object (`IInitializableObject`, created by type name from settings.json) adds both providers. Ordering is guaranteed by InitializationOperation.
- Settings.json catalog location provider: our catalog builder wraps the stock `BinaryCatalogBuilder`. After GenerateCatalog it replaces the location it added with a new `ResourceLocationData(keys, id, typeof(EncryptedBinaryCatalogProvider), typeof(BinaryContentCatalogData), deps)` and copies `Data`.
- Plain pass-through in Convert: remote catalog, and Addressables' persistentDataPath cached copy (written plain at `:460`), still load. The local .hash holds the plaintext hash; runtime only compares hash strings (`ContentCatalogProvider.cs:409`), so it is unaffected.
- The key must exist when the catalog is parsed, i.e. before `Addressables.InitializeAsync` (also implicit on first load). Init object logs a clear error if `!HasKey`. Convert throws `InvalidOperationException("[AddressablesEncryption] Catalog is encrypted but no key is set. Call AddressablesEncryption.SetKey before Addressables initializes (before the first Addressables call).")` and also `Debug.LogError`s it, because BinaryDataProvider swallows the exception when IgnoreFailures is set.
- Play Mode "Use Asset Database" never reads catalog files, so there is no effect there. "Use Existing Build" exercises it.
- JSON catalog (`m_EnableJsonCatalog=1` / JsonCatalogProvider) is not supported: the build fails with a clear message when the toggle is on.

## Requirements
- Functional: toggle off → byte-identical output to today. Toggle on → `aa/<platform>/catalog.bin` starts with AAEN, settings.json catalog provider = `vanhaodev.addressablesencryption.EncryptedBinaryCatalogProvider`, init object present, player loads. Missing key → load fails with the message above. Wrong key → "Cannot decrypt catalog: wrong key or corrupted file."
- Non-functional: catalog decrypt is one in-memory pass (catalogs are small, KB-MB). Obfuscation-excluded runtime types.

## Architecture
Runtime (`Runtime/Scripts/Catalog/`):
- `EncryptedBinaryCatalogProvider : BinaryCatalogProvider`: `[Preserve]`, `[Obfuscation]`, ctor(ResourceManager). Override CreateInnerCatalogLocation → `ResourceLocationBase(id, id, EncryptedCatalogDataProvider.Id, typeof(BinaryContentCatalogData)) { Data = opts }`.
- `EncryptedCatalogDataProvider : BinaryDataProvider`: ctor(BinaryDataProvider stockParser). Convert: IsEncrypted? → `EncryptedBundleFormat.Decrypt(bytes, aes, "catalog")` → `stockParser.Convert(type, plain)`. Else → `stockParser.Convert(type, bytes)`.
- `EncryptedCatalogInitialization : IInitializableObject`: `[Preserve]`, `[Obfuscation]`. Initialize: find the stock parser by `BinaryContentCatalogData.kBinaryAssetProviderId`, add `new EncryptedCatalogDataProvider(stock)` and `new EncryptedBinaryCatalogProvider(rm)` to `Addressables.ResourceManager.ResourceProviders` if not present (idempotent across re-init). Copy `DisableCatalogUpdateOnStart`/`IsLocalCatalogInBundle` from the stock BinaryCatalogProvider: InitializationOperation sets them in Execute `:143-150` over all ContentCatalogProviders, ours included, since it runs after init objects. Verify in test. Log error if !HasKey. Return true. `InitializeAsync` returns a completed op (`ResourceManager.CreateCompletedOperation`).
- `AddressablesEncryption.DecryptCatalog(bytes)`: internal, reuses the `_aes` + "no key" message variant.

Editor:
- `Settings/AddressablesEncryptionSettings.cs`: `[SerializeField] bool m_EncryptCatalog` + `public static bool IsCatalogEncryptionEnabled()` (Find()?.m_EncryptCatalog ?? false).
- `Pipeline/EncryptedBinaryCatalogBuilder : BaseCatalogBuilder`: CatalogProviderType = typeof(EncryptedBinaryCatalogProvider), CatalogExtension "bin", SupportsLocalCatalogBundling false. GenerateCatalog → `new BinaryCatalogBuilder().GenerateCatalog(...)` → encrypt `Path.Combine(Addressables.BuildPath, AddExtensionToCatalogFilename(paths.RuntimeCatalogFilename))` with `ResolveBuildKey()` → swap the location entry. Parameterless ctor (TypeCache/Activator path also works).
- `Pipeline/EncryptedCatalogInitializationData : ScriptableObject, IObjectInitializationDataProvider`: Name "Encrypted Catalog", `CreateObjectInitializationData()` → `ObjectInitializationData.CreateSerializedInitializationData<EncryptedCatalogInitialization>(Name)`.
- `BuildScriptPackedEncryptedMode`: override `CreateCatalogBuilder`: toggle on → require `settings.CatalogProviderType` ∈ {BinaryCatalogProvider, EncryptedBinaryCatalogProvider} else throw. Return `new EncryptedBinaryCatalogBuilder()`. Otherwise base. In BuildDataImplementation, when the toggle is on, add a transient `EncryptedCatalogInitializationData` instance to `settings.InitializationObjects` before base build and remove it in `finally` (same pattern as the provider restore). Resolve the key even when no group is encrypted.

## Related Code Files
- Create: `Runtime/Scripts/Catalog/{EncryptedBinaryCatalogProvider,EncryptedCatalogDataProvider,EncryptedCatalogInitialization}.cs`, `Editor/Scripts/Pipeline/{EncryptedBinaryCatalogBuilder,EncryptedCatalogInitializationData}.cs`, `Tests/Editor/CatalogEncryptionTests.cs`.
- Modify: `Api/AddressablesEncryption.cs`, `Settings/AddressablesEncryptionSettings.cs`, `Pipeline/BuildScriptPackedEncryptedMode.cs`, `Tests/Runtime/PlayModeTestContentBuild.cs` (toggle on during the test build, restore after), PlayMode fixtures (SetKey before `InitializeAsync` in `[UnitySetUp]`. NoKey/WrongKey tests clear or change the key **after** init).
- Runtime asmdef: no change (Unity.Addressables already referenced).

## Implementation Steps
1. Runtime catalog classes + `DecryptCatalog`.
2. Settings toggle + tooltip ("Call SetKey before Addressables initializes").
3. Catalog builder + init data + builder overrides.
4. EditMode tests: (a) data provider Convert with AAEN bytes + no key → throws with "before Addressables initializes"; (b) wrong key → "wrong key"; (c) plain bytes pass through to the stub parser; (d) build with toggle on → catalog.bin AAEN, settings.json contains `EncryptedBinaryCatalogProvider` and `EncryptedCatalogInitialization`; toggle off → catalog not AAEN. Restore the toggle in TearDown.
5. PlayMode prebuild turns the toggle on, so the whole suite runs against an encrypted catalog. Add a test asserting `Addressables.ResourceManager.ResourceProviders` contains our catalog provider.
6. Manual: player build + rip check (plan.md).

## Todo List
- [ ] Runtime: catalog provider, data provider, init object
- [ ] Editor: toggle, catalog builder, init data, CreateCatalogBuilder override, transient init object
- [ ] JSON catalog guard
- [ ] EditMode tests (4) + PlayMode on encrypted catalog
- [ ] Rip check: catalog unreadable

## Success Criteria
- Toggle off: existing tests green, no catalog changes. Toggle on: all PlayMode tests green. `head -c 4 aa/<platform>/catalog.bin` = AAEN. Player loads. AssetRipper/hex shows no addresses.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Addressables update changes inner location / provider ids | M×H | Pinned 4.1.1; init object fails loudly if the stock parser isn't found |
| Transient InitializationObjects entry left after an editor crash | L×M | Remove in finally; on load, Setup menu strips null/transient entries |
| User calls Addressables before SetKey (common) | H×M | Default off; README rule; explicit error text |
| Remote catalog still plain (addresses visible on CDN) | H×L | Documented limitation; Q1 |

## Security Considerations
Hides addresses, bundle names and the dependency graph from local rippers. The same key encrypts the catalog and bundles (one secret; leaking it already breaks everything). Decrypted catalog lives only in memory for local loads.

## Next Steps
Phase 5 WebGL (catalog path must also work there), Phase 6 docs.
