# Spell output and augment-composition native pipeline

Status: portable implementation complete; installed contracts pass against Orb of Creation
v1.0.5; live mutation promotion remains deliberately pending.

This dossier is the native contract for M1 family B-003 (`V-SPELL-06`). It covers the global
spell-output selector and replacement of one equipped runtime spell's exact augment-glyph stack.
Base-recipe discovery and creation are B-002; removal and ordering are B-004.

## Evidence source and identity

The evidence was read from the pinned assemblies under `artifacts/game-v105`; no game process or
save was opened. A composition target is the non-empty runtime `Spell.guidContainer` UUID, not its
recipe UUID or display name. Augments are exact `GlyphSO.GetGuid()` UUIDs plus positive counts.
Names are diagnostics supplied later by the shared lifecycle identity catalog.

Installed tests pin the v1.0.5 player/output, spell-manager/list, spell identity/reference,
glyph registry/verdict, augment read/write, and stacked-record members used by this family. The
composition-specific tokens are:

| Member | Token |
|---|---:|
| `Player._instance` | `0x0400044B` |
| `Player.maxSpellOutputLevel` | `0x04000420` |
| `Player.GetSpellOutputLevel()` | `0x06000690` |
| `IntVariable.AsInt()` | `0x060015AE` |
| `IntVariable.SetValue(int)` | `0x060015AC` |
| `GlyphSO.All` | `0x040006D9` |
| `GlyphSO.IsAvailable()` | `0x06000BB6` |
| `GlyphSO.IsSpellAugment()` | `0x06000BB8` |
| `GlyphSO.GetMaxUsages()` | `0x06000BCE` |
| `GlyphSO.MeetsNonLvRequirements(List<GlyphSO>, Spell)` | `0x06000C0E` |
| `GlyphSO.GetMasterReqOfList(List<GlyphSO>)` | `0x06000C09` |
| `Spell.GetAugmentGlyphs()` | `0x06001075` |
| `Spell.GetQuantityOfGlyph(GlyphSO)` | `0x06001049` |
| `Spell.GetRecipeMasteryLevel()` | `0x06001047` |
| `Spell.SetAugmentGlyphs(StackedIdRecord<GlyphSO>)` | `0x06000FAC` |
| `AbstractStackedRecord<GlyphSO, IdReference<GlyphSO>>.Set(GlyphSO, int)` | `0x060029E8` |

The manifest contains 14 capture contracts and 14 action contracts for the new touches. Shared
B-002 and identity contracts are reused rather than duplicated. All 26 action dependencies bind
to typed delegates once per lifecycle; any missing member makes the family
`ContractUnavailable`. Execution contains no reflection.

## Native transitions

### Set the global output level

Installed IL proves `Spell.GetOutputLevel()` reads `Player.GetSpellOutputLevel()`, while
`Spell.GetLevel()` derives its result from that output and the base-effect level. The UI's
`UISpellInformation.SetSpellLevel()` calls `Spell.SetLevel()`, but `Spell.SetLevel()` only
recomputes cost; it does not own a separate persistent per-spell level. The truthful mutation is
therefore the one global `IntVariable` selected by `Player.GetSpellOutputLevel()`.

The boundary reads that variable and `Player.maxSpellOutputLevel`, requires the requested integer
in `1..maximum`, rejects an already-current value, acquires the family permit last, then calls
`IntVariable.SetValue(int)`. Success is only the requested value becoming observable in the same
global variable. Derived levels and costs are returned from the newer shared world; they are not
parallel outcome gates.

### Replace one spell's exact augment stack

The boundary re-resolves exactly one equipped concrete `Spell` by runtime UUID. It then resolves
every requested glyph from `GlyphSO.All`, rejects duplicate rows, and revalidates
`IsAvailable()`, `IsSpellAugment()`, and `GetMaxUsages()`. It expands the exact requested counts
for the native combined `MeetsNonLvRequirements()` and `GetMasterReqOfList()` predicates, and
compares the required mastery with `Spell.GetRecipeMasteryLevel()`. These checks all precede the
family mutation permit.

