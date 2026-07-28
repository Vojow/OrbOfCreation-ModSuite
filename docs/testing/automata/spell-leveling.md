# Automata spell-leveling testing

[Automata test map](README.md) · [Native contracts](../native-contracts.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Spell leveling must follow progression capability, distinguish Single from All,
pay live native costs, reject queued upgrades as completed capability, verify
mastery advancement, and stop ambiguous retries until explicit lifecycle
recovery.

The split between the two halves is itself part of the contract. The worker sees
discovery and mastery readiness and nothing else; the leveling prerequisite and
the level's affordability are read live at the action boundary, because neither
is on the snapshot (W59). A test that lets the planner decide either of those is
testing a design the suite does not have.

## Primary ownership

- [SpellLevelCycleEvaluatorTests.cs](../../../tests/OrbModding.Tests/Services/SpellLeveling/Runtime/ServiceCycle/SpellLevelCycleEvaluatorTests.cs) owns
  the configuration gate, discovery, readiness, ranking, and which capability the
  level-all upgrade grants.
- [SpellLevelCycleActionAdapterTests.cs](../../../tests/OrbModding.Tests/Services/SpellLeveling/Runtime/ServiceCycle/SpellLevelCycleActionAdapterTests.cs) owns
  prerequisites, affordability, epoch and ownership guards, the verified `+1`
  delta, and the block-until-lifecycle rule.
- [SpellLevelFeatureStatusProjectorTests.cs](../../../tests/OrbModding.Tests/Services/SpellLeveling/Diagnostics/SpellLevelFeatureStatusProjectorTests.cs) owns
  locked, operational, blocked, and not-ready projections.
- Game contract tests own `PurchaseLevel`, `TryLevelAllSpells`, prerequisite,
  cost, and unlock-upgrade shapes.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~SpellLevel"
```

Then run `Fast`; include installed contracts for any reflected/native change.

## Required cases for behavior changes

- No eligible discovered spell remains `Locked`, not falsely complete.
- Single pays and verifies exactly one native level.
- All calls the audited manager action only after the capability upgrade is
  completed, not merely queued.
- Affordability and prerequisites are checked immediately before mutation,
  at the boundary rather than in the plan.
- A failure blocks spell leveling without blocking Structure/Upgrade buying,
  casting, concepts, or Mentor.

## Runtime handoff

UAT must separately prove Single and All on a disposable save, including live
resource cost, mastery increase, queued-versus-completed unlock behavior,
save/reload, and emergency disable.
