# Testing documentation

This directory is the maintained entry point for test strategy, test selection,
module ownership, installed-game contracts, and runtime validation.

## Start here

- [Repository test strategy](strategy.md) — evidence layers, merge policy,
  coverage, compatibility, and release gates.
- [Headless E2E](headless-e2e.md) — deterministic simulation boundaries,
  scenarios, metrics, and performance reports.
- [ServiceCycle observability](../runtime-architecture/observability.md) — decode a
  production trace artifact into bounded timing and causal evidence.
- [Native contracts](native-contracts.md) — manifest and installed-assembly
  verification.
- [Runtime validation](runtime-validation.md) — ordered V0–V7 Unity/UAT gates.
- [UI overhaul validation](ui-overhaul-validation.md) — post-install native styling,
  interaction ownership, emergency resume, responsive layout, and Runtime-action checks.
Run the complete normal-development feedback loop with:

```bash
./script/test
```

This runs every portable partition with a hard 60-second wall-clock deadline,
including deterministic performance/allocation simulations and external-process
tests. The deadline exposes deadlocks or accidental soak behavior promptly.
It is per attempt, not for the whole gate: a failing run is retried up to
`ORB_TEST_ATTEMPTS` times (default 3), and a pass-after-retry is a broken test
rather than a pass. Set `ORB_TEST_ATTEMPTS=1` when you want the first answer.

On Windows, the equivalent lane helper remains:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane All
```

Run a focused risk lane with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane Reliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyReliability
```

## Module guides

| Change area | Test guide | First focused scope |
|---|---|---|
| Orb Automata | [Automata test map](automata/README.md) | select the changed feature below |
| Auto Buy policy, safety, throughput | [Auto Buy](automata/auto-buy.md) | `FullyQualifiedName~AutoBuy`, then `AutoBuyReliability` |
| Auto Cast | [Auto Cast](automata/auto-cast.md) | `FullyQualifiedName~AutoCastTests` |
| Auto Concept ServiceCycle planner and boundary | [Auto Concept](automata/auto-concept.md) | `FullyQualifiedName~AutoConcept` |
| Spell leveling | [Spell leveling](automata/spell-leveling.md) | `FullyQualifiedName~SpellLevel` |
| Automata configuration/coordinator/status | [Automata integration](automata/integration.md) | `FullyQualifiedName~Automata` |
| Orb Mentor | [Mentor](mentor.md) | `FullyQualifiedName~Mentor` |
| Orb Mod Config | [Mod Config](mod-config.md) | `FullyQualifiedName~ModConfig` |
| Orb Modding Common | [Common](common.md) | select the changed Common contract |
| Cross-feature scheduling/ownership/lifecycle | [Suite integration](suite-integration.md) | `FullyQualifiedName~ServiceCycle|FullyQualifiedName~ActionFamilyIntegration` |

The fully qualified name filters are navigation aids, not complete merge gates.
After the focused scope passes, use the guide’s required portable, contract, and
runtime gates.

## Evidence boundaries

```text
policy/component → headless adapter → headless E2E → installed contract → Unity UAT
```

- Portable success proves only game-independent behavior against stubs and
  deterministic models.
- Installed contracts prove exact audited metadata without launching Unity.
- Runtime UAT proves Harmony wiring, save behavior, controls, native side
  effects, and visible responsiveness on a disposable save.
- Deterministic performance uses modeled frames and operation counts. Host
  wall-clock timing is diagnostic and cannot replace desktop/Deck profiling.

## Maintaining these guides

When a test is added or its ownership changes:

1. Put it in the narrowest module/feature guide that explains its risk.
2. Add a stable category when the test belongs to a reusable risk lane.
3. Put allocation probes, large deterministic stress workloads, and soak-style
   iteration counts in `PerformanceSimulation`, even when they happen to run
   quickly on one machine.
4. Keep `PerformanceSimulation` and `ExternalProcess` mutually exclusive.
5. Update the relevant runtime handoff when portable evidence changes what UAT
   must still prove.
6. Record results only when they ran on the current tree or exact release
   artifact.

Do not duplicate large test inventories across pages. Feature pages own detailed
file maps; this hub owns navigation and shared rules.
