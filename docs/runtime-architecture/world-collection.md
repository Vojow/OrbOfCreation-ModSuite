# Shared world collection

> **Lifecycle: Accepted.** One registered service reads the whole game once per interval and
> publishes an immutable snapshot every other service consumes.

[Back to the dossier](README.md) · [How a service gets its data](service-data-flow.md) ·
[Migration decisions](world-collection-decisions.md)

This document describes the world publication specifically. The rule it serves — a service receives
configuration, world state and strategy and nothing else — is stated in
[service-data-flow.md](service-data-flow.md), which is the authority if the two ever disagree.

## Why it exists

Before this, every service read the game for itself. Two services wanting the same number read it
twice, on the same thread, at different moments — so they could disagree about the world while
believing they agreed. Shared collection replaces that with one reader and one snapshot: services
consume a value that was true at a single instant, and the cost of reading is paid once.

That per-service reading was the *capture* phase, and for ordinary services it is gone: the ordinary
contract has no capture member at all. See [service-data-flow.md](service-data-flow.md).

## The pipeline

```
StructureSO.All, UpgradeSO.All, … ──each interval──► GameWorldCollector ──samples──┐
                                                                                  │
IdScriptableObject.RuntimeLookup ──first stable Playing capture──► identity snapshot
                                                                                  │ exact reference
                                                                                  ▼
                                                                        GameWorldCycleFrame
                                                                                  │ worker thread
                                                                                  ▼
GameWorldFrameDeriver ──► GameWorldState ──► one publish action
        │  back on the Unity thread, dispatched before any service starts
        ▼
ServiceCycleRegistry's ServiceWorldPublisher
        │  pinned once per cycle by the runtime, at cycle start
        ▼
ServiceCaptureContext.World  and  IServiceCycleWorkerDefinition.Evaluate(…, world, …)
```

Each stage exists for one reason:

- **Collector** compiles one accessor per member per category against the loaded assembly, and
  answers each modifier record the way the game's own accessor would without calling it — see the
  reading rules below. It is the only stage that touches a native object.
- **Frame** carries raw samples across the thread boundary. It cannot be the collector, because a
  service frame is structurally forbidden from storing delegates and the collector is almost
  entirely delegates.
- **Identity catalog** validates and copies the live runtime registry once per lifecycle on the
  Unity thread. The frame and derived world carry that exact immutable snapshot reference; later
  250-millisecond captures neither enumerate it again nor copy its rows into category tables.
- **Deriver** turns samples into rows — the ported half of the game's own math, including the full
  resource rate chain. It runs on the worker and holds no native surface to reach for.
- **Publish action** carries the derived snapshot back to the Unity thread. Publishing is an action
  rather than something the worker does directly so that the live world changes at exactly one point
  in the pump, on one thread, before any service decides anything that frame. See
  [W4](world-collection-decisions.md).
- **Publisher** swaps the reference under the generation the snapshot was *collected* at. Latest
  wins; nothing published is ever written again.
- **The pin** is the runtime's, not the consumer's. `ServiceCycleStartCoordinator` reads the
  publication once when it opens a cycle and hands that snapshot to both halves — the main-thread
  capture through its context, the worker through its frame projection. A service holds no publisher,
  so it cannot evaluate against one snapshot and act against another. See
  [W50](world-collection-decisions.md).

## Reading rules

**Reproduce the accessor, never call it.** `ValueModifierRecord.GetValue()` calls `Calculate()` when
dirty, which writes `calculatedValue`, clears `calculationDirty`, and re-stamps observables —
mutating game state on the suite's schedule. Every modifier is therefore read as `GetValue()` would
answer it and never through `GetValue()`: the memo when the record is clean, `Adjust(baseValue)` over
both modifier sets when it is dirty. Neither half is the rule on its own. Taking the memo raw
published the `[NonSerialized]` zero of a record nothing had touched since load; recomputing
unconditionally published a number for records the game will never recompute — a record with no
modifiers is never dirtied, so its memo is what the game charges from for the whole session, and
"correcting" `passiveCostMod` from 0 to 100 over-priced every structure by 1.25 to the power of its
owned levels. The number to publish is the number the game will act on.

**Enumerate the runtime object, not the save record.** A save record describes what was written to
disk; the runtime `ScriptableObject` describes what the game is currently playing.

**A generation is a frame number, and it is the frame the game was read on.** Not the frame the
snapshot finished deriving on — those differ, and only the first one lets a consumer ask "has the
world been re-read since I last changed it" and get the right answer. See
[W18](world-collection-decisions.md).

