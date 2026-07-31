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
  owns worker-to-action composition, publication wake-up,
  idle-reason handoff to feature health, and structural worker-state safety.
- [AutoConceptFeatureStatusProjectorTests.cs](../../../tests/OrbModding.Tests/Services/AutoConcept/Diagnostics/AutoConceptFeatureStatusProjectorTests.cs)
  owns feature health projection, including reasonless and summary-free operational status.
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

- Timed Cycle orders every unlocked concept across concept types. A
  cross-type rotation proceeds only when removing the exact active assignment
  provably opens a native typed or typeless slot for its replacement.
- Locked or undiscovered concepts are never assigned or counted as rotation
  candidates, and unlock state is revalidated immediately before mutation.
- Same-name/different-UUID or wrong-type content never aliases.
- Unsafe owned drain rolls back before any balancing action.
- A recent rotation prefers its remembered compatible replacement.
- A native slot or prospective-drain rejection ends the current world round. Its receipt is observed
  only with a strictly newer world, the candidate re-enters ordinary planning without deferral state,
  and persistent refusal remains loud on every later publication rather than routing around a
  collection gap.
- A successfully assigned Timed Cycle replacement starts its own complete settled-active training
  period and moves to the back of the timed rotation order; depth settlement does not restart it.
- Multi-slot scenarios fill every independently compatible acquired slot before depth, keep a
  separate settled-training deadline for each active assignment, and permit one eligible slot to
  rotate while another assignment is still training.
- Progress simulations use the recipe's native-resolved completion requirement after completion-time
  and time-scaling modifiers, then apply the active instance's resolved speed. Resource-safety
  scenarios cover drain ratio separately.
- Breadth precedes mastery rebalance, and rebalance precedes depth.
- Manual-preservation policy removes only verified owned quantity.
- Timed training starts only after assignment settlement.
- A committed depth change records its queued target as suite-owned before native settlement; later
  settlement must not restart the active Timed Cycle session.
- Feature health distinguishes an active training wait from the post-training
  absence of another unlocked, assignable replacement; operational health carries no reason or
  positive summary text.
- The world snapshot publishes `AlchemyRecipeSO.GetMaxUsageSlots()` rather than the raw
  `maxUsageSlots` modifier, so the native `-1` sentinel resolves before breadth, rotation, or depth.
- Depth clamps to the live native mastery limit.
- Projection preserves the reserve test, quantity floor, and halving search.
- The live drain watchdog rolls back owned depth while stock remains positive when a drained
  resource's net rate has become negative.
- A stale epoch, changed identity, unsettled quantity, or ambiguous mutation
  fails closed without touching an unrelated assignment.

## Trace-to-regression workflow

Treat a recent-event dump as the start of a reproducible scenario:

1. Preserve the dump, `LogOutput.log`, exact DLL SHA-256, and effective Auto Concept settings.
2. Decode the decision sequence by UUID, action kind, receipt result, queued quantity, settled
   quantity, world/configuration generations, and monotonic time.
3. Add the smallest failing evaluator regression. If it crosses a receipt, settlement, or more than
   one native action, also add a `HeadlessE2E` journey.
4. Extend a deterministic `PerformanceSimulation` invariant for churn, publication-bounded retry,
   rotation fairness, and total action count.
5. Run `AutoConceptReliability`, the complete Auto Concept scope, `./script/test`, and installed-game
   contracts before any proportional runtime probe.

`AutoConceptJourneyTests.cs` owns the cross-layer regression: a real native-adapter journey, the
dump-shaped headless path, a 600-second deterministic round-robin simulation, and a three-active-slot
simulation with distinct native-resolved completion requirements and quantity-dependent speeds. One
scenario `RunAt` is one world publication; its immediate receipt and action follow-ups deliberately
retain that same world generation.

## Runtime handoff

Runtime UAT must observe real breadth and depth assignments, manual preservation,
timed rotation, unsafe-drain rollback, mastery changes, save/reload, and control
state. Use a disposable save and disable Auto Concept immediately after the
bounded probe.
