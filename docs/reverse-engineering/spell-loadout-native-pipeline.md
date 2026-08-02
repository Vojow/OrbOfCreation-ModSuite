# Equipped-spell removal and reorder native pipeline

Status: portable implementation complete; installed contracts pass against Orb of Creation
v1.0.5; live mutation promotion remains deliberately pending.

This dossier is the native contract for M1 family B-004 (`V-SPELL-04` and `V-SPELL-05`). It
covers removing one exact equipped runtime spell and swapping two exact loadout slots. Discovery
and creation are B-002; output and augment composition are B-003.

## Evidence source and identity

The evidence was read from the pinned assemblies under `artifacts/game-v105`; no game process or
save was opened. A removal target is the non-empty runtime `Spell.guidContainer` UUID. A move
target is that same runtime UUID plus one zero-based destination slot. Recipe UUIDs and names are
decision evidence, never mutation identity.

Installed tests pin the v1.0.5 members introduced by this family:

| Member | Token or exact shape |
|---|---:|
| `Spell.IsEmpty()` | `0x06001027` |
| `Spell.CanRemove()` | `0x06001038` |
| `Spell.GetName()` | `0x06001087` |
| `SpellManager.RemoveSpell(Spell)` | `0x0600074C` |
| `AbstractListVariable<Spell>.SwapPositions(int,int)` | public instance `void(int,int)` |
| `AbstractListVariable.UpdateObservable()` | `0x060014ED` |
| `UISpellList.OnDrop()` | `0x06002701` |

The action also reuses the already-declared spell-manager singleton, active-spell list, list-value,
runtime-spell identity, and `GuidContainer.get_guid` contracts. Its complete twelve-member binding
set compiles typed delegates once per lifecycle. A missing member makes the family
`ContractUnavailable`; execution performs no reflection.

## Native transitions

### Remove one equipped runtime spell

`Spell.CanRemove()` is the player-facing gate. Its audited IL consults
`Spell.IsChargeAvailable()` and then `Spell.IsCasting()`. The MCP action invokes it again on the
Unity main thread immediately before acquiring the family permit. A false result is an honest
`native_remove_refused`; the action does not enter the more permissive manager path.

`SpellManager.RemoveSpell(Spell)` removes the supplied instance from `activeSpells`, calls
`Spell.Destroy()`, then calls `SpellManager.RecomputeSpellWeight()`. The manager also contains
warning/recharge reconciliation for non-ready spell states, which is why the action preserves the
UI's stricter `CanRemove` admission instead of treating that internal cleanup as player authority.

The outcome gate is target identity absent from the live list and the surviving non-empty runtime
UUIDs in their exact prior order. Empty-slot preservation versus list compaction is evidence, not
an assumed native invariant. Released weight, glyph usage, drain, and resources are newer-world
observations and never accounting gates. A throw after the exact outcome is observable remains
committed; an unchanged target, wrong survivor identity, or reordered survivor sequence
quarantines only B-004 until lifecycle replacement.

### Move one equipped runtime spell

Installed IL proves `UISpellList.OnDrop()` checks `DragDropContext.ListsMatch()` and
`IndicesMatch()` before calling `AbstractListVariable<Spell>.SwapPositions(source,destination)`,
then `UpdateObservable()`. The action accepts only an exact equipped runtime UUID, resolves its
current source slot at execution time, rejects an out-of-range or same-slot destination, acquires
the family permit last, and invokes those same two native members.

The outcome gate is the complete raw slot-identity sequence with exactly source and destination
exchanged. This includes empty slots, so moving into a hole is distinguishable from removal or
compaction. Observer notification is invoked but is not a substitute for the identity outcome.
A throw after the exact swap remains committed; a missing or different sequence quarantines B-004.

## MCP and world contract

`spell-slots` is the pre-decision surface. Every occupied row carries its named runtime spell and
recipe, exact slot, current cast/ready/attune state when active, native remove availability, and all
other destination slots in order with named occupants or an explicit empty marker. The shared
loadout summary includes equipped count, maximum equipped count, and empty-slot availability.

`game_spell_loadout` accepts:

- `mode="remove"` with `spellInstanceUuid`; or
- `mode="move"` with `spellInstanceUuid` and zero-based `destinationSlot`.

It has no world-generation, verbosity, receipt, or payment argument. The HTTP worker submits one
frame operation; `SpellLoadoutGameAction` runs on the Unity main thread and re-resolves every
mutable fact. A committed response is only `status` plus the newer complete named loadout,
including every next remove/move decision. No catalog join or post-mutation read is needed.
Preflight refusals name the exact gate. Only failures that reached native evidence retain named
before/after ordered slots and quarantine state.

There is no owning remove/reorder automation planner. Auto Cast reads slot positions but owns the
separate cast capability; planner symmetry is therefore not applicable and no MCP-only planner was
invented.

## Disposable-save promotion checklist

Run only in a later explicitly supervised session:

1. Record the save backup, lifecycle, scene, loadout capacity, every slot, runtime UUID, recipe,
   name, casting/readying/attuning state, weight, glyph usage, drain, and relevant resources.
2. Read `world_list(category="spell-slots")`; compare every named occupied/empty slot, removal
   verdict, destination, capacity, and holding with the visible spellbook and hotbar.
3. Refuse an unknown runtime UUID; verify no mutation, no native call, and no quarantine.
4. Refuse removal for every naturally protected state the save permits; compare
   `native_remove_refused` with the visible remove control and verify no mutation.
5. Refuse move to the current slot and outside the published range; verify exact evidence and no
   native call.
6. Move one spell to another occupied slot; verify the exact two-position swap in the UI/hotbar
   and returned complete loadout without a read-back call.
7. Move one spell into an empty slot; verify empty identity moves to the source and no spell is
   created or destroyed.
8. Move the same recipe's two distinct runtime instances; verify runtime UUID, not recipe/name,
   chooses the source.
9. Remove one idle removable spell; verify that exact UUID disappears, all survivors retain their
   prior relative order, and the returned loadout exposes every next decision.
10. Compare visible weight, glyph usage, drain, and resource changes as observations only; none is
    an outcome or payment gate.
11. Force no fault. If an organic post-native throw occurs, accept committed only when the exact
    removal/swap outcome is visible; otherwise verify B-004 quarantine until lifecycle replacement.
12. Reload the save and repeat read-only checks to prove bindings, quarantine, and native object
    references were lifecycle-invalidated.

Portable and installed gates prove modeled ordering, complete bindings, response shape, and pinned
metadata/IL. They do not claim this supervised live checklist has passed.
