# Spell-manager discovery and creation native pipeline

Status: portable implementation complete; installed contracts pass against Orb of Creation
v1.0.5; live mutation promotion remains deliberately pending.

This dossier is the native contract for M1 family B-002 (`V-SPELL-01` through
`V-SPELL-03`). It covers selecting one authored base recipe, discovering that exact
recipe, and creating another runtime spell instance from it. Augment and output-level
composition is B-003; loadout removal and reordering are B-004.

## Evidence source and identity

The evidence was read from the pinned installed assemblies under
`artifacts/game-v105`; no game process or save was opened. Identities are always the
canonical `SpellRecipeSO.GetGuid()` UUID plus exact native type. A player-facing name is
diagnostic and comes from the shared lifecycle identity catalog.

The installed-contract tests pin these v1.0.5 metadata tokens:

| Member | Token |
|---|---:|
| `SpellManager.selectedCoreGlyphs` | `0x04000499` |
| `SpellManager.selectedAugmentGlyphs` | `0x0400049A` |
| `SpellManager.activeSpells` | `0x0400049C` |
| `SpellManager.instance` | `0x040004AD` |
| `SpellRecipeSO.All` | `0x04000A32` |
| `Spell.guidContainer` | `0x040007DF` |
| `SpellManager.CreateSpell()` | `0x0600073F` |
| `SpellManager.DiscoverSpell()` | `0x06000741` |
| `SpellManager.GetSpellFromRecipe(List<GlyphSO>)` | `0x06000747` |
| `SpellManager.GetSpellCreateCost(List<GlyphSO>)` | `0x0600074A` |
| `SpellRecipeSO.GetDiscoverCost()` | `0x06001442` |
| `SpellRecipeSO.GetGlyphRecipe()` | `0x06001447` |
| `SpellRecipeSO.IsCreatable()` | `0x0600144F` |
| `SpellRecipeSO.CanDiscover()` | `0x06001451` |
| `GlyphSO.IsAvailable()` | `0x06000BB6` |
| `GlyphSO.IsSpellAugment()` | `0x06000BB8` |
| `GenericListVariable<T>.Empty()` | `0x06001569` |
| `EmptyTypeListVariable<T>.HasEmptySpot()` | `0x0600155C` |

The manifest additionally binds the exact list value/add operations, resource-cost
predicate, spell-reference getter, `GuidContainer.get_guid()`, and the capture-side cost,
quantity, selection, and loadout members. The action builds its complete delegate set once
per lifecycle. One missing member makes the whole family `ContractUnavailable`; execution
does no reflection.

## Native transitions

### Select an authored base recipe

There is no one native method that represents the whole UI selection gesture. The action
therefore uses the same audited native list operations the UI drives:

1. resolve exactly one `SpellRecipeSO` from `SpellRecipeSO.All` by UUID;
2. read its ordered `GetGlyphRecipe()` core sequence;
3. require every entry to be an exact, available, non-augment `GlyphSO`;
4. acquire the family mutation permit;
5. empty native core and augment selection lists;
6. append the authored core sequence in order;
7. call `SpellManager.GetSpellFromRecipe()` and require the result to be the requested
   UUID, with zero selected augments.

The verified outcome is selection identity: the live ordered core sequence resolves to the
requested recipe. List call counts and intermediate cleanup are failure evidence, not extra
outcome gates. B-002 intentionally accepts a stable recipe handle instead of a caller-authored
glyph array; this prevents a second recipe resolver and still reaches every authored base
recipe. B-003 owns augment composition.

### Discover the selected recipe

IL proves `SpellManager.DiscoverSpell()` resolves `selectedCoreGlyphs` through
`GetSpellFromRecipe`, calls `SpellRecipeSO.Discover()`, and only then calls
`ResourceCostList.PerformCost()`. The game therefore owns a discover-before-payment partial
commit possibility. The action does not reorder or reimplement it.

