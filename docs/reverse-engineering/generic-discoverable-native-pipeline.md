# Generic discoverable native pipeline

## Scope and verdict

B-008 completes `V-DISC-01`: discover one exact, published `AlchemyRecipeSO`, `EquipmentSO`,
`GlyphSO`, `RitualSO`, or `TimeRuneSO` through the native `IDiscoverable` contract. One
`GenericDiscoveryGameAction` serves MCP and any future feature consumer. `SpellRecipeSO` also
implements `IDiscoverable`, but its mutation remains exclusively in
`SpellWorkbenchGameAction`, whose select/discover/create state machine is the owning capability.
`EquipmentSO.Discover` is included here; B-009 extends the returned equipment state for artifact
creation and adds the separate equip/apply verbs without creating a second discovery action.

Installed metadata and IL prove the interface, concrete implementer set, UI ordering, and every
bound member. Portable tests prove main-thread and lifecycle admission, type-exact resolution,
native verdicts, payment-last preflight, outcome verification, quarantine, world projection,
wire shape, names, and contract completeness. Live Unity/save behavior remains unpromoted until
the supervised checklist below passes.

## Audited native route

The pinned game has exactly six `IDiscoverable` implementers:
`AlchemyRecipeSO`, `EquipmentSO`, `GlyphSO`, `RitualSO`, `SpellRecipeSO`, and `TimeRuneSO`.
`UIDiscoverablePage.HandleClick` (`0x0600231C`) supplies the selected object to a
`UICostButton`; its terminal callback invokes `IDiscoverable.Discover` (`0x06001C97`).
`UICostButton.OnClick` performs this exact sequence:

1. `ResourceCostList.HasEnough` (`0x06001E0F`);
2. `ResourceCostList.PerformCost` (`0x06001E19`);
3. the installed callback, which is the selected object's `Discover` implementation.

The interface also owns `GetDiscoverCost` (`0x06001C92`), `IsDiscoverVisible`
(`0x06001C93`), `CanDiscover` (`0x06001C94`), `IsDiscovered` (`0x06001C95`), and
`IsDiscoverRequired` (`0x06001C96`). The concrete `Discover` tokens are
`AlchemyRecipeSO` `0x06000850`, `EquipmentSO` `0x06000B10`, `GlyphSO` `0x06000BFB`,
`RitualSO` `0x06001366`, `SpellRecipeSO` `0x06001432`, and `TimeRuneSO` `0x06001858`.
`EquipmentSO.Discover` immediately calls `EquipmentSO.Create` (`0x06000B11`); that downstream
artifact result is why equipment-specific post-state belongs to B-009.

## Shared GameAction and boundary order

The lifecycle binding set compiles every reflection member before submission. Any missing type,
interface member, cost member, resource member, or concrete implementer produces
`contract_unavailable`; execution performs no reflection or name lookup. Each call checks, in
order:

1. captured Unity main thread;
2. family quarantine and lifecycle epoch;
3. exact UUID plus expected concrete native type through `TypedRegistryResolver`;
4. membership in the five-type generic family and exact `IDiscoverable` assignability;
5. live `IsDiscovered`, `IsDiscoverVisible`, and `CanDiscover` verdicts;
6. exact `GetDiscoverCost` result and native `HasEnough` verdict;
7. the shared generic-discovery mutation permit, last;
8. native `PerformCost`, then the same concrete `Discover` callback used by the UI.

Success gates only identity and requested outcome: the exact resolved object reports
`IsDiscovered() == true`. Cost rows, before/after holdings, and whether native subtraction changed
a representable balance are failure evidence, never a gate. A native throw after the requested
discovered outcome is observable commits. If payment or discovery was attempted but the exact
target does not become discovered, the family is quarantined for the lifecycle. Preflight refusal
does not quarantine. Lifecycle invalidation discards delegates, native references, resolver state,
and quarantine together.

## Pre-decision world and MCP surface

The existing category traversal for all six concrete types reuses one compiled
`WorldDiscoverableBinding`; it does not add another registry scan. Every alchemy-recipe,
equipment, glyph, ritual, spell-recipe, and time-rune row carries a `discover` decision containing
the game-owned visibility, discovered, required, `CanDiscover`, and aggregate affordability
verdicts. Each exact cost line includes a named resource, scientific-string `cost`, canonical
spendable `amount`, and per-line affordability. Thus a strategist can decide without attempting a
mutation.

`game_discover(uuid=...)` accepts a UUID and optional exact native-type assertion. Admission is
limited to the five categories owned by this family. A spell-recipe UUID is rejected as the wrong
capability and directs the caller to `game_spell_workbench`; a discovery-tree UUID remains owned by
`game_discovery_offer`. Success waits for a newer published world and returns that complete named
category row inline, including the next discovery decision. It contains no generation, receipt,
payment stanza, request echo, or follow-up-read requirement. Preflight refusal is compact and
named. A fault after native execution retains the decomposed state, native stage, named costs, and
holdings needed to diagnose partial commitment.

## Native contract delta

B-008 adds 37 manifest rows: 15 capture bindings and 22 action bindings. Capture covers the exact
interface type and six implementers plus the shared discovery/cost/resource readers. The action
set covers the five owned concrete types, interface verdicts and callback, cost entries,
affordability/payment, resource identity, and spendable amount. `SpellRecipeSO` is capture-only in
this family because its mutation bindings remain in the spell-workbench capability. The installed
manifest loop proves every row against the audited assembly, visibility, signature, and token;
focused installed tests pin the implementer set and the two native UI ordering edges.

## Supervised disposable-save checklist

1. List one eligible row from each of alchemy recipes, equipment, glyphs, rituals, and time runes;
   compare display name, visibility, discovered/required state, exact costs, holdings,
   affordability, and blockers with the corresponding UI.
2. List a discovered row and confirm `discover.available` is false without losing its named
   identity or other player-facing state.
3. Attempt a hidden target and verify refusal occurs before payment and does not quarantine.
4. Attempt an already-discovered target and verify refusal occurs before payment.
5. Attempt a visible `CanDiscover == false` target and verify the native-verdict refusal.
6. Attempt an unaffordable target and compare the named cost/holding evidence to the UI; confirm no
   payment or discovery callback occurs.
7. Discover an alchemy recipe and verify the returned full row is newer, named, discovered, and
   immediately usable for its next decision without a read-back.
8. Repeat for a glyph, ritual, and time rune, comparing visible downstream unlocks with the game.
9. Discover one equipment asset; verify the exact asset becomes discovered/created and record the
   equipment-instance/effect facts B-009 must add to post-state.
10. Pass a spell-recipe UUID to `game_discover`; verify capability refusal names
    `game_spell_workbench`, then discover it through that owning action.
11. Exercise a cost against an enormous holding where subtraction is noisy or unrepresentable;
    the correct discovered outcome must commit with accounting confined to evidence.
12. Cross a scene/save lifecycle and confirm bindings, references, publication state, and any
    generic-discovery quarantine are discarded before the next call.
