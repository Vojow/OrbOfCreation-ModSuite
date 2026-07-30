# Native contract manifest

[Back to testing hub](README.md) · [Repository strategy](strategy.md) · [Reverse-engineering audit](../reverse-engineering/audit.md)

[`data/native-contracts.json`](../../data/native-contracts.json) is the reviewable compatibility inventory for game members resolved through reflection or patched with Harmony. It records:

- assembly identities plus each audited platform baseline's exact two-file SHA-256 pair, date, build description, and platform-relative provenance;
- the declaring native type and its visibility;
- member kind, name, visibility, staticness, return/value type, and ordered parameter types;
- the owning feature and whether use is reflection or Harmony;
- `place` — where the contract sits: `capture` (collected once into the world snapshot), `action` (read at
  an action boundary), `patch` (Harmony), or `legacy` (explicit residual debt outside those places);
- `sourceTokens`, the literal type and member strings a selector may name, which is how the audit
  recognizes a call site;
- source-only aliases where runtime helpers use a property name while metadata exposes its accessor.

A contract records the shape of the game's surface and where the suite stands relative to it, not which of
our files touches it. Contracts carry no `sources[]` path list: a per-file audit could only ever catch "this
file reflects on a member declared for some other feature", never an undeclared native target, while coupling
a game-surface audit to the repository's own folder layout.

The manifest does not change runtime behavior. Existing adapters and Harmony target factories still validate contracts and fail closed when a member is missing or ambiguous.

For Auto Buy, the manifest-to-implementation relationship is documented in the
[native purchase pipeline](../reverse-engineering/auto-buy-native-pipeline.md)
and [queue/completion model](../reverse-engineering/auto-buy-queue-and-completion.md).
For Auto Items, see the
[native Scroll and Relic pipeline](../reverse-engineering/auto-items-native-pipeline.md).

## Enforcement layers

`NativeContractManifestTests` has two modes:

1. On every machine, it validates schema completeness, unique IDs, complete exact-pair baselines, assembly-hash synchronization with `GameAssemblyAudit`, platform-relative provenance without user-specific paths, every contract's `place`, and the bounded source audit.
2. When `OOC_GAME_DIR` points to an installation, it verifies both assembly hashes and every manifest
   type/member against PE metadata without loading Unity or the game into the test process. A hash mismatch
   remains a hard failure but does not short-circuit the read-only metadata audit, so one run reports both
   the unknown binary identity and every structural contract difference.

The source audit scans the supported C# trees named in `sourceAudit.roots` for reflection and Harmony use. Every literal native selector it finds must be declared by *some* contract; the check is on the native shape, not on which file names it, so moving a file cannot break an audit of the game's surface. `sourceAudit.exemptions` carries exact-path exemptions with a non-empty reason: generic BepInEx validation, Steamworks compatibility, and tolerant UI-cloning/navigation reflection are exempted because treating those implementation details as exact game-progression contracts would create brittle false positives.

A separate check keeps `place` honest: every contract is `capture`, `action`, or `patch` unless its id is on the shrinking `UnmigratedServiceContracts` allowlist, which is the only way to declare `legacy`. All service-runtime debt is gone; the lone residual entry is a read-only UI icon lookup. This is what makes "capture once, then decide" measurable rather than asserted.

CI runs the game-contract test project without game references. Installed metadata validation is skipped there, but manifest structure, `place`, and source coverage remain mandatory.

## Adding or changing a native target

1. Audit the installed assembly named in the manifest. Confirm the exact declaring type, overload, visibility, staticness, return/value type, and ordered parameters.
2. Add or update the manifest entry in the same change as the reflection or Harmony source. Record every owning feature, the usage mode, the `place`, and the source tokens a selector names.
3. If the selector is deliberately generic or framework-only, add a narrow exact-path exemption and explain why it is not an Orb of Creation gameplay contract. Do not exempt a mixed gameplay adapter.
4. Run the portable suite and `dotnet test tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj -p:UseGameStubs=true` with no `OOC_GAME_DIR`; this is the CI-equivalent source gate.
5. On Windows, run `tools/test-modsuite.ps1 -GameRoot <path>`. On other platforms, set `OOC_GAME_DIR` and run the game-contract project plus the supported real-reference project builds directly with `dotnet`; PowerShell is not required for their generated-entity verification.
6. Complete the relevant interactive runtime validation gate. Metadata compatibility does not prove lifecycle safety or correct native side effects.

## Auditing a game update

Create the update as an ordinary manifest diff rather than replacing the file wholesale:

- add or update one complete platform baseline pair with its audit date, build description, and relative provenance;
- remove contracts no longer named by any supported source;
- change signatures and visibility in place when the native member changed;
- add new contract IDs for new targets and record affected features;
- keep the old and new manifest diff in the review evidence so added, removed, and changed contracts are explicit.

A hash-only update is insufficient. Every contract must pass against the new assemblies, and runtime fail-closed guards remain required even after the manifest is refreshed.

When multiple platform builds are supported, admit exact assembly pairs rather than independent per-file
hash allowlists. This prevents a main assembly from one audited build being combined with a first-pass
assembly from another. Platform path discovery is a separate compatibility contract: a recognized hash pair
must still be read from the platform's actual Managed directory. An unknown complete pair admits only the
quarantined control plane unless the player explicitly accepts that exact pair; an incomplete or
undiscoverable pair still fails closed completely.
