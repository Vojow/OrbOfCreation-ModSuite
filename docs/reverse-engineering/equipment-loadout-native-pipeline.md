# Equipment creation and loadout native pipeline

This dossier is the native boundary for `V-ART-01`, `V-EQ-01`, and `V-EQ-02` on the audited
v1.0.5 assembly. Artifact creation uses the already-shared generic discovery transaction;
loadout changes use one `EquipmentLoadoutGameAction` for features, MCP, and tests.

## Native entry points and identity

`EquipmentSO.Discover` (`0x06000B10`) calls `EquipmentSO.Create` (`0x06000B11`). Creation sets the
asset's `isCreated` state and emits the game's observables, audio, and popup. It does not prove or
promise an automatic equip. `game_discover` therefore owns artifact creation and returns the newer
equipment row; B-009 does not duplicate payment or discovery.

The loadout UI reaches `EquipmentManager.ToggleItem(Guid/EquipmentSO)` (`0x06000514-15`) and then
the exact object overloads `EquipItem(EquipmentSO)` (`0x06000517`) or
`UnEquipItem(EquipmentSO)` (`0x06000519`). The GameAction resolves the submitted UUID as exactly
`EquipmentSO` through the shared typed registry and invokes those object overloads. Names are
diagnostics; UUID plus expected type remains identity.

## Equip transition

The installed IL proves this order inside `EquipItem(EquipmentSO)`:

1. `equipment.GetUsageCost().HasEnough()`;
2. when the target is absent, reject a globally full `equippedEquipment` list;
3. compute remaining target stacks as `GetMaxLevel() - GetStacks(target)`;
4. compute affordable stacks as `Floor(GetUsageCost().MaximumCostTimes()).ToInt()`;
5. clamp amount by `GlobalVariables.GetMultiBuy()`, remaining stacks, and affordable stacks;
6. `equippedEquipment.Stack(target, amount)`;
7. refresh the native stack observer;
8. `equipment.Equip(equippedEquipment.GetStacks(target))`;
9. play native audio.

`UIEquipmentItem.CanEquip` (`0x060023C3`) owns one additional player-visible admission rule that
the manager does not: for a new target, `HasTypeRoom` (`0x060023C4`) requires both a global slot and
`GetTypesEquipped(primaryType) < primaryType.GetMaxTypeSlots()`. The action revalidates that exact
guard immediately before its mutation permit; omitting it would permit an MCP-only state the UI
refuses.

`EquipmentSO.Equip(int)` (`0x06000B12`) applies the resulting total stack level. Positive levels
reserve `GetUsageCost() * equippedLevel`, apply effects, and start or quick-complete attunement.
Zero removes the UUID-keyed usage reservation and effects and clears attunement. Usage is a standing
reservation, not a purchase ledger.

## Unequip transition

`UnEquipItem(EquipmentSO)` checks `IsEquipped`, clamps the removal by live multi-buy and current
stacks, un-stacks the exact target, refreshes the observer, calls `EquipmentSO.Equip(remaining)`, and
plays native audio. It does not accept an arbitrary amount; `game_equipment` deliberately models one
player click and therefore has no amount parameter.

## Verification and quarantine

The requested transition is the exact target's stack increase or decrease by the amount computed
from the same live native facts. That identity/outcome is the only success gate. Resource usage,
effects, attunement, global/type counts, and list observations remain failure evidence and published
post-state; they never become payment-delta gates. A throw after the exact stack outcome commits.
A missing or wrong stack transition faults and quarantines this family for the lifecycle, matching
B-001. Lifecycle invalidation drops bindings, registry resolutions, and quarantine state.

## MCP surface

The `equipment` world row is the pre-decision surface. It includes creation state, exact current and
maximum stacks, global and primary-type slots, live multi-buy, named usage-cost resources with
current holdings, and explicit `equip`/`unequip` availability plus the next native-clamped amount.
`game_equipment(uuid, mode=equip|unequip)` returns no receipt or payment stanza on success: it waits
for a newer immutable world and returns that complete named row, which contains the next decision.
Failures before native work carry the exact refusal; faults retain decomposed before/after evidence.

## Disposable-save promotion checklist

1. Compare one uncreated artifact row with the Create screen, then create it through `game_discover`.
2. Confirm the returned equipment row becomes created without claiming it was auto-equipped.
3. Compare usage costs, current holdings, maximum stacks, multi-buy, and both slot counts to the UI.
4. Equip a new artifact and verify the returned stack, slot counts, attunement, and next decisions.
5. Equip the same artifact again with multi-buy above one and verify the native clamp.
6. Unequip some stacks, then the final stack; verify type/global slots reopen in returned post-state.
7. Exercise maximum-stack, global-full, type-full, uncreated, unaffordable, and not-equipped refusals.
8. Cross a scene/save lifecycle and prove a fresh typed resolution and binding set is used.

No game or save was touched while producing this dossier; the checklist is the live promotion gate.
