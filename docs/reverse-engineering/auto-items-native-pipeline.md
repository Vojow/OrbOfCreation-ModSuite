# Auto Items native pipeline

> **Evidence status: static contracts and guarded submission implementation accepted; serialized
> asset and interactive effect-result evidence remain release gates.**

[Back to reverse-engineering index](README.md) | [Auto Items plan](../plans/auto-items.md)

## Audited input

This pass inspected the installed Windows Steam baseline already accepted by
[the reverse-engineering audit](audit.md):

| Assembly | SHA-256 |
|---|---|
| `Assembly-CSharp.dll` | `5845797D40E4631517DE9F4D6296F10C7381AAD5DA733128B2C4685E66E8711F` |
| `Assembly-CSharp-firstpass.dll` | `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A` |

Selected C# was decompiled read-only with ILSpy 10.1.0. No game binary or save was changed.

## Identity and taxonomy

All four product families are `ConsumableSO` objects. The family is serialized membership in the
public `ConsumableSO.consumableTypes` list, not a distinct managed subtype and not a safe inference
from the display name.

The canonical extracted entity inventory identifies these exact `ConsumableTypeSO` definitions:

| Family | UUID | Canonical name |
|---|---|---|
| Fruit | `46e0ab83-df7c-4f35-8012-3d9a3c97b753` | `Fruit` |
| Potion | `8103dae4-6945-4d18-b562-d2ffcd7ef49e` | `Potion` |
| Relic | `5d27b76e-eed3-49cc-a069-b9106000ede4` | `Relic` |
| Scroll | `70b36536-64e5-4f70-ad6f-af5787d719cc` | `ScrollConsumable` |

Other serialized consumable types exist, including Food, PotionAugment, PotionResource,
ThreadConsumable, Treasure, and the global aggregate type. Classification must therefore match the
exact UUID and exact `ConsumableTypeSO` runtime type. An object with no supported family or with more
than one supported family is unknown and cannot be mutated.

The shared publication now carries `consumableTypes` in a separate one-to-many
`WorldConsumableType` relation rather than storing one guessed enum on the scalar row. It preserves
unknown native types so policy can fail closed instead of silently reclassifying them.

## Toxicity is a resource cost

The canonical resource identity is:

| Fact | Value |
|---|---|
| UUID | `4dd4e062-2015-4809-a50f-f37647bda339` |
| Managed type | `ResourceSO` |
| Canonical name | `PotionToxicity` |
| Display name | `Toxicity` |

`ConsumableSO` has two different cost lists:

- `consumeCost` is paid once when a use is accepted;
- `usageCost` is held for the duration of an engaged timed effect and removed when that usage ends.

`CanFire()` requires a positive stock count, no cooldown, `usageCost.HasEnough()`, and
`consumeCost.HasEnough()`. `CollectQuantity()` calls `UseCost()` before accepting each unit, and
`UseCost()` calls `consumeCost.PerformCost()`.

`ResourceCostList.HasEnough()` delegates each row to `ResourceSO.HasAmount()`.
`ResourceCostList.PerformCost()` delegates each row to `ResourceSO.Spend()`. This is the native
admission and mutation path; Auto Items must not recreate it with a direct toxicity write.

### Recovery and rest

`ResourceSO.Spend()` always calls `ResetRest()`. `ResourceSO.Initialize()` creates a singular rest
timer from the serialized `restEngageTime`. When that timer completes, `EngageRest()` marks
`inRestMode` and adds the serialized `restingRateMod` to the resource's rate. A later spend or active
drain resets that state.

The shared world snapshot already publishes the native facts needed to observe this:

- raw quantity and effective capacity;
- net `GetTrueRate()`;
- `inRestMode`;
- `restEngageTime`;
- `restingRateMod`;
- `invertedResource`;
- quality and the other rate inputs.

For an inverted resource, `GetDisplayQuantity()` returns `GetMissing()`, which is
`Max(capacity - raw quantity, 0)`. Toxicity is therefore represented as remaining
internal headroom with a player-facing inverted display: spending headroom raises displayed
toxicity, while positive resource rate restores headroom. The shared world deriver compares the
captured raw quantity with its capacity to identify exact-zero displayed toxicity for the recovery
latch; the live checklist still has to correlate it with the visible meter through recovery and
rest.

