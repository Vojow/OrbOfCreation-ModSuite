# Auto Buy native purchase pipeline

[Reverse-engineering index](README.md) · [Queue and completion](auto-buy-queue-and-completion.md) · [Native contracts](../testing/native-contracts.md)

## Scope and evidence boundary

This document connects the audited game-member inventory to the suite's
active Auto Buy purchase path. It describes three different kinds of evidence and does not
promote one into another:

- **Static native contract:** exact type/member shape in the audited installed
  assemblies, recorded in [`data/native-contracts.json`](../../data/native-contracts.json).
- **Suite implementation:** ordering and fail-closed behavior in the active
  native adapters under `src/AutoBuy/ServiceCycle/Native/`, which are the only
  Auto Buy code that touches the game. Auto Buy has no capture of its own: the
  facts it decides from arrive on the shared world snapshot published by world
  collection.
- **Runtime behavior:** side effects and callback order observed in Unity. Only
  statements explicitly labelled runtime-observed carry this authority.

The manifest proves that the selected members exist with the expected shape.
It does not by itself prove the internal IL order of resource deduction, queue
insertion, echo actions, UI refresh, or completion effects.

The legacy engine and its incremental catalog have been deleted. The sections
below that describe them are kept as a record of the native surface and of how
it was once driven; they are history, not a description of the running code, and
are written in the past tense for that reason.

The active ServiceCycle service decides from the shared world snapshot:
candidates, availability, level, queued state and price all arrive as published
rows, and the cost chain in particular is now computed by `GameCostMath` rather
than asked for. What Auto Buy calls natively is the action boundary —
the shared owning-view resolver, type-specific availability, `CanPurchase()`,
destination capacity, `Purchase()`, and `ActionManager.GetRemainingRoom()` —
plus a refusal-diagnostics cold path that runs only after `CanPurchase()` has
already said no.

## Audited native surface

| Concern | Structure contract | Upgrade contract | Evidence |
|---|---|---|---|
| Registry | `StructureSO.All` | `UpgradeSO.All` | Static contract |
| Owning UI route | exact `structureType` + `StructureTypeSO.structures`; exact `StructureListVariable.GetAll()` through `ViewSO.relevantLists` / `availableLists` | exact `UpgradeListVariable.GetAll()` through `ViewSO.relevantLists` / `availableLists` | Static contract |
| Owning UI availability | `ViewSO.IsAvailable()` | `ViewSO.IsAvailable()` | Static contract |
| Availability | `IsAvailable()` | `IsAvailable()` | Static contract |
| Native admission | `CanPurchase()` | `CanPurchase()` | Static contract |
| Cost | `GetPurchaseCost()` → `ResourceCostList` | `GetPurchaseCost()` → `ResourceCostList` | Static contract |
| Current level | `GetPurchaseLevel()` | `GetPurchaseLevel()` | Static contract |
| Queued state | `GetQueuedQuantity()` | `GetQueuedPurchaseLevel()` | Static contract |
| Mutation | `Purchase(bool)` | `Purchase()` | Static contract |
| Completion | `CompleteAction()` | `CompleteAction()` | Static contract/Harmony target |
| Queue signal | `QueueBuild(int)` | `Purchase()` | Static contract |
| Finite lifecycle | not used by adapter | `HasFiniteLevels()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` | Static contract |
| Authored list additions | none | `viewListAdditions : List<ViewListVariable.ListTuple>` | Static contract |
| Capacity-bound destination | none | exact tuple `list`/`element`, `maxSizeVariable : IntVariable`, `HasEmptySpot()` | Static contract |

This table records what the installed assemblies offer, not what the suite calls
each cycle. World collection publishes the exact candidate-to-category/list/view
relation and the view's captured availability. The action boundary rebuilds that
same relation from live native objects immediately before payment, then reads the
live owning-view verdict, the structure's own verdict when applicable, native
admission, and any authored destination capacity. Nothing prices through
`GetPurchaseCost()` on the planning path — the suite owns that arithmetic and
publishes `WorldPurchaseCost`.

