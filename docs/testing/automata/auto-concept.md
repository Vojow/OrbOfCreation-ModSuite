# Auto Concept testing

[Automata test map](README.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Auto Concept must use stable typed identities, distinguish acquired/compatible
concepts from merely registered definitions, preserve manual assignments where
configured, obey native slot and quantity limits, and invalidate safely across
inventory, mastery, scene, save, reset, and NG+ changes.

## Primary ownership

- [AutoConceptControllerHeadlessTests.cs](../../../tests/OrbModding.Tests/AutoConceptControllerHeadlessTests.cs) owns controller decisions, slot
  management, failures, and active-list changes.
- [AutoConceptDomainClassifierAdoptionTests.cs](../../../tests/OrbModding.Tests/AutoConceptDomainClassifierAdoptionTests.cs) owns typed domain
  classification and rejected identities.
- [ConceptRuntimeHeadlessTests.cs](../../../tests/OrbModding.Tests/ConceptRuntimeHeadlessTests.cs) owns reflection-shaped runtime state.
- [AutomataTests.cs](../../../tests/OrbModding.Tests/AutomataTests.cs) owns configuration defaults and migration-facing values.
- [GameplayInvalidationBusTests.cs](../../../tests/OrbModding.Tests/GameplayInvalidationBusTests.cs) and [AutomataCoordinatorTests.cs](../../../tests/OrbModding.Tests/AutomataCoordinatorTests.cs) own
  scheduling/invalidation integration.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~AutoConcept|FullyQualifiedName~ConceptRuntime"
```

Then run `Fast`. Include `PerformanceAll` when changing discovery, invalidation,
rotation cadence, or slot traversal.

## Required cases for behavior changes

- Locked or unacquired concepts are retained for later but never assigned.
- Same-name/different-UUID or wrong-type content never aliases.
- Manual-preservation policy does not overwrite protected slots.
- Quantity depth stops at live native mastery/slot limits.
- One invalid concept does not starve healthy compatible concepts.
- Lifecycle replacement discards native references and rebuilds from stable
  identity.

## Runtime handoff

Runtime UAT must observe real slot assignments, manual preservation, mastery
limits, inventory unlocks, save/reload, and control state. Use a disposable save
and disable Auto Concept immediately after the bounded probe.
