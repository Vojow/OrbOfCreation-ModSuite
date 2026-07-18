# Native contract manifest

[Back to testing](testing.md) · [Reverse-engineering audit](../reverse-engineering/audit.md)

[`data/native-contracts.json`](../../data/native-contracts.json) is the reviewable compatibility inventory for game members resolved through reflection or patched with Harmony. It records:

- the audited assembly file, SHA-256, date, build description, and provenance;
- the declaring native type and its visibility;
- member kind, name, visibility, staticness, return/value type, and ordered parameter types;
- the owning feature, whether use is reflection or Harmony, and the source files that select the contract;
- source-only aliases where runtime helpers use a property name while metadata exposes its accessor.

The manifest does not change runtime behavior. Existing adapters and Harmony target factories still validate contracts and fail closed when a member is missing or ambiguous.

## Enforcement layers

`NativeContractManifestTests` has two modes:

1. On every machine, it validates schema completeness, unique IDs, assembly-hash synchronization with `GameAssemblyAudit`, declared source paths, and the bounded source audit.
2. When `OOC_GAME_DIR` points to an installation, it verifies both assembly hashes and every manifest type/member against PE metadata without loading Unity or the game into the test process.

The source audit scans supported Automata, Mentor, and Mod Config C# trees for reflection and Harmony use. A file must either own manifest contracts or have an exact-path exemption with a non-empty reason. Literal native selectors in audited files must resolve to a contract associated with that file. Generic BepInEx validation, Steamworks compatibility, and tolerant UI-cloning/navigation reflection are exempted because treating those implementation details as exact game-progression contracts would create brittle false positives.

CI runs the game-contract test project without game references. Installed metadata validation is skipped there, but manifest structure and source coverage remain mandatory.

## Adding or changing a native target

1. Audit the installed assembly named in the manifest. Confirm the exact declaring type, overload, visibility, staticness, return/value type, and ordered parameters.
2. Add or update the manifest entry in the same change as the reflection or Harmony source. Record every owning feature, usage mode, and selecting source path.
3. If the selector is deliberately generic or framework-only, add a narrow exact-path exemption and explain why it is not an Orb of Creation gameplay contract. Do not exempt a mixed gameplay adapter.
4. Run the portable suite and `dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -p:UseGameStubs=true` with no `OOC_GAME_DIR`; this is the CI-equivalent source gate.
5. On a game computer, run `tools/test-modsuite.ps1 -GameRoot <path>` so the manifest is checked against the installed files and supported projects build against real references.
6. Complete the relevant interactive runtime validation gate. Metadata compatibility does not prove lifecycle safety or correct native side effects.

## Auditing a game update

Create the update as an ordinary manifest diff rather than replacing the file wholesale:

- update assembly hashes, audit date, build description, and provenance;
- remove contracts no longer selected by supported source;
- change signatures and visibility in place when the native member changed;
- add new contract IDs for new targets and record affected features;
- keep the old and new manifest diff in the review evidence so added, removed, and changed contracts are explicit.

A hash-only update is insufficient. Every contract must pass against the new assemblies, and runtime fail-closed guards remain required even after the manifest is refreshed.
