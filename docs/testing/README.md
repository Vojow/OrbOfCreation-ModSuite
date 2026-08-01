# Testing documentation

This directory contains executable test selection and runtime-validation
guidance. Historical implementation checklists do not belong here.

## Required development gate

Run the portable/profile lane and installed-game contracts serially:

```bash
ORB_TEST_ATTEMPTS=1 ./script/test
OOC_GAME_DIR=/path/to/audited/game dotnet test \
  tests/OrbModding.GameContractTests/OrbModding.GameContractTests.csproj \
  --configuration Release
```

`./script/test` runs the ordinary portable suite, the compile-time profile suite,
and the profiled trace-tool build under a 60-second deadline. The installed lane
reads the audited assemblies without launching Unity. Do not run these commands
in parallel: they share restore and output trees.

Record ordinary, profile, and installed totals together with manifest schema,
contract, source-exemption, known-entity, and compiler-warning totals. Compare
them with the target branch in both directions and explain every change. A retry
that turns red into green is a flake report, not clean evidence.

On Windows, the supported lane wrapper is:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane All
```

## Evidence layers

```text
component -> native-shaped integration -> headless journey -> installed metadata -> Unity UAT
```

- Portable tests prove behavior against source-only stubs whose job is to model
  the runtime accurately.
- Installed contracts prove the accepted assembly hashes and exact metadata
  shape, not Unity behavior.
- Runtime validation proves Harmony wiring, native effects, save/load, UI,
  player control, and responsiveness on a disposable save.

See [repository strategy](strategy.md), [headless E2E](headless-e2e.md),
[native contracts](native-contracts.md), and
[runtime validation](runtime-validation.md).

## Module guides

| Area | Guide | Focused selector |
|---|---|---|
| Automata | [Feature map](automata/README.md) | select the affected feature |
| Auto Buy | [Auto Buy](automata/auto-buy.md) | `FullyQualifiedName~AutoBuy` |
| Auto Cast | [Auto Cast](automata/auto-cast.md) | `FullyQualifiedName~AutoCast` |
| Auto Concept | [Auto Concept](automata/auto-concept.md) | `FullyQualifiedName~AutoConcept` |
| Auto Items | [Auto Items](automata/auto-items.md) | `FullyQualifiedName~AutoItems` |
| Auto Scribe | [Auto Scribe](automata/auto-scribe.md) | `FullyQualifiedName~AutoScribe` |
| Spell leveling | [Spell leveling](automata/spell-leveling.md) | `FullyQualifiedName~SpellLevel` |
| Shared Automata composition | [Automata integration](automata/integration.md) | `FullyQualifiedName~Automata` |
| Mentor | [Mentor](mentor.md) | `FullyQualifiedName~Mentor` |
| Mod Config and suite UI | [Mod Config](mod-config.md) | `FullyQualifiedName~ModConfig` |
| Common | [Common](common.md) | select the changed contract |
| Cross-feature runtime | [Suite integration](suite-integration.md) | `FullyQualifiedName~ServiceCycle` |

Focused filters are diagnostic navigation, never substitutes for the complete
gate. A testing page stays only while its commands, paths, and claimed ownership
can be checked against the current tree.