One cold path adds to that. When `CanPurchase()` refuses, the adapter asks the
game *why*, so a refusal can name a cause instead of being a silent skip:
`IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` and
`GetPurchaseCost().HasEnough()` — the game's own verdict on the price, not a
re-pricing. The same exact cost list is decoded into every resource UUID, cost,
bandwidth flag, and live `GetTrueQuantity()`/`GetMissing()` value. An incomplete
read carries an explicit status rather than a partial list. The manifest carries
these under the owner *Automata Auto Buy refusal diagnostics* at place `action`.
Separate action-place contracts now record the admitting `ViewSO.IsAvailable()`
and `StructureSO.IsAvailable()` calls; their capture-place counterparts remain
because world collection also publishes both kinds of availability.

The shared queue contract is `ActionManager.GetRemainingRoom()` plus
`ActionManager.instance.actionableItems.maxQueuedItems.AsInt()`. Upgrade
single-level isolation additionally uses `GlobalVariables.GetMultiBuy()` and
`IntVariable.AsInt()/SetValue(int)`. Structure fallback grouping may read
`Player.GetBulkDevelopment()`.

## Owning-view reachability chain

The player-facing progression gate lives on the owning `ViewSO`, not on every
item rendered inside it. `ViewSO.prerequisites` determines whether that tab is
available. The authored ownership edge is indirect:

```text
candidate exact UUID + exact native type
    -> exact StructureListVariable / UpgradeListVariable membership
    -> ViewSO.relevantLists or ViewSO.availableLists
    -> exact owning ViewSO
    -> ViewSO.IsAvailable()
```

For a structure the resolver also proves that `StructureSO.structureType` is an
exact `StructureTypeSO`, has a non-empty stable UUID, and contains that exact
structure reference exactly once in its private `structures` list. Both view
list fields participate, while the same view/list route repeated in both is
collapsed. Different matching routes are ambiguous. A missing, unreadable,
ambiguous, or contradictory route is retained in the world as a candidate row
with that named status; the planner excludes it and publishes the corresponding
skip count. It is never an empty gap that can be proposed forever.

The released-0.5.0 Construction Aura incident proves why this is progression
state rather than only an action refusal. Structure
`6a361a01-8405-4fbc-9af1-42f471911d9e` was present in the
`ArtificerStructures` list (`2c3b16bc-…`) and natively purchasable while its
owning `WorkshopArtificer` view (`b8ebce37-…`) was unavailable. The item itself
did not carry the tab prerequisite, so item-only collection could not see the
player-reachability gate.

## `CanPurchase()` truth table

Selected IL from the audited v1.0.5 assembly proves the two methods are not
equivalent admission oracles:

| Native term | `StructureSO.CanPurchase()` (`0x060017A1`) | `UpgradeSO.CanPurchase()` (`0x060018C2`) |
| --- | ---: | ---: |
| per-level requirements | `HasMetLevelRequirements()` | `HasMetQueuedLevelRequirements()` |
| max queued level | no | rejects `IsMaxQueuedLevel()` |
| affordability | no | `GetPurchaseCost().HasEnough()` |
| candidate `IsAvailable()` | no | yes |
| owning `ViewSO.IsAvailable()` | no | no |
| action queue admission | `ActionManager.CanLoadAction(this)` | `ActionManager.CanLoadAction(this)` |

For structures in particular, `CanPurchase()` means only per-level requirements
and action-load admission. It does not imply the structure is available and it
does not imply the player can pay. The boundary therefore treats the owning-view
and structure-availability reads as independent mandatory gates, not as
diagnostic restatements of `CanPurchase()`.

## What the deleted incremental catalog established

The pre-collection pipeline enumerated both registries itself, sliced the walk, and admitted a
candidate only on a complete contract. Its slicing did not survive — a shared collection pass
replaced per-feature enumeration — but three of its rules did, and they are why the current shape
looks the way it does.

- **Identity is the stable UUID plus the exact audited native type.** A UUID/type contradiction is
  invalid, and a same-UUID native-reference replacement advances the lifecycle epoch rather than
  being treated as the same object.
