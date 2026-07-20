# Auto Buy testing

[Automata test map](README.md) · [Headless E2E](../headless-e2e.md) · [Performance plan](../../plans/performance-suite.md)

Detailed invalid, race, completion-corruption, seeded, and adverse-performance
coverage is owned by the [negative simulation plan](auto-buy-negative-simulations.md).
The reverse-engineering dossier documents the [native purchase pipeline](../../reverse-engineering/auto-buy-native-pipeline.md),
[queue/completion model](../../reverse-engineering/auto-buy-queue-and-completion.md),
[simulation evidence map](../../reverse-engineering/auto-buy-simulation-evidence.md),
and [stage-profile boundary](../../reverse-engineering/auto-buy-stage-profiles.md).

## Risk contract

Auto Buy must select fairly, respect live affordability and reserves, preserve
manual queue room, revalidate every individual level, avoid completed or stale
candidates, restore native global state, and remain responsive under rapid queue
turnover. The game remains authoritative for identity, availability, cost,
level, completion, and queue admission.

## Test layers

| Concern | Primary tests |
|---|---|
| Candidate/group/continuation decisions | [AutoBuyDecisionTests.cs](../../../tests/OrbModding.Tests/AutoBuyDecisionTests.cs), [AutoBuyTests.cs](../../../tests/OrbModding.Tests/AutoBuyTests.cs) |
| Dirty resources, parking, rejection, lifecycle | [AutoBuyDirtyResourceTests.cs](../../../tests/OrbModding.Tests/AutoBuyDirtyResourceTests.cs), [AutoBuyLifecycleTests.cs](../../../tests/OrbModding.Tests/AutoBuyLifecycleTests.cs), [AutoBuyRejectionTelemetryTests.cs](../../../tests/OrbModding.Tests/AutoBuyRejectionTelemetryTests.cs) |
| Native catalog/adapters and multi-buy | [AutoBuyCatalogHeadlessTests.cs](../../../tests/OrbModding.Tests/AutoBuyCatalogHeadlessTests.cs), [NativeMultiBuyScopeTests.cs](../../../tests/OrbModding.Tests/NativeMultiBuyScopeTests.cs), [QueueCapacitySnapshotTests.cs](../../../tests/OrbModding.Tests/QueueCapacitySnapshotTests.cs) |
| Complete queue/economy journeys | [AutoBuySimulationE2ETests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationE2ETests.cs), [AutoBuyReliabilityTests.cs](../../../tests/OrbModding.Tests/AutoBuyReliabilityTests.cs) |
| Invalid observations and mutation failures | [AutoBuySimulationFailureTests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationFailureTests.cs) |
| Live queue/economy/lifecycle races | [AutoBuySimulationRaceTests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationRaceTests.cs) |
| Malformed completion observations | [AutoBuySimulationCompletionTests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationCompletionTests.cs) |
| Simulator contract | [AutoBuySimulationContractTests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationContractTests.cs) |
| Seeded mixed-event reliability | [AutoBuySimulationStateMachineTests.cs](../../../tests/OrbModding.Tests/AutoBuySimulationStateMachineTests.cs) |
| Stage and adverse throughput | [AutoBuyStagePerformanceTests.cs](../../../tests/OrbModding.Tests/AutoBuyStagePerformanceTests.cs), [AutoBuyAdversePerformanceTests.cs](../../../tests/OrbModding.Tests/AutoBuyAdversePerformanceTests.cs) |
| Real native shape | `OrbModding.GameContractTests` Auto Buy contracts |

## Commands

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyDecision
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyReliability
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoBuyPerformance
```

Use `Fast` after the focused lanes because Auto Buy shares Common scheduling,
configuration, decisions, ownership, and mutation verification with other
features.

## Decision coverage

Decision tests own:

- stable candidate ranking and deterministic order;
- Structure group size versus ranked-pass continuation;
- one-level finite Upgrade behavior;
- unavailable-candidate isolation;
- reserve monotonicity;
- fairness before a candidate is revisited;
- explicit characterization of current behavior before the new independent
  group-size/continuation policy replaces it.

When changing policy, add the desired contract first. Do not weaken a current
characterization test merely to make the implementation pass; rename or replace
it when the intended contract genuinely changes.

## Reliability coverage

Reliability tests own live queue-capacity and reserve changes, resource
threshold crossings, completion effects, lifecycle replacement, manual actions,
finite maximum levels, ambiguous mutation outcomes, native multi-buy restoration,
and healthy sibling progress. Safety invariants should be checked after the
event that can violate them, not only at the end of a long simulation.

The seeded state machine uses four reviewed deterministic seeds and 240 events
per seed. Its failure message reduces to the first failing prefix and prints a
bounded replay-compatible/synthetic event tail. Synthetic fault controls are
never accepted as runtime-replay evidence.

## Synthetic performance workloads

| Stage | Structures | Target per Structure | Upgrades | Completion cadence |
|---|---:|---:|---:|---:|
| Early | 8 | 10 | 2 | 1 per 60 frames |
| Mid | 64 | 40 | 12 | 1 per 15 frames |
| Late | 180 | 100 | 24 | 1 per 4 frames |
| Endgame | 180 | 1,000 | 24 | 1 per frame |

These are stable stress profiles, not observed progression populations. Only
the 180-Structure total is grounded in the reviewed serialized mapping; see the
[stage-profile boundary](../../reverse-engineering/auto-buy-stage-profiles.md).

The checked report owns frames to all submissions/completions, theoretical
submission overhead, queue depth, idle purchasable frames, candidate evaluations,
cost/lifecycle reads, and repeated-Structure coverage. Wall-clock time is never
the sole gate.

## Runtime handoff

Portable tests cannot prove Harmony callbacks, actual native cost deduction,
queue visualization, save/load effects, or player controls. Follow Automata V3
and V4 in the [runtime protocol](../runtime-validation.md), including Structure
and Upgrade isolation, reserves, action multiplier, queue reservation, sustained
repeat fairness, and emergency disable.

## Known next work

- Compare the same four stage definitions through the pre-change and candidate
  engines, not only against checked historical JSON.
- Resolve the persistent highest-ranked pre-mutation rejection starvation gap,
  then enable the quarantined `PermanentlyFailingLeaders` adverse gate.
- Strengthen endgame queue-continuity targets after the new grouping policy has
  an achievable reviewed baseline.
- Add controlled allocation evidence and desktop/Steam Deck runtime profiles.