**A missing member degrades one category, not the pass.** Binding failures are reported per category
in `WorldCollectionReport` with the member that could not be found. A build that renamed one field
still publishes the other forty.

### Authored facts are lifecycle-structural

Seven readers describe authoring rather than played state: plot authoring, effect blocks, entity
requirement graphs, crafting recipe types, crafting recipe authored edges, structure costs, and
upgrade costs. `GameWorldCollector` traverses those readers only once for a lifecycle epoch and
retains their raw buffers and category reports. It still derives immutable output tables on the
worker for every publication; only repeated Unity/native traversal is skipped.

Their paired live facts remain ordinary collection: prerequisite-link and native requirement
verdicts, crafting visibility/purchase/capacity/drain verdicts, resources, active modifier inputs,
and affordability refresh on the 250-millisecond cadence. Lifecycle replacement invalidates both
the retained authoring and the compiled native references. The field-by-field ownership table is in
[Game MCP frame operations](game-mcp-frame-operations.md#data-lifetime-and-owner-inventory), because
that audit is what exposed the accidental repeated traversal.

The live entity-name catalog follows an even narrower lifecycle contract: it binds at the first
stable Playing capture after `RuntimeReady`, then reuses one UUID-sorted snapshot until lifecycle
replacement. It is attached metadata, not a fourth ServiceCycle publication and not a 250-ms reader.
Its registry and fallback rules are normative in the
[game boundary doctrine](game-boundary-doctrine.md#live-entity-identity-catalog).

## What is deliberately not collected

An immutable publication may not carry a list, and wrapping each list in an audited table is a
per-category design decision rather than a mechanical one. So the scalar half of these categories is
collected and the list half is not:

| Category | Not carried |
| --- | --- |
| Ritual | `ritualInstances`, `currentSpoils` |
| Plot node | `sizeMods` |
| Discovery tree | the discovered-identity list |

This is stated rather than silently omitted because a consumer that assumed a consumable row
described the consumable completely would be wrong about exactly the part that says how many are in
stock.

Three cases that look like they belong on that list do not:

- **A consumable's stock count.** The save record stores a `consumableCounts` list and derives the
  total on load; the runtime keeps that total in a plain cached int, and `Quantity` carries it.
- **A plot node's phase quantities.** `IdleQuantity` and `TotalQuantity` are summed out of
  `phaseInstances` during collection and `RemainingQuantity` is derived from them on the worker, so
  the numbers travel while the instances do not. The game's own `GetQuantity()` and
  `GetTotalQuantity()` are *not* how they are read: both reach the list through `GetPhaseInstance()`,
  which lazily caches and creates a missing instance, and collection does not write.
- **A plot's action instances.** They travel, one row each in `PlotActionInstances`, keyed by the pair
  and by the instance's position in the plot's own list — that position is the plot's, not a queue's.
  The pair table's count of them stayed where it was; the rows joined it rather than replacing it.
- **A crafting recipe's authored edges and evaluated blockers.** `CraftingRecipes` is one entity row
  per concrete `CraftingRecipeSO`, with immutable nested tables for its crafting types, authored
  resource inputs, generated resource outputs, consumable completion outputs, and engagement-drain
  blocks. The Unity-thread reader invokes the native visibility, starting-quantity purchase,
  generated-output capacity, and necessary-drain evaluators through lifecycle-compiled bindings.
  It copies only their values. The worker then enriches each resource edge from the already-derived
  `Resources` table in the same frame; it never follows a retained native reference. These verdicts
  remain separate because none implies the others.

**The live action queue** is collected — a queue is a list variable carrying its own uuid — and what
is deliberately absent is not the reading but the *authority*. A collected queue reading may shape a
plan and may never admit an action into a slot, because both services consume the slots they are
competing for. See [W53](world-collection-decisions.md).

## Acting twice on one world

A service that attempts a game-facing action must not decide again until the world has been re-read
since. A commit is absent from the pinned reading. A skip, rejection, or fault is just as important:
the live action boundary has shown that the facts which produced the plan were insufficient or
divergent. Planning another action from those same facts means trusting a snapshot just proven
unreliable.

The first world is the same problem in different clothes. At activation the published snapshot is the
seed one — an empty world, or a real collection whose prices the game has not finished cooking — and a
service deciding against it decides that nothing costs anything. So the gate is armed at birth too:
`ServiceCycleSlot.ArmWorldGate` raises the floor to the generation that was live when the runner became
current, at activation and again on lifecycle replacement.

This is enforced by the runtime, not by the services, and there is nothing to declare. The gate is
unconditional: every ordinary service is subject to it and none opts in.
`ServiceCycleSlot.RecordWorldInvalidation` raises the floor after every attempted game-facing
dispatch, whatever its terminal disposition, and `TryStartCycle` refuses to open the next cycle until
the published world generation is strictly newer than the floor. Because both numbers are pump frames,
the comparison is a plain `>`. The floor only rises, so re-arming never forgives an earlier attempt.

A `Source` is exempt by shape, so collection never waits on itself. That is the only exemption:
committed, skipped, rejected, and faulted ordinary attempts all close the gate. Strictly-after is
deliberate — within a frame, actions dispatch before captures, so a snapshot stamped with our own
action's frame can contain it, but waiting one more collection is cheap next to acting again on
unreliable facts. A world source that cannot answer holds the service closed, because "unknown" is
not "fresh".

Nothing about this lives in a feature. `AutoBuyService.ShouldStart` answers only "is Auto Buy
configured to run"; freshness is not its question to answer. Only the generation crosses the seam
(`IServiceWorldGenerationSource`), so this stays a scheduling rule rather than a second way to read
the world — the runtime still pins a cycle's snapshot exactly once, when the cycle opens. Every held
frame is recorded as a `ServiceWorldGateDeferralFact`, because a held service is otherwise
indistinguishable from a service with nothing to do.

This deliberately couples every consumer to collection's health: if collection stalls, consumers that
have already acted stop acting rather than working from a world that is falling behind. Emergency
stop freezes the generation for the same reason and with the same effect.

## Consumers

**Auto Buy** asks the game nothing while deciding. Its candidates *are* the snapshot's structures
and upgrades; identity, availability, current and queued levels, prices, resource quantities, the
economic priority a candidate's authored effects earn it, and the multi-buy and bulk-development
counts all come from published rows. The snapshot also publishes each candidate's exact
category/list/owning-view relation from both `ViewSO.relevantLists` and `availableLists`, including a
named fail-closed row when that relation is missing, unreadable, ambiguous, or contradictory. The
planner therefore excludes content behind an unavailable owning view instead of repeatedly proposing
work that only the boundary can refuse. The type-specific `CanPurchase()` fold and the queue's
capacity and remaining room are read at the action boundary, which re-checks them before mutating anyway
([W39](world-collection-decisions.md)); the effect classification is a lookup into the published
effect table ([W43](world-collection-decisions.md)); and the candidate walk is over the published
tables rather than the two registries ([W44](world-collection-decisions.md)).

**Auto Harvest** consumes it too, which is what made the snapshot's sufficiency a real claim rather
than a claim about one service. Six of its eight facts come from the snapshot: five from the
plot-node and plot-action tables, and the sixth — the action's audited structural safety — from the
plot-authoring, phase-descriptor and effect-block tables, computed on the worker as this service's
own policy rather than published as a verdict ([W54](world-collection-decisions.md)). A false
plot-action prerequisite latch is published as needing validation, not as an unmet prerequisite; an
otherwise-ready GameAction calls the exact current action's domain validator once before quantity
mutation ([W37](world-collection-decisions.md)). It has no main-thread fact-capture stages left to
measure.

| Native read | Why it stays |
| --- | --- |
| `ActionManager` active-action list | Whether this pair is already queued or running, and whether a slot is free. Published now, but read where it is *acted on*: a collected reading admits nothing ([W53](world-collection-decisions.md)). |
| The plot's own `actionInstances`, per submission | The instance to submit into. A live object rather than a fact — every fact the decision rested on rode in on the action. |
| The two pairs' plot and action objects | Resolved once per lifecycle by uuid. Needed to mutate, not to decide. |
| The exact action's parameterless prerequisite validator | A false published latch cannot distinguish “unmet” from “not checked.” One fresh action-boundary call supplies that verdict; before/result/after latch evidence is recorded and no UI method participates. |

Exempting it from the migration on the grounds that its capture was narrow was a mistake, for reasons
that had nothing to do with what Auto Harvest gains — see [W14](world-collection-decisions.md),
[W22](world-collection-decisions.md), and [W37](world-collection-decisions.md).

## Layout

One file per category under `src/Common/Runtime/World/Categories/`, each holding that
category's row struct and its binder. The machinery they plug into lives one directory up:
`WorldCategoryMachinery.cs` (buffers, readers, derivers), `NativeAccessorBinder.cs` (member binding),
`GameWorldCollector.cs` (the pass), `GameWorldStateDeriver.cs` (the five derived categories).

Five categories are exceptions to one-row-per-entity. Which *tables* the identity walk skips is a
longer list, and it is stated in exactly one place — `NotIdentityTables` in
`tests/OrbModding.Tests/Runtime/Verification/WorldIdentityWalkTests.cs` — because every second
reading of an entity another table already claims lands there.

Purchase costs are the first: `Categories/WorldPurchaseCost.cs` holds a
second walk of the structure registry, a buffer that admits several rows per entity, and a deriver
that prices both kinds of entity from four other tables. `Categories/WorldUpgradeCost.cs` is the
matching second walk of the upgrade registry, and appends into the same buffer — which is why the
collector, not either reader, is what resets it. Read the result through `WorldPurchaseCostLookup`,
which returns an entity's whole range — a partial price is worse than none.

Each purchase-cost row keeps the authored `BaseExactAmount` separate from the verified port's
`EffectiveExactAmount`. Its immutable modifier-source table names every structure-chain input or
upgrade per-level modifier, including the stable resource/entity/variable UUID when one exists.
Affordability is a same-generation fact over the complete entity range: ordinary costs compare with
true holdings, bandwidth costs compare with headroom, and duplicate authored entries for one
resource are combined before either comparison. `WorldExactCostMath.TryCombinedExactCost` is the
single aggregation definition used here and by Auto Buy; a grouped request is refused unless every
row carries that exact rising-curve group, and no caller approximates it as `levels * next cost`.
The row's affordability deliberately excludes Auto Buy reserves and excess modes, which are feature
policy rather than facts about whether the native price is covered.

Plot actions are the second, and go further: a row is a *pair*, so it has no identity of its own at
all. `Categories/WorldPlotAction.cs` walks the plot registry a second time, recording which actions
each plot offers and which it is running, and prices each pair against the plot's remaining quantity
on the worker. Read it through `WorldPlotActionLookup`, which searches the composite key. It is
exempt from the identity walk for the same reason purchase costs are, only more so: neither guid on
a row belongs to the row.

Action queues are the third, and split the difference. `Categories/WorldActionQueue.cs` publishes two
tables: a queue is an entity — the list variable carries a uuid no other category collects — so
`ActionQueues` is walked like any other identity table, while `ActionQueueSlots` is keyed by its
queue and its index and is exempt beside the costs and the pairs. Neither is reached by a registry
walk. A list variable's `All` is declared on its generic base and the member binder does not walk
base types, so both queues are resolved by uuid through the identity registry every other lookup
already goes through — which also avoids the action-manager singleton entirely.

Spell slots are the fifth, and are the only category keyed by a position rather than by a guid.
`Categories/WorldSpellSlot.cs` publishes the equipped loadout and `Categories/WorldSpellCost.cs`
publishes what casting out of it costs, both filled by one reader — a slot's price is only answerable
from the same equipped instance the slot was read from, so a second walk would ask the game the same
question twice. Neither table is identity-keyed: a position may be unfilled and two positions may
hold the same spell, so `SlotIndex` is the key and it counts the unfilled positions exactly as the
game does, because it is the number a cast is addressed by. Like the queues, the loadout is reached
by uuid through the identity registry rather than through the spell-manager singleton, and it is a
bespoke reader rather than a plain binder so that `Spell`'s unread serialized fields are not dragged
into the declared surface ([W60](world-collection-decisions.md)).

Entity requirements are the fourth. `Categories/WorldEntityRequirement.cs` reads every upgrade's,
structure's, and Research entry's per-level prerequisite container, so a row is one condition and an entity has as
many as it was authored with. It claims no identities at all — the rows are keyed by an entity its
own category already claimed, and claiming again would report every upgrade as a duplicate of itself.
Its list is `[SerializeReference]`, so accessors are compiled per concrete condition class on first
sight; a class that does not bind yields a row of kind `Unknown` rather than none, because an
unmodelled condition has to be visible as a requirement nobody can evaluate rather than as an entity
with no requirements. Beside the authored graph, one non-identity table carries the native
parameterized `Check(ConditionInfo)` verdict and its exact input level for every upgrade, structure,
and Research entry.
It is a same-generation differential oracle, not a replacement for the graph or an admission result;
the worker fails loud if its graph verdict disagrees ([W58](world-collection-decisions.md),
[W68](world-collection-decisions.md)).
