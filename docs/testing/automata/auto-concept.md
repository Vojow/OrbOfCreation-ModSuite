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
- Breadth precedes mastery rebalance, and rebalance precedes depth.
- Manual-preservation policy removes only verified owned quantity.
- Timed training starts only after assignment settlement.
- Feature health distinguishes an active training wait from the post-training
  absence of another unlocked, assignable replacement.
- Depth clamps to both configured quantity and the live mastery limit.
- Projection preserves the reserve test, quantity floor, and halving search.
- A stale epoch, changed identity, unsettled quantity, or ambiguous mutation
  fails closed without touching an unrelated assignment.

## Runtime handoff

Runtime UAT must observe real breadth and depth assignments, manual preservation,
timed rotation, unsafe-drain rollback, mastery changes, save/reload, and control
state. Use a disposable save and disable Auto Concept immediately after the
bounded probe.