The authoritative zero test should use the accepted native representation. The code clamps
near-cap gains to the exact capacity in `Increment()`, so no suite-defined epsilon is justified.

## Submission and completion are separate

The player-facing entry point is:

```text
UI / hotbar
  -> ConsumableSO.SelectAndFire()
     -> CollectQuantity(GlobalVariables.GetMultiBuy())
        -> CanFire()
        -> consumeCost.PerformCost()
     -> queuedQuantity += accepted
     -> Inventory.QueueConsumable(this)
        -> PrepNextUsage() immediately only when the shared queue was empty
```

`PrepNextUsage()` selects the strongest owned `ConsumableCount`, creates a `ConsumableUsage` through
`Fire()`, decrements stock, starts the cooldown, and initializes effect results. Preparation then
advances over later game increments. `StartUsage()` eventually engages the usage, executes instant
effects, applies duration effects, and completes the queue entry.

Consequences for the adapter:

1. `SelectAndFire()` uses the global multi-buy value. A one-item automation action enters the
   already audited `NativeMultiBuyScope` at one under the suite's shared
   `NativeMultiBuyOverride` lease.
2. Acceptance immediately spends the consume cost and increments `queuedQuantity`.
3. Stock decrements immediately only when this item starts preparation; it can remain unchanged
   when another consumable is already ahead in the shared queue.
4. Effect completion cannot be claimed from the return of `SelectAndFire()`.
5. The implementation submits only while `Inventory.CanUseConsumable()` proves the shared
   consumable queue is idle. That
   preserves manual queue ownership, makes stock and usage creation observable in the same
   boundary, and prevents automated work from waiting behind a manual item.

The immediate submission postcondition requires an exact `queuedQuantity` increase
of one, the accepted native consume-cost transition, and—under the proposed idle-only gate—one unit
of stock moved into a non-engaged usage. The eventual effect needs a separate observation rather
than being falsely reported as complete at submission time.

## Native random Scroll targeting

`SetRandomization(true)` sets the persisted `randomized` flag. `IsRandomized()` returns it only when
the serialized `canBeRandomized` capability is true.

During `InitiateUsage()`, the game creates its effect context with
`SetRandom(IsRandomized())`. `RequestTargetEffectScript.Initiate()` then:

1. asks its serialized `TargetSelectOptions` for a random valid target;
2. records that target in `EffectResultInfo`; or
3. cancels the result when no random target exists.

When randomization is off, the same script can open the player's targeting interface instead.
Automation must therefore require `canBeRandomized == true` and a freshly revalidated
`IsRandomized() == true`; it must never submit through the manual-target branch.

The exact target-reference kind authored by Scroll assets and the best postcondition for the chosen
attribute remain serialized/live evidence, not assembly metadata.

## Relic result

Relics share the same `ConsumableSO` submission and preparation pipeline. Their global effect is
authored in serialized effect blocks, so the assembly proves the mechanism but not the exact state
edge each Relic changes. Auto Items therefore verifies native submission, not later durable-effect
completion; the live checklist must identify that later edge before release validation is complete.

## Accepted implementation contracts

The following are strong enough to implement publication, fail-closed planning, and guarded
submission:

- exact four-family `ConsumableTypeSO` UUIDs;
- exact `PotionToxicity` `ResourceSO` UUID;
- `ConsumableSO.consumableTypes`, `consumeCost`, and `usageCost` as the relevant authored edges;
- existing scalar stock, queue, randomization, cooldown, duration, and preparation facts;
- existing shared `WorldResource` recovery and rest facts;
- `CanFire()` as the live native readiness predicate;
- `SelectAndFire()` as the player-equivalent submission path;
- global multi-buy isolation as mandatory for a one-item action.

These contracts authorize only the guarded submission boundary described here. They do not prove
which later random attribute or global Relic effect completed.

## Temporary usage lifecycle

