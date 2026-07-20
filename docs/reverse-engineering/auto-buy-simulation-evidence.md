# Auto Buy simulation evidence map

[Reverse-engineering index](README.md) · [Native pipeline](auto-buy-native-pipeline.md) · [Queue/completion model](auto-buy-queue-and-completion.md) · [Negative simulation matrix](../testing/automata/auto-buy-negative-simulations.md)

## How to read this map

Each seam has four independent questions:

1. Is the native member shape statically verified?
2. Does production Automata implement a fail-closed boundary?
3. Does a portable test execute that production boundary or only a model?
4. Has the exact side effect/order been observed in Unity?

A portable simulation can enforce policy and safety without claiming question
four. Conversely, a one-time runtime observation does not replace deterministic
regression coverage.

## Admission and mutation seams

| Simulation seam | Native/static source | Production boundary | Portable owner | Remaining runtime evidence |
|---|---|---|---|---|
| Queue unavailable/contradictory | `ActionManager`, `ActionableListVariable`, `IntVariable` contracts | `TryCaptureQueueCapacity` + `QueueCapacitySnapshot` | `QueueCapacitySnapshotTests`, `AutoBuySimulationFailureTests` | failure behavior when native objects are temporarily null during real lifecycle |
| Manual action takes room | shared `ActionManager` queue | second queue capture and live native purchase rejection | `AutoBuySimulationRaceTests` | exact interleaving with manual UI action |
| Availability unknown | `IsAvailable()` contracts | admission adapter requires known availability | `AutoBuySimulationFailureTests`, headless adapter tests | exception/null behavior in the installed build |
| Native admission unknown | `CanPurchase()` contracts | complete-contract evidence | `AutoBuySimulationFailureTests` | native rejection reasons are not typed by the game API |
| Cost unresolved/malformed | `GetPurchaseCost()`, resource and `BigDouble` contracts | full-vector decoder and resource snapshot cache | `AutoBuyDirtyResourceTests`, `AutoBuySimulationFailureTests` | malformed native collection behavior in a real update |
| Cost/resource changes before submit | live resource/cost contracts | per-level reevaluation and final native submit | `AutoBuySimulationRaceTests` | real callback capable of changing cost inside the same frame |
| Pre-mutation rejection | adapter preflight | `LastNativeMutationOutcome` remains zero; scheduler applies bounded timed retry | `AutoBuySimulationFailureTests`, `AutoBuyAdversePerformanceTests` NF-03 | frequency and native causes in ordinary play |
| Attempted ambiguous mutation | queued-state capture + native purchase contract | `NativeMutationVerifier` and lifecycle block | `AutoBuySimulationFailureTests`, `AutoBuyDirtyResourceTests` | controlled disposable runtime fault only |
| Upgrade multi-buy isolation | `GetMultiBuy`, `AsInt`, `SetValue` contracts | `NativeMultiBuyScope` verified enter/restore/quarantine | `NativeMultiBuyScopeTests`, headless purchase tests | modifier-key changes during active mutation |
| Exact one-level result | queued-state methods | before/after exact `+1` postcondition | headless adapter tests, simulation E2E | visible queue/resource confirmation for current beta |
| Ownership loss | no game contract; suite-local lease | ownership recheck immediately before mutation | action-family integration tests | third-party plugins outside known ownership registry |

## Lifecycle and completion seams

| Simulation seam | Native/static source | Production boundary | Portable owner | Remaining runtime evidence |
|---|---|---|---|---|
| Registry unlock/replacement | `StructureSO.All`, `UpgradeSO.All`, stable UUID contracts | incremental candidate index and lifecycle epoch | `AutoBuyCatalogHeadlessTests`, `AutoBuyLifecycleTests` | registry population timing on new game/save/NG+ |
| Queue signal during prepared group | `QueueBuild(int)`, `UpgradeSO.Purchase()` Harmony targets | exact-identity automated scope | `AutoBuyDirtyResourceTests`, `AutoBuySimulationRaceTests` | callback ordering relative to resource deduction |
| Native completion | both `CompleteAction()` Harmony targets | exact candidate invalidation + settlement gate | `AutoBuySimulationE2ETests`, `AutoBuySimulationCompletionTests` | bulk/echo callback trace |
| Malformed completion identity/count | no typed native payload; simulator contract | exact simulated queue preflight | `AutoBuySimulationCompletionTests` | determine whether real callback can supply equivalent ambiguity |
| Lifecycle replacement mid-work | save/load/reset/NG+ contracts | cancel prepared work and rebuild epoch | `AutoBuySimulationRaceTests`, state-machine tests | same-UUID native reference behavior per boundary |
| Queue clear during settlement | modeled reload behavior | lifecycle clear + settlement reset | `AutoBuySimulationCompletionTests` | queue state at real save/load/reset boundaries |

## Performance seams

| Simulation seam | Evidence today | What it proves | What it does not prove |
|---|---|---|---|
| One mutation per modeled frame | deterministic stopwatch costs and coordinator | scheduler slicing and operation bounds | Unity wall-clock cost |
| 180-Structure late catalog | serialized entity mapping | reviewed definition count | availability in a particular save |
| 304 queue capacity | prior runtime observation plus live-capacity adapter | one observed save/build and correct live-read path | universal capacity at every progression point |
| Early/mid/late/endgame candidate subsets | synthetic stress profiles | comparative scaling and regression detection | exact player progression populations |
| Completion cadences | synthetic stress profiles | queue-consumer pressure from 1/60 to 1/frame | actual action-duration distribution |
| Runtime-derived perturbations | bounded synthetic Bulk-3 storms, transient cost outages, 35 ms heavy-tail reads, and 28-to-137 catalog growth | recovery, coalescing, resumability, and operation-count regressions under shapes seen in diagnostics | attribution to an exact save, game build, or Unity wall-clock cost |

## Promotion rule

Promote a modeled seam to runtime-observed only when a sanitized record contains:

- audited game/assembly identity;
- lifecycle boundary and generation;
- stable UUID plus expected native type;
- native capacity and remaining room;
- before/after queued state and relevant resource quantities;
- ordered callbacks/events; and
- enough configuration to reproduce policy without exposing a save or personal
  path.

Then add or extend a deterministic replay/fixture. Do not encode synthetic fault
controls as if the game emitted them.