- **Registry presence is never availability or completion.** Locked content stays visible and can
  become active after progression, so the two questions are asked separately.
- **A partial cost vector never authorizes a purchase.** World collection resolves cost rows the
  same way: a candidate whose vector cannot be fully resolved is skipped rather than guessed at.

The earliest real Unity point at which every registry is complete remains a runtime contract, which
is why lifecycle transitions still invalidate and recollect rather than assuming one permanent
startup snapshot.

## Immediate pre-mutation validation

Every action the worker planned is revalidated on the Unity main thread before it
is allowed to mutate:

1. read live queue room through `ActionManager.GetRemainingRoom()` and subtract
   the configured reserve; the worker does not bound its plan by the queue at all,
   so this read is the only queue authority;
2. resolve the candidate by stable UUID and exact native type;
3. rebuild the exact owning category/list/view relation with the shared audited
   resolver, then require live `ViewSO.IsAvailable()`;
4. for a structure, require live `StructureSO.IsAvailable()` independently of
   native admission;
5. call the type-specific `CanPurchase()` described in the truth table above;
6. for an upgrade, traverse every exact `viewListAdditions` tuple. If its target
   list has a `maxSizeVariable`, validate the audited list/variable identities and
   require live `HasEmptySpot()`;
7. detach the live refusal-cost evidence and enter the existing mutation verifier.

The view and structure availability reads are deliberately repeated at the
admitting boundary even though planning used published availability. These are
mutable native progression facts, and `StructureSO.CanPurchase()` does not cover
either of them. Each additional level of a grouped structure purchase repeats
the owning-view relation, both availability gates, and native admission before
the next `Purchase(true)` call.

The same is now true of the per-level prerequisites, which used to be the one
admitting term the snapshot could not carry, because the game's own answer takes
the level as an argument. The conditions are published as rows and the worker
evaluates them for the level a purchase would reach — `level + queuedLevels + 1`
for an upgrade, `quantity` for a structure — so a candidate whose next level is
gated never reaches the boundary at all. A condition the suite cannot evaluate
counts as gated. This is what stopped Auto Buy planning `ScribeScroll4` against
an unfinished `ImprovedScribing`. See W58.

## Refusal diagnosis

All owning-view relation failures, a locked owning view, a structure whose own
availability dropped, and a full or untrusted destination are named pre-payment
refusals. They do not enter the `CanPurchase()` diagnostic path because their
contracts are independent of that bool.

When `CanPurchase()` itself refuses, a sole `HasEnough()` failure is expected
snapshot staleness for upgrades: resource quantities moved after collection
through drain or queue-time spending. The action records every live row,
same-batch resource overlap, collection-to-admission time, and world-generation
delta, then returns a pre-native skip. Common holds the service behind its
world-freshness gate until a later collection; configuration is untouched.

An availability or level-cap contradiction is structural. If every readable
term passes, the parameterized per-level prerequisite is the remaining term by
elimination. Those cases remain invariant violations: they terminate the batch
and stand Auto Buy down after writing the full diagnostic. None of these cold
reads happen on an admitted purchase.

## Destination-capacity admission

An aspect acquisition is an ordinary `UpgradeSO`. Its authored
`viewListAdditions` contains `ViewListVariable.ListTuple` values whose inherited
`list` and `element` fields cause `UpgradeSO.ApplyListAdditions()` to add a view.
The world-aspect destination is `CreatedWorldAspects`
(`74ec1f90-e94c-4cd7-a1d0-7b35016b57ff`), whose
`AbstractListVariable<ViewSO>.maxSizeVariable` must be the exact
`WorldAspectSlots` `IntVariable`
(`4b1bb2de-723a-4360-827c-8e4483f3ff8d`). Neither `CanPurchase()` nor
`Purchase()` inspects this destination before applying the tuple.

The boundary is generic over the authored tuple list, not over aspect upgrade
names. Every exact tuple is validated. A tuple targeting an unbounded list needs
no capacity gate. A capacity-bound tuple must match an audited exact list/max
identity pair and its live list must answer `HasEmptySpot() == true`; an unknown
pair, malformed tuple, identity contradiction, or full destination refuses
before payment. The currently audited capacity profile contains the world-aspect
pair above.

