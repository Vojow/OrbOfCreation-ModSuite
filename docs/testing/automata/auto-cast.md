# Auto Cast testing

[Automata test map](README.md) · [Native contracts](../native-contracts.md) · [Runtime protocol](../runtime-validation.md)

## Risk contract

Auto Cast must take turns across the equipped loadout, respect the reserve floor
and the resource start threshold, hold and release charged spells exactly once,
pause for manual casting, interlock new fires with native consumable preparation,
verify the audited native fire boundary, and isolate failures from other Automata
action families.

The split between the two halves is itself part of the contract. The worker sees
the published loadout, its readiness, prices, and consumable activity; whether a
target request is already open, whether the caster is free, whether the slot
still holds the spell that was planned, and whether the spell has anything to aim
at are all read live at the action boundary, because none of them is on the
snapshot (W60). Published queued, preparation, and pending-usage evidence prevents
obviously conflicting plans, while the same exact native state is re-read on the
main thread immediately before `Spell.Fire`. Charge release bypasses both gates.

## Primary ownership

- [AutoCastCycleEvaluatorTests.cs](../../../tests/OrbModding.Tests/Services/AutoCast/Runtime/ServiceCycle/AutoCastCycleEvaluatorTests.cs) owns
  the configuration gate, the admission ladder, the rotation cursor, the channel
  pause, and the full-charge hold's whole lifetime.
- [AutoCastCycleActionAdapterTests.cs](../../../tests/OrbModding.Tests/Services/AutoCast/Runtime/ServiceCycle/AutoCastCycleActionAdapterTests.cs) owns
  slot re-identification, the live refusals, the epoch, ownership and
  manual-pause guards, the verified one-fire delta, and the block-until-lifecycle
  rule.
- [AutoCastFeatureStatusProjectorTests.cs](../../../tests/OrbModding.Tests/Services/AutoCast/Diagnostics/AutoCastFeatureStatusProjectorTests.cs) owns
  the order of the terms in the health line.
- [AutoCastTests.cs](../../../tests/OrbModding.Tests/AutoCastTests.cs) owns the
  configuration defaults and the toggle button.
- [NativeMutationVerifierTests.cs](../../../tests/OrbModding.Tests/NativeMutationVerifierTests.cs) owns capture/execute/capture failure semantics.
- Game contract tests own exact loadout, cast, resource, target, and Harmony
  members from the installed assemblies.

## Focused command

```powershell
dotnet test tests/OrbModding.Tests/OrbModding.Tests.csproj -p:UseGameStubs=true --filter "FullyQualifiedName~AutoCast"
```

Then run `Fast`; include installed contracts for any reflected/native change.

## Required cases for behavior changes

- Disabled or emergency-stopped configuration plans no cast and still reschedules.
- A spell the reserve floor or the start threshold refuses is attributed to that
  term rather than silently dropped.
- A charged spell is held for exactly one cast and released once, including when
  the setting is turned off or the loadout is rearranged underneath the hold.
- A channel in progress pauses the whole rotation rather than its own slot.
- A queued, preparing, or non-expired pending consumable prevents a new Fire in
  both the immutable worker admission and the final native preflight, but never
  prevents releasing an existing charge hold.
- The boundary re-resolves the slot by position and identity, and refuses rather
  than casting whatever is in the position.
- Fire exceptions or unverified results block only that spell, and only until the
  next lifecycle. If native fire opens target requests before throwing, the
  boundary resolves those requests before returning the fault so player input is
  not left captured by the game's targeting layer.
- Manual casting pauses future automation without stranding a live charge hold.

## Runtime handoff

V3 proves control placement and read-only state. V4 proves a real cast, resource
deduction, charge behavior, target behavior, interruption, and emergency
disable. An installed contract alone cannot prove that Harmony observation saw
the actual runtime fire.
