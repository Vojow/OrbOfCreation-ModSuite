# Targeting native pipeline

## Scope and verdict

B-005 covers the current native target-request lifecycle: submit one eligible structure, ask the
game to choose and submit a random eligible structure, or cancel the effect result that owns the
request. Installed metadata and IL prove these mechanisms for the pinned game build. Portable
tests prove suite binding, admission, verification, quarantine, and MCP behavior. Live Unity/save
behavior remains unpromoted until the supervised checklist passes.

## Audited mechanism

- `TargetingManager.IsTargeting` (`0x06000770`) and `GetTargetingLink`
  (`0x06000771`) expose the one queue-head request.
- `TargetLink.GetAllTargets` (`0x06003268`) evaluates the authored selection and scaling. In this
  build, `StructureSO` is the only direct `Targeting.ITargetable` implementer.
- specific submit re-resolves a UUID from that exact list and reruns `TargetLink.CheckTarget`
  (`0x0600326B`) immediately before the permit and native call.
- `TargetingManager.SubmitTarget` assigns the supplied object before removing the queue head.
  Success is private `target` (`0x04001A94`) being the same object, `HasTarget` (`0x06003265`)
  being true, and the original link no longer being current.
- `UITargetingInterface.Randomize` calls `TargetLink.GetRandom` and then `SubmitTarget`. Randomize is
  terminal random submission, not candidate shuffling. The transaction starts before RNG, verifies
  the returned exact `StructureSO` with `CheckTarget`, then applies the same submit postcondition.
- `UITargetingInterface.Close` does not call `RemoveRequest`; it closes UI only. Gameplay cancel is
  private link `resultInfo` (`0x04001A96`) followed by `EffectResultInfo.Cancel` (`0x06001BFC`).
  Native Cancel marks the result cancelled and removes its target links. Success is `IsCancelled`
  plus retirement of the original link.

Reflection is confined to lifecycle binding. One missing member makes the capability unavailable;
execution uses compiled delegates. The permit is last after non-mutating identity and native-verdict
checks. A throw after the exact outcome commits. Wrong object, retained request, or uncancelled
result quarantines this family until lifecycle replacement. No payment, counter, RNG, or downstream
effect ledger gates success.

## Read and MCP surface

The `targeting` world category is empty while idle. An active row contains the named request owner,
owner and selection native types, cancel availability, and every eligible `StructureSO` UUID in
native order. MCP enriches candidates from the same world with player-facing name, differing
internal name, committed/effective level, availability, and work-in-flight state. Costs and
affordability are absent because targeting has no payment decision.

`game_targeting` accepts `submit` plus `targetUuid`, `randomize` without a target, or `cancel`
without a target. Success returns the named submitted target when applicable and complete newer
targeting state: the next named ordered request or `pending:false`. It has no receipt, request echo,
payment, counters, or world generation. Refusals give the failed reason; faults retain decomposed
native evidence and quarantine state.

Auto Cast already uses `IsTargeting`, `GetTargetingLink`, `GetRandom`, and `SubmitTarget` inside its
atomic cast action. That policy must resolve every request opened by its own cast, so this generic
action does not replace or change it. The native semantics are symmetric; no second planner exists.

## Supervised disposable-save checklist

1. Before a request, confirm `world_list(category="targeting")` is empty.
2. Open a targeted spell/effect and compare owner and selection type with the UI.
3. Compare every candidate, order, name, committed/effective level, and availability.
4. Submit a non-first candidate and confirm the effect targets that exact structure.
5. Confirm success returns the named structure and next request or `pending:false` without read-back.
6. Open another request, randomize, and confirm the returned named target is visibly used.
7. Exercise a one-candidate random pool and confirm exact identity still verifies.
8. Submit an absent UUID and a catalog-known ineligible structure; both refuse without mutation.
9. Call every mode while idle; each refuses `no_pending_request` without native calls.
10. Cancel a request and confirm its requesting effect remains uncommitted/cancelled.
11. If practical, cancel an effect result owning multiple target links and confirm all retire.
12. Cross a scene/save lifecycle and confirm old bindings/quarantine and native references vanish.
