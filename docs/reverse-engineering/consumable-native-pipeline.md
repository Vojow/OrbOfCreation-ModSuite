# Consumable player native pipeline

## Scope and verdict

B-006 covers five player verbs through one lifecycle-scoped consumable GameAction: use, cancel one
pending use, discard owned stock, enable or disable randomization, and move a consumable within the
inventory or hotbar list. Installed metadata and IL prove the pinned build's native mechanisms.
Portable tests prove complete binding, main-thread and lifecycle admission, exact requested-outcome
verification, family quarantine, shared Auto Items ownership, world projection, and MCP shape. Live
Unity/save behavior remains unpromoted until the supervised checklist below passes.

## Audited native mechanisms

| Verb | Player pipeline | Requested identity/outcome gate |
|---|---|---|
| Use | `UIConsumableRefList.ClickConsumable` (`0x0600229B`) -> `ConsumableSO.SelectAndFire` | the exact UUID-resolved consumable's queued count increases by one |
| Cancel | `UIConsumableRefList.CancelConsumable` (`0x0600229D`) -> `ConsumableSO.CancelUsage` (`0x060009D2`) | the selected `ConsumableUsage` UUID leaves the exact item's list, its `EffectResultInfo` is cancelled, and queue count decreases by one |
| Discard | `UIConsumableRefList.DiscardConsumable` (`0x0600229E`) -> `GlobalVariables.GetMultiBuy` -> `ConsumableSO.Discard` (`0x060009D3`) | the exact holding decreases by `min(requested, live amount)` |
| Randomize | `UIConsumableRefItem.TurnRandomizationOn/Off` (`0x06002296/97`) -> `ConsumableSO.SetRandomization` (`0x060009D1`) | the exact consumable reports the requested randomized state |
| Move | `UIConsumableRefList.OnDrop` (`0x0600229A`) -> same-list/index guards -> `SwapPositions` -> `UpdateObservable`; hotbar then applies `SetAt` | the complete same-list UUID sequence is exactly the source/destination swap |

`CancelUsage` obtains `ConsumableUsage.GetResultInfo` (`0x06000DD1`), calls
`EffectResultInfo.Cancel` (`0x06001BFC`), removes that usage, then calls `PrepNextUsage`. The action
therefore verifies cancellation identity, not merely a queue counter. `OnDrop` uses the authored
`ConsumableRefListVariable`; cross-list movement is not a native verb and is refused by this API.

## Boundary order and failure posture

Reflection is confined to lifecycle binding and compiled delegates. The B-006 binding set includes
every reused base contract plus 13 family-specific action contracts; withholding any one makes all
five verbs `contract_unavailable`. Execution then checks Unity main thread, lifecycle epoch, family
quarantine, exact UUID/native type, and verb-specific live predicates. The shared action-family
permit is captured last. Use additionally checks visibility, no pending targeting interaction,
`Inventory.CanUseConsumable`, `ConsumableSO.CanFire`, and enters a fixed-one native multi-buy scope.

Payment, cost delta, stock delta during use, sound, observables, and downstream effect ledgers are
evidence, never success gates. A native throw after the exact requested outcome commits. A throw or
return with a wrong target/order/state quarantines all five B-006 modes until lifecycle replacement.
Preflight refusal never quarantines and never attempts mutation. This is the same posture as B-001;
no second quarantine mechanism exists.

The existing `AutoItemsConsumableUseGameAction` remains the single capability definition. Auto
Items continues to call its automation submission with conservative family, temporary-effect, and
target policy. MCP calls the player submission overload on that same object and applies the game's
ordinary player predicates instead. Ownership covers `ConsumableUse` and
`NativeMultiBuyOverride`; no second planner or direct native mutation path was introduced.

## Pre-decision world and MCP surface

The shared world pass now captures `Inventory._instance`, `allConsumables`, `hotBar`, their ordered
values and maxima, and `Inventory.CanUseConsumable` once on the Unity main thread. Each `consumables`
row publishes player-facing identity, amount and queued amount, per-level holdings, family types,
live immediate and held costs with resource holdings, native affordability/use admission, pending
usages, discard/cancel/randomization decisions, current placements, and every same-list move
destination. Cost values come from `ResourceTuple.GetValue`; affordability comes from the game's
`ResourceCostList.HasEnough`, not a parallel approximation.

`game_consumable` accepts:

- `use` and `cancel` with `consumableUuid`;
- `discard` with `consumableUuid` and positive `amount`;
- `set_randomization` with `consumableUuid` and `enabled`;
- `move` with `consumableUuid`, `list` (`inventory` or `hotbar`), and zero-based `destination`.

Success contains only the newer named target row, the complete newer named inventory and hotbar,
all next decisions, and pending targeting state when use opened one. It has no receipt, payment
stanza, request echo, world generation, or required read-back. Refusal contains its named reason.
Fault retains decomposed before/after action evidence and quarantine state.

## Supervised disposable-save checklist

1. Compare every named inventory/hotbar slot, empty slot, maximum, placement, amount, level bucket,
   pending usage, use cost, held cost, affordability, cooldown, and use verdict with the UI.
2. Use a permanent consumable and confirm queue `+1`; confirm the success post-state needs no read.
3. Use a temporary consumable and confirm its pending usage, duration, and held costs appear.
4. Use a consumable that opens targeting and confirm the named request/candidates return inline.
5. Cancel a pending usage and confirm that exact usage disappears, reports cancelled in game, and
   the next queued usage advances normally.
6. Refuse cancel with no pending usage and confirm no native call or quarantine.
7. Discard one from a multi-item holding, then request more than remains and confirm native clamping.
8. Confirm discard never invents a refund or payment stanza.
9. Enable and disable randomization on a supported consumable; refuse an unsupported one.
10. Move within inventory and hotbar in both directions; compare the complete order and hotbar
    behavior with the UI. Refuse same-position, absent-source, cross-list, and out-of-range cases.
11. Exercise hidden, empty, cooldown, unaffordable, inventory-busy, and targeting-in-progress use
    refusals where the disposable save permits; none may mutate or quarantine.
12. Cross a scene/save lifecycle and confirm prior bindings, native references, and quarantine are
    discarded before the next action.
