# Automata spell-leveling testing

[Automata test map](README.md) · [Native contracts](../native-contracts.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Spell leveling must follow progression capability, distinguish Single from All,
pay live native costs, reject queued upgrades as completed capability, verify
mastery advancement, and stop ambiguous retries until explicit lifecycle
recovery.

## Primary ownership

- [AutoSpellLevelControllerHeadlessTests.cs](../../../tests/OrbModding.Tests/AutoSpellLevelControllerHeadlessTests.cs) owns controller modes, capability,
  cost, readiness, and mutation behavior.
- [AutomataFeatureStatusTests.cs](../../../tests/OrbModding.Tests/AutomataFeatureStatusTests.cs) owns locked, operational, blocked, and faulted
  projections.
- Game contract tests own `PurchaseLevel`, `TryLevelAllSpells`, prerequisite,
  cost, and unlock-upgrade shapes.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~AutoSpellLevel"
```

Then run `Fast`; include installed contracts for any reflected/native change.

## Required cases for behavior changes

- No eligible discovered spell remains `Locked`, not falsely complete.
- Single pays and verifies exactly one native level.
- All calls the audited manager action only after the capability upgrade is
  completed, not merely queued.
- Affordability and prerequisites are checked immediately before mutation.
- A failure blocks spell leveling without blocking Structure/Upgrade buying,
  casting, concepts, or Mentor.

## Runtime handoff

UAT must separately prove Single and All on a disposable save, including live
resource cost, mastery increase, queued-versus-completed unlock behavior,
save/reload, and emergency disable.