Before the call, the boundary re-resolves the exact selected recipe and requires no augments,
`!IsDiscovered()`, `CanDiscover()`, `IsCreatable()`, and
`GetDiscoverCost().HasEnough()`. Payment remains native. The sole success gate is that the
requested recipe is now discovered. Payment, selection cleanup, and the optional
`PostDiscoverRecipe` auto-equip are observations exposed by the next world publication; they
cannot fault or quarantine a correct discovery outcome.

### Create another runtime spell instance

`SpellManager.CreateSpell()` resolves the current core selection and delegates to
`CreateRecipe`. Installed IL proves `CreateRecipe` consumes the selected augments, calls
`SpellRecipeSO.CreateWith(...)`, adds the result through `SpellManager.AddSpell`, and then
clears selection. The UI's creation button uses the same
`GetSpellCreateCost(...).HasEnough()` verdict.

The boundary requires the exact base selection, an already discovered and creatable recipe,
`activeSpells.HasEmptySpot()`, and the current exact create-cost predicate. Success means a
new, non-empty runtime `Spell.guidContainer` UUID referencing the requested recipe appeared
in the live loadout. A count increase, an empty UUID, or a new instance of another recipe is
not success.

## MCP and world contract

`world_list`/`world_get` category `spell-recipes` is the pre-decision surface. The same row
shape names the recipe and ordered core glyphs, includes owned/bonus glyph levels, current
selection, equipped slots, loadout count/capacity, and the relevant discovery or creation
cost with current spendable resource amount. Costs remain visible before selection. The
`select`, `discover`, or `create` object identifies the next callable mode, availability, and
stable refusal reason.

`game_spell_workbench` accepts `mode` (`select`, `discover`, or `create`) and
`spellRecipeUuid`. It has no world-generation or verbosity argument. It executes on the Unity
main thread through the one `SpellWorkbenchGameAction`. A committed response contains the
newer named `spell-recipes` row only: no payment stanza, native receipt, counters, or request
echo. A preflight refusal names its exact failed native predicate; a fault after evidence
capture retains before/after selection, discovery, and instance identity evidence. Only a
wrong/missing requested outcome quarantines this family for the lifecycle.

There is no owning automation planner for spell discovery or creation, so planner symmetry is
not applicable. Existing cast and mastery automation continue to consume their own canonical
actions and the same published spell identities.

## Disposable-save promotion checklist

Run only in a later explicitly supervised session:

1. Record the save backup, lifecycle generation, scene, recipe UUID/name, selected glyphs,
   discovered flag, loadout slots, and both relevant resource amounts.
2. Read the recipe through both `world_list` and `world_get`; verify identical named row fields.
3. Compare core glyph order and owned levels with the visible spell-discovery UI.
4. Compare discovery cost, creation cost, affordability, and loadout capacity with the UI.
5. Call `select`; verify the UI selects the exact authored core sequence and clears augments.
6. Refuse `discover` for a different recipe UUID and verify no resource, discovery, selection,
   or loadout mutation.
7. Exercise one native `CanDiscover`/`IsCreatable` or unaffordable refusal if the save permits;
   compare the reason with visible UI availability.
8. Call `discover`; verify the exact recipe becomes discovered and the compact response reports
   the next decision without a read-back.
9. Compare the native discovery charge as observation only; do not use a floating-point delta as
   the action verdict.
10. Record whether native `PostDiscoverRecipe` auto-equipped an instance and verify the returned
    equipped slots match the UI.
11. If no instance was auto-equipped, call `select` again, then `create`; verify one new non-empty
    runtime instance of the exact recipe and the returned loadout state.
12. Exercise full-loadout and unaffordable-create refusals where practical; verify zero new
    instance and no family quarantine.
13. Force no fault. If an organic post-outcome throw occurs, verify the committed result follows
    the observable requested outcome; otherwise verify a missing/wrong outcome quarantines only
    B-002 until the lifecycle changes.
14. Reload the save and repeat the read-only checks to prove lifecycle invalidation rebuilt all
    bindings and retained no native object reference.

Portable and installed gates prove the modeled ordering, complete binding set, response shape,
and native metadata. They do not claim that the supervised live checklist has passed.