After admission, one `StackedIdRecord<GlyphSO>` is passed to `Spell.SetAugmentGlyphs()`. Installed
IL proves that setter replaces both the UUID-reference stack and resolved-glyph stack, invokes
`SpellRecipeSO.LoadGlyphs`, and then recomputes spell cost. The verified outcome is exact target
identity plus the exact canonical UUID/count stack read back through `GetAugmentGlyphs()` and
`GetQuantityOfGlyph()`. A throw after that outcome is observable remains committed; a missing,
wrong-target, or different-stack outcome quarantines only B-003 for the lifecycle.

Cost, usage, duration/toggle compatibility, and resource balances remain newer-world decision
evidence. They do not replace the requested identity/outcome postcondition and there is no payment
postcondition.

## MCP and world contract

The `spell-recipes` rows are the pre-decision surface. They publish the global current/maximum
output selector and each equipped runtime spell's UUID, slot, recipe, output/effective/mastery
levels, exact applied augments, every named augment option with owned/bonus level, availability,
maximum/current uses and mastery requirement, plus current cast/drain costs, spendable resource
amounts, and affordability.

`game_spell_composition` accepts:

- `mode="set_output_level"` with `outputLevel` only; or
- `mode="set_augments"` with `spellInstanceUuid` and `augmentGlyphs`, each containing
  `glyphUuid` and positive `count`. An empty array clears augments.

It has no world-generation or verbosity argument. The HTTP worker submits one frame operation;
the same `SpellCompositionGameAction` runs on the Unity main thread. A committed response contains
only the newer post-state: global output and, when one spell was targeted, that exact equipped
spell with named options and economics. There is no receipt, payment stanza, counter envelope, or
request echo. Refusals name the failed native predicate. Faults retain before/after identities and
composition evidence. There is no owning automation planner for composition, so planner symmetry
is not applicable.

## Disposable-save promotion checklist

Run only in a later explicitly supervised session:

1. Record the save backup, lifecycle, scene, current/max output, equipped runtime UUIDs, recipe
   names, augment stacks, levels, and cast/drain resource amounts.
2. Read the relevant `spell-recipes` row and compare every equipped UUID/slot, applied augment,
   option holding/max-use, level, and cost with the visible spellbook and Details panels.
3. Request the current output level and verify `already_in_requested_state`, zero mutation, and no
   family quarantine.
4. Request output `0` and `maximum+1`; verify the exact live range in each refusal and no mutation.
5. Set a valid different output; verify every affected visible spell level/cost and the returned
   newer post-state without a read-back call.
6. Refuse an unknown runtime spell UUID; verify no equipped spell or resource state changes.
7. Refuse unknown, non-augment, unavailable, duplicate, over-maximum, incompatible, and
   mastery-blocked glyph requests wherever the save permits; compare reasons with visible UI
   availability and limits.
8. Apply one valid multi-glyph stack; verify exact counts on the requested runtime spell only and
   compare returned derived levels, usage verdict, cast/drain costs, amounts, and affordability.
9. Replace that stack with a different stack, then clear it with `augmentGlyphs=[]`; verify each
   full replacement and absence of stale glyphs.
10. Verify a second instance of the same recipe is not changed, proving runtime-instance targeting.
11. Compare all costs and resource movements as observation only; no floating-point delta is an
    action verdict.
12. Force no fault. If an organic post-setter throw occurs, accept committed only when the exact
    requested state is visible; otherwise verify B-003 quarantines until lifecycle replacement.
13. Reload the save and repeat read-only checks to prove bindings, quarantine, and native object
    references were lifecycle-invalidated.

Portable and installed gates prove modeled ordering, complete bindings, response shape, and pinned
metadata. They do not claim this supervised live checklist has passed.