**Maintainer live observation (released 0.5.0):** purchasing an aspect with zero
free slots invented an extra slot in the UI. Buying the later slot upgrade then
added another slot which stayed permanently empty. This supersedes the earlier
IL-only expectation that the list addition might silently no-op. Three aspect
upgrades exist; the observed one was likely the crafting/Workshop aspect
(`d9f1a5c3-…`), with Alchemy Lab (`2f37d7a7-…`) and Rituals
(`38f53d08-…`) as the other candidates, but the exact overflowing upgrade remains
authored-data/live territory. The suite's contract is narrower and firm: it must
never create the full-destination state, whichever upgrade authored the tuple.

## Mutation transaction

### Structure

1. Capture `GetQueuedQuantity()`.
2. Invoke `StructureSO.Purchase(true)`, once per requested level, re-reading
   the owning-view relation, both live availability gates, and `CanPurchase()`
   before each level past the first.
3. Capture `GetQueuedQuantity()` again.
4. Accept an exact delta of `+1` for a single-level request, and a delta in
   `[1, count]` for a group.

`Purchase(true)` forces exactly one level and consults no multiplier, so a bulk
structure buy is the same call repeated inside one verifier scope — which is what
the Bulk Development grouping mode asks for. A group that stops early because a
gate changed or `Purchase(true)` committed no further affordable level is a
partial success once at least one level committed, not a refusal. The Boolean argument
shape and exact queued-state methods are statically verified. The meaning of the
`true` argument and the internal native order of resource spending versus
`QueueBuild` are not asserted here without a reviewed IL/runtime observation.

### Upgrade

1. Resolve and read the global multi-buy variable.
2. Set it to the requested level count and verify the readback.
3. Capture `GetQueuedPurchaseLevel()`.
4. Invoke `UpgradeSO.Purchase()`.
5. Capture `GetQueuedPurchaseLevel()` again.
6. Accept a delta in `[1, count]`. `Purchase()` honours the multiplier but the
   game may afford fewer levels than asked for, so any committed level is a
   success and only a zero delta is a miss.
7. Restore the original global multi-buy value and verify restoration on every
   exit path.

If multi-buy entry or restoration cannot be verified, no mutation is attempted
and Upgrade purchasing is quarantined. Structure purchasing is independent of
that global quarantine.

## Mutation outcomes

`NativeMutationVerifier` distinguishes the observable boundary, not the game's
internal intent:

| Outcome | Native invocation known to have started? | Safe interpretation |
|---|---:|---|
| Before capture failed | No | No mutation authority was obtained |
| Execution threw | Yes | Ambiguous even when an after-state can be read |
| After capture failed | Yes | Ambiguous |
| Postcondition failed | No exception, but no queued delta | Benign skip |
| Verified | Yes | The expected queued delta was observed |

A call that threw is ambiguous and blocks that candidate until a newer lifecycle.
A call that completed cleanly and simply moved nothing is a benign skip, not a
fault: the batch advances to its next action. Reserving the fault classification
for real exceptions is deliberate — around a fifth of attempts in observed play
are zero-delta misses, and treating each as a fault discarded the rest of the
batch.

A definite structural rejection before any native call terminates the batch.
A price-only admission refusal is instead a pre-native skip and requires a fresh
world before re-planning. An ambiguous mutation remains blocked until lifecycle
recovery.

## What remains unknown

- exact native IL order of queue insertion, resource deduction, notifications,
  and `QueueBuild`/`Purchase` callbacks;
- which native failures can throw after a partial side effect in the audited
  game build;
- whether all completion/echo paths produce the same Harmony callback order;
- whether queue capacity can change outside the currently observed progression
  paths;
- exact availability/registry populations at named player progression stages;
- which exact authored aspect upgrade produced the observed overflowing-slot
  incident.

These unknowns require an installed-assembly IL audit or sanitized runtime
observation before they can strengthen simulation authority.
