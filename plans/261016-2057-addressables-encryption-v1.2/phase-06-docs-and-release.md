# Phase 6 — Docs, version 1.2.0, plan updates

## Context Links
- `README.md` (v1.1.0 header, Runtime API table, Remote bundles & cache, Sample, Roadmap/limitations)
- `Packages/com.vanhaodev.addressablesencryption/package.json` (`"version": "1.1.0"`)
- `plans/261016-1427-addressables-encryption-v1/plan.md` (Known limitations, deferred M5 memory, content update)
- Phase reports in `plans/reports/` (memory numbers from Phase 1, WebGL verification from Phase 5)

## Overview
Priority P2 · Status pending · Depends on 0-5. Docs only, no code.

## Key Insights
- README currently says RAM peak ~2-3x (prior plan) and "catalog not encrypted"; content update is "refused". All three change.
- No `./docs` folder exists in the repo. README is the single doc, so don't create docs/ (YAGNI).

## Requirements
README updates:
1. Header/install: v1.2.0, manifest URLs `#1.2.0`.
2. Memory: "bundles stream-decrypt on read; managed peak ≈ 128 KB buffer, independent of bundle size" + the measured Phase 1 number.
3. Catalog option: `AddressablesEncryptionSettings > Encrypt Catalog` (default off). Local catalog only, remote catalog plain. Binary catalog only.
4. **Rule:** call `AddressablesEncryption.SetKey` before Addressables initializes (before any Addressables call) when Encrypt Catalog is on. Show the error text.
5. Obfuscation: the runtime asmdef `com.vanhaodev.addressablesencryption` must be excluded from renaming (types are created by name from catalog/settings.json). The attributes are in place, but some obfuscators ignore them.
6. Content update: supported. The Content Update group inherits Encryption. List the 3 fail-loud rules (Phase 2).
7. WebGL: supported (or "supported with frame-sliced fallback"). Measured hitch, recommend small bundles, no sync remote loads, cache in IndexedDB. Remove any "WebGL not supported" / on-hold wording.
8. Limitations: streaming AudioClips still blocked; remote catalog plain; key extractable from binary.
9. Testing section: batchmode commands from plan.md.

README structure ("tabs" via an anchor nav bar, as popular libs do; GitHub has no real tabs):
- Under the title + version line + one-paragraph pitch, a nav bar:
  `[Install](#install) · [Setup](#setup) · [Encrypt a group](#encrypt-a-group) · [Runtime API](#runtime-api) · [Remote & cache](#remote--cache) · [Content update](#content-update) · [Catalog](#catalog) · [WebGL](#webgl) · [Limitations](#limitations) · [Examples](#examples) · [Sample](#sample)`
- One `## ` section per link, in exactly that order, each self-contained (readable alone, repeats the minimum context it needs). Topic sections first. **Examples** is second-to-last. **Sample** is last.
- Anchors: GitHub slugs lowercase the heading, drop `&` and other punctuation, and turn spaces into `-`, so `## Remote & cache` → `#remote--cache`. Verify every nav link on the rendered GitHub page (or with a markdown preview) after writing.
- Map the content items above: 1 → Install. Setup menu + key asset/env var → Setup. Schema toggle + hashed names → Encrypt a group. API table + 4 (SetKey-before-init rule) → Runtime API. Remote cache text + 2 (memory) → Remote & cache (memory note may also go in Limitations). 6 → Content update. 3 (+ rule 4 repeated) → Catalog. 7 → WebGL. 8 + 5 (obfuscation) → Limitations. 9 (testing commands) → at the end of Setup or a short subsection of Limitations; no extra nav item (none requested).
- Examples section (code only, short captions): SetKey hardcoded, SetKey from server (async fetch then SetKey, then load), load asset (`LoadAssetAsync` / `InstantiateAsync`), download size + `DownloadDependenciesAsync`, `ClearCache` (+ `ClearDependencyCacheAsync` for plain bundles), content update flow (Check for Content Update Restrictions → Update a Previous Build, Content Update group gets Encryption automatically).
- Sample section: the current HanniDemo walkthrough (import, setup, group, build & play, toggle encryption), moved to the end unchanged except version strings.
- No other README style constraints.

Other:
- `package.json` version 1.2.0. Description unchanged unless it mentions limitations (it doesn't today).
- Mark the v1 plan's deferred items (M5 memory, content update) as done in v1.2 with a link to this plan.
- This plan: set phase statuses, plan.md frontmatter `status: completed`.

## Architecture
N/A (documentation).

## Related Code Files
- Modify: `README.md`, `Packages/com.vanhaodev.addressablesencryption/package.json`, `plans/261016-1427-addressables-encryption-v1/plan.md`, this plan's `plan.md` + phase files.

## Implementation Steps
1. Gather numbers: Phase 1 memory test log, Phase 5 WebGL report.
2. Restructure README into the nav-bar + per-topic sections layout (above), filling content items 1-9; check every anchor link.
3. Bump package.json. Search for leftover `1.1.0` strings: `grep -rn "1\.1\.0" README.md Packages/com.vanhaodev.addressablesencryption`. The sample import path `Assets/Samples/Addressables Encryption/<version>` is generic, leave it.
4. Update plans.
5. Final full EditMode + PlayMode run; manual rip check on a fresh Windows player.

## Todo List
- [ ] README restructured: nav bar + 11 sections (Examples second-to-last, Sample last), anchors checked
- [ ] package.json 1.2.0
- [ ] v1 plan deferred items closed
- [ ] v1.2 plan statuses
- [ ] Final test run + rip check

## Success Criteria
- `grep -n "1.1.0" README.md` → no install URL hits. `grep -in "webgl.*not supported\|on hold" README.md` → none.
- Every nav link jumps to its section on GitHub; Examples is second-to-last, Sample last.
- A new user can follow the README from install to encrypted catalog + content update without reading code.
- All suites green on the release commit.

## Risk Assessment
| Risk | L×I | Mitigation |
|---|---|---|
| Docs claim WebGL support before verification | M×M | Phase 5 report is a hard input; if unverified, write "experimental" |
| Version string drift (README vs package.json vs git tag) | L×L | grep check step 3; tag 1.2.0 only on user request |

## Security Considerations
README keeps "not DRM" and the key-extraction warning. No real keys in examples (use `my-secret`).

## Next Steps
Ask the user before tagging `1.2.0` / pushing (same as v1.1).