`ConsumableSO.consumableUsages` is the authoritative saved roster. Each `ConsumableUsage` owns a
stable `GetGuid()` UUID plus `en`, `dr`, and `maxDr`:

- `Fire()` creates a new non-engaged usage, adds it to the source consumable, initializes its
  effects, and applies `usageCost` under that usage UUID for duration items.
- `StartUsage()` calls `Engage()`, executes instant effects, applies toggled duration effects, and
  attaches the status effect.
- `Increment()` reduces `dr` only after engagement. Expiry calls `EndUsage()`, expires the attached
  status, removes the usage, and removes its usage cost.
- Save/load persists the usage list. `HydrateUsage()` reapplies the usage cost and, for an engaged
  usage, its duration effect.

The native use path does not impose a general no-stacking rule; `IsUsing()` is a query rather than
an admission guard. Auto Items therefore enforces one pending or active Fruit/Potion usage across
the temporary families. Submission must add exactly one pending usage, a later publication must
observe that usage engaged, and its later disappearance is accepted as expiry only after engagement
was seen.

Serialized effect blocks determine each item's benefit and target. The current policy does not
interpret or rank those graphs: a player must opt in each exact UUID. Runtime safety still proves
the exact Fruit/Potion family, finite positive duration, toxicity-only cost vectors, sufficient
native toxicity headroom, stock, visibility, cooldown, native readiness, queue ownership, and
absence of another temporary usage.

## Implemented boundary

- known entities now include the four exact family UUIDs and `PotionToxicity`;
- the consumable collector publishes every native type membership;
- it publishes both raw resource-cost vectors, distinguished as immediate `Consume` and held
  `Usage`;
- it publishes every usage UUID, owning item, pending/engaged state, and remaining/maximum duration;
- a null, unidentified, or structurally unavailable relation skips the whole item and marks the
  consumable category incomplete;
- installed-game metadata and portable traversal/sorting tests cover the new fields and tables.
- Auto Items is registered in the shared ServiceCycle host, disabled by default, and dispatches at
  most one action per turn;
- Scrolls use native random targeting; Relics receive first priority whenever native `CanFire()`
  and the published toxicity headroom admit them;
- Fruit and Potion family switches default off and require an exact UUID allowlist entry; admitted
  temporary items may use any native toxicity headroom that covers their cost;
- positive toxicity does not itself reserve recovery. Scrolls and admitted temporary items continue
  filling headroom until no otherwise-eligible use fits, which latches recovery until the resource
  returns to its exact native cap (zero displayed toxicity);
- pending or active temporary usages block all further item automation, and a later publication
  must confirm engagement before disappearance can count as clean expiry;
- the action boundary re-resolves identity and family, rechecks visibility, queue idleness,
  `CanFire()`, lifecycle, ownership, and targeting capability;
- one-item multi-buy isolation is restored synchronously; an ambiguous Scroll/Relic attempt
  quarantines Auto Items, while an ambiguous or unconfirmed temporary attempt quarantines only the
  exact item until lifecycle invalidation.

## Open interactive evidence before release

1. Capture the serialized `PotionToxicity` flags and modifiers, then confirm live that displayed
   toxicity is the inverted missing amount, recovery raises internal quantity, spending reaches the
   exact native cap gate, and rest changes the native rate after `restEngageTime`.
2. Capture each supported item's `consumableTypes`, `consumeCost`, `usageCost`,
   `canBeRandomized`, duration, and effect-block topology. Prove that supported costs reference the
   toxicity UUID and record any additional cost.
3. Prove the shared Inventory queue-idle member and transitions.
4. For one Scroll, record random target selection, cancellation when no valid target exists,
   submission state, preparation completion, the selected attribute, and the durable effect state.
5. For one Relic, record admission at both zero and nonzero toxicity when native headroom permits,
   rejection when it does not, preparation completion, and the durable global effect state.
6. Race each live boundary with manual item use and lifecycle replacement.
7. For one allowlisted Fruit and one allowlisted Potion, record pending usage creation, engagement,
   duration countdown, expiry removal, toxicity recovery, and save/load hydration.

Until those observations exist, Auto Items remains disabled by default and is not ready for a
release claim of live effect completion.
