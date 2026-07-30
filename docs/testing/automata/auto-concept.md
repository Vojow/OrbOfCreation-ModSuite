# Auto Concept testing

[Automata test map](README.md) · [Native contracts](../native-contracts.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Auto Concept must rank and rotate stable Concept identities from the published
world, preserve manual quantities where configured, obey native slot and mastery
limits, and own only quantities whose verified mutations it committed.

The worker sees recipe progress, core types, active and queued quantities, drain
ratios, and resource vectors from the snapshot. The quantity-dependent native
drain multiplier cannot be derived from those facts: the action boundary creates
the prospective instance and owns the halving search, reserve test, quantity
floor, live identity and settledness checks, and verified add/remove mutation
(W62). Projection refusals do not latch; ambiguous mutations block until the
next lifecycle.

## Primary ownership

- [AutoConceptCycleEvaluatorTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Runtime/ServiceCycle/AutoConceptCycleEvaluatorTests.cs)
  owns the five-priority decision ladder, ranking, training sessions, ownership,
  preferred replacement window, and deterministic candidate cursor.
- [AutoConceptCycleActionAdapterTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Runtime/ServiceCycle/AutoConceptCycleActionAdapterTests.cs)
  owns epoch, configuration, ownership-family, and native-preflight result
  mapping.
- [AutoConceptNativeAdapterTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Runtime/ServiceCycle/AutoConceptNativeAdapterTests.cs)
  owns live re-identification, settled quantity checks, prospective drain parity,
  mastery clamping, verified mutations, and lifecycle recovery.
- [AutoConceptServiceCompositionTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Runtime/ServiceCycle/AutoConceptServiceCompositionTests.cs)
  owns worker-to-action composition, fallback cadence, configuration wake-up,
  idle-reason handoff to feature health, and structural worker-state safety.
- [AutoConceptFeatureStatusProjectorTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Diagnostics/AutoConceptFeatureStatusProjectorTests.cs)
  owns feature health projection.
- [AutoConceptDomainClassifierAdoptionTests.cs](../../../tests/OrbModding.Tests/AutoConceptDomainClassifierAdoptionTests.cs)
  owns action-boundary adoption of the shared typed classifier.
- [GameWorldCollectorTests.cs](../../../tests/OrbModding.Tests/Runtime/World/GameWorldCollectorTests.cs)
  owns the concept recipe, assignment, and drain-vector publication.

## Focused command

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/test-portable.ps1 -Lane AutoConceptReliability
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~AutoConcept"
```

Then run the portable gate. Include installed contracts for any world-reader or
native-boundary change.

## Required cases for behavior changes

- Timed Cycle orders every unlocked, allowed concept across concept types. A
  cross-type rotation proceeds only when removing the exact active assignment
  provably opens a native typed or typeless slot for its replacement.
- Locked or undiscovered concepts are never assigned or counted as rotation
  candidates, and unlock state is revalidated immediately before mutation.
- Same-name/different-UUID or wrong-type content never aliases.
- Unsafe owned drain rolls back before any balancing action.
- A recent rotation prefers its remembered compatible replacement.
- A native slot or prospective-drain rejection defers only the refused candidate, advances to another
  unlocked candidate, permits safe depth on the active assignment, and cannot create an immediate retry
  loop.
- A successfully assigned Timed Cycle replacement starts its own complete settled-active training
  period and moves to the back of the timed rotation order; depth settlement does not restart it.
- Multi-slot scenarios fill every independently compatible acquired slot before depth, keep a
  separate settled-training deadline for each active assignment, and permit one eligible slot to
  rotate while another assignment is still training.
- Progress simulations use the recipe's native-resolved completion requirement after completion-time
  and time-scaling modifiers, then apply the active instance's resolved speed. Resource-safety
  scenarios cover drain ratio separately. Tests must not assume a common wall-clock completion
  rate across concepts or quantities.
- Breadth precedes mastery rebalance, and rebalance precedes depth.
- Manual-preservation policy removes only verified owned quantity.
- Timed training starts only after assignment settlement.
- A committed depth change records its queued target as suite-owned before native settlement; the
  later settled quantity must not restart Timed Cycle's active-session deadline.
- Feature health distinguishes an active training wait from the post-training
  absence of another unlocked, assignable replacement.
- The world snapshot publishes `AlchemyRecipeSO.GetMaxUsageSlots()` rather than
  the raw `maxUsageSlots` modifier. Its native `-1` sentinel must resolve to the
  mastery-derived or unlimited quantity before breadth, rotation, or depth runs.
- Depth clamps to both configured quantity and the live mastery limit.
- Projection preserves the reserve test, quantity floor, and halving search.
- The live drain watchdog rolls back owned depth while stock remains positive when a drained
  resource's net rate has become negative.
- A stale epoch, changed identity, unsettled quantity, or ambiguous mutation
  fails closed without touching an unrelated assignment.

## Trace-to-regression workflow

Treat a recent-event dump as the start of a reproducible scenario, not as the final
test evidence:

1. Preserve the dump, `LogOutput.log`, exact DLL SHA-256, and effective Auto Concept
   settings from the same launch.
2. Decode the decision journal and state the causal sequence in terms of UUID,
   action kind, receipt result, queued quantity, settled quantity, and monotonic time.
3. Add the smallest failing evaluator regression. If the defect crosses a receipt,
   settlement, or more than one native action, also add a `HeadlessE2E` journey.
4. Add or extend a deterministic `PerformanceSimulation` invariant for churn,
   retry cadence, rotation fairness, and total action count.
5. Run `AutoConceptReliability`, the complete Auto Concept scope, `./script/test`,
   installed-game contracts, and the proportional V3/V4 runtime probe.
6. Install only while the game is closed, record the installed hash, launch fresh,
   and compare the new log against the expected bounded sequence.

The trace-derived stop conditions are strict: two successful rotations closer than
`TrainingPeriodSeconds`, the same rejected candidate retried inside
`FallbackEvaluationIntervalSeconds`, or a repeated two-concept cycle while another
safe unlocked candidate exists fails the probe. Turn Auto Concept off immediately
and retain the dump if any condition occurs.

`AutoConceptJourneyTests.cs` owns the cross-layer regression: a real native-adapter
journey, the dump-shaped headless E2E path, and a 600-second deterministic
round-robin simulation. It also owns a three-active-slot simulation whose concepts
have different native-resolved completion requirements and quantity-dependent
resolved speeds. These tests assert settled training, safe depth, rejection
backoff, advancement and mastery deltas, independent slot rotation, advancement
beyond two concepts, and bounded action volume.

## Runtime handoff

Runtime UAT must observe real breadth and depth assignments, manual preservation,
timed rotation, unsafe-drain rollback, mastery changes, save/reload, and control
state. Use a disposable save and disable Auto Concept immediately after the
bounded probe.
