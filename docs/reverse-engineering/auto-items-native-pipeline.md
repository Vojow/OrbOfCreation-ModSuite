# Auto Items native pipeline

> **Evidence status: accepted metadata contracts, serialized consumable topology, live read-only
> item evidence, and guarded Scroll/Relic/temporary submission.** Live effect completion has not
> been validated.

[Back to reverse-engineering index](README.md) ·
[Game boundary doctrine](../runtime-architecture/game-boundary-doctrine.md)

## Scope and audited baseline

This dossier reconciles the useful native evidence from the Auto Items work on PR #102 with the
suite's one-cadence ServiceCycle runtime and game-boundary doctrine. Auto Items supports Scrolls,
Relics, and exact-UUID-approved temporary Fruits, Potions, and Threads. Auto Scribe is a separate
feature. One registered feature-wide quick control changes `AutoItems.Mode`; there is no
per-family or temporary-item control.

The declared contracts were checked against the read-only `artifacts/game-v105` managed
assemblies for Orb of Creation v1.0.5-2. No game binary, installation, or save was changed.

## Identity and published facts

Scroll and Relic are memberships in `ConsumableSO.consumableTypes`, not managed subtypes and not
names. The exact supported `ConsumableTypeSO` identities are:

| Family | UUID | Canonical native name |
|---|---|---|
| Fruit | `46e0ab83-df7c-4f35-8012-3d9a3c97b753` | `Fruit` |
| Potion | `8103dae4-6945-4d18-b562-d2ffcd7ef49e` | `Potion` |
| Relic | `5d27b76e-eed3-49cc-a069-b9106000ede4` | `Relic` |
| Scroll | `70b36536-64e5-4f70-ad6f-af5787d719cc` | `ScrollConsumable` |
| Thread | `66a50127-5210-4a3a-93f4-952287858b90` | `ThreadConsumable` |

The collector publishes all native type memberships rather than reducing them to one guessed
family. That relation is a set, not a discriminator, but the item's operation is single. In the
eight authored family sets below, the second authored family — Fruit, Treasure, Modification,
Resource, or Food — records an acquisition channel or category rather than another operation.
Treasure relics and fruit relics are therefore both Relics; Treasure and Fruit describe how those
Relics are acquired. A sole supported membership selects its operation. The accepted assets author
four permanent fruits — Blitz Berry, Continuous Coconut, Frugal Fig, and Power Pear — as both
`Fruit` and `Relic`; that exact supported set selects the Relic operation. Other multi-family rows
pair one supported operation family with Food, Modification, Resource, or Treasure metadata. No
supported membership, a repeated stable family UUID, or any other combination spanning multiple
supported operations fails closed.

The live read-only Game MCP resolved `a1799c52-f9ff-4556-b052-f577ac3e7270` as an exact visible
`ConsumableSO` with quantity zero, `hasDuration=false`, and `durationBase=8`. Read-only type-tree
inspection of the installed `sharedassets0.assets` identifies it as `Continuous Coconut` and its
two `consumableTypes` references as Fruit
(`46e0ab83-df7c-4f35-8012-3d9a3c97b753`) and Relic
(`5d27b76e-eed3-49cc-a069-b9106000ede4`). The same asset census finds 68 consumables across exactly
eight single- and multi-membership patterns; the only two-supported-operation topology is the four
authored `Fruit + Relic` permanent fruits. This acquisition-channel meaning, together with their
non-duration shape, is why the resolver encodes that exact game-authored set rather than a general
precedence rule over unseen combinations.

The shared world also publishes each consumable's quantity, queued quantity, preparation and
cooldown readings, visibility, randomization capability, maximum carry, immediate `consumeCost`,
held `usageCost`, usages, and per-level counts. Relations are all-or-nothing per consumable: an
unreadable relation rejects that complete item reading instead of authorizing policy from a partial
graph. Costs retain every resource row. Auto Items requires the exact capped inverted Toxicity
resource and its immediate cost for worker admission; `CanFire()` remains authoritative for the
complete live cost vector.

## Native submission

The player-facing path is:

```text
ConsumableSO.SelectAndFire()
  -> CollectQuantity(GlobalVariables.GetMultiBuy())
     -> CanFire()
     -> consumeCost.PerformCost()
  -> queuedQuantity increases
  -> Inventory.QueueConsumable(this)
  -> idle inventory begins preparing the queued item
```

Auto Items acquires the suite's shared `NativeMultiBuyOverride` lease and enters
`NativeMultiBuyScope` with quantity one. It never writes stock, queue state, Toxicity, or effects
directly. Under the required idle-inventory preflight, a successful submission has an immediate,
observable edge: stock decreases by one and queued quantity increases by one. Later preparation,
random target selection, and the eventual Scroll or Relic effect remain game-owned and are not
reported as completed by this boundary.

Scrolls additionally require `canBeRandomized`, enable the native randomized flag, confirm it with
`IsRandomized()`, and use the authored request-target graph. The boundary requires exactly one exact
`RequestTargetEffectScript`, obtains its exact `TargetStructure`, recomputes the valid target list
for the strongest live owned level, and refuses an empty list with that exact explanation. It never
enters the manual targeting branch.

Temporary items use the same `ConsumableUse` action. They have no service, lease, health row, or
action family of their own. Worker admission requires the exact item UUID in
`AutoItems.TemporaryItemAllowlist`, finite positive duration, no preparation or cooldown, stock,
Toxicity headroom, and immediate/held cost vectors containing no resource category other than
Toxicity. The boundary repeats exact UUID/type/family, duration, and toxicity-only vector checks,
then applies the same live `CanUseConsumable()`, `CanFire()`, ownership, and quantity/queue proof.
It additionally requires exactly one new native usage.

Mutual exclusion is global across temporary families: any pending or active temporary usage blocks
Scroll, Relic, and temporary submissions. Conversely, native Scroll or Relic preparation makes
`Inventory.CanUseConsumable()` refuse a temporary submission.

After a committed temporary receipt, lifecycle-scoped worker state follows only that exact item.
Later world publications must show exactly one usage and at least one engaged reading before it
disappears. Multiple usages, an expired usage before proof completes, or disappearance without
observed engagement quarantines that exact UUID for the lifecycle and publishes the exact cause in
Runtime health. There is no expiry timer; disappearance and expiry are facts from later ordinary
world publications.

## Freshness classification

The protocol applies these classes from the
[game-boundary doctrine](../runtime-architecture/game-boundary-doctrine.md):

| Native check or operation | Class | Protocol consequence |
|---|---|---|
| Stable UUID plus exact `ConsumableSO` resolution | Pure | Resolve from the lifecycle-stamped native registry immediately before use; a missing or differently typed object rejects. |
| `consumableTypes` plus `ConsumableTypeSO.GetGuid()` | Pure | Re-read the complete live membership set, resolve a sole supported operation or the exact authored `Fruit + Relic -> Relic` topology, and require that operation to match the plan. Every other cross-operation set rejects. |
| `IsVisible()`, `canBeRandomized`, `IsRandomized()` | Pure | Read live. Randomized mode is confirmed again after `SetRandomization(true)`. |
| `hasDuration` and `durationBase` | Pure | Re-read the live fields for a temporary item and require a finite positive duration immediately before admission. |
| `consumeCost.costs` and `usageCost.costs`, exact `ResourceTuple.resource` UUID/type, and `valueBig` | Pure | Traverse both complete live vectors. Immediate cost must contain Toxicity; neither vector may contain another resource or an invalid magnitude. `CanFire()` remains the affordability oracle. |
| `ConsumableSO.All`, each temporary family membership, and `consumableUsages` | Pure | Re-read all exact native consumables and refuse every family while any temporary usage is pending or active. An unreadable row refuses with its exact reason. |
| Native busy check, `Inventory.CanUseConsumable()` | Pure | Read the live shared preparation state immediately before mutation. A false result is a named `NativeBusy` refusal. |
| `ConsumableSO.CanFire()` | Pure | Read live stock, cooldown, and native cost admission immediately before the ownership permit and mutation. A false result is a named `CanFireRefused` refusal. |
| `GetStrongestLevel()`, `GetStrongest()`, `GetCountScalingInfo()` | Pure | Rebuild the strongest-level scaling input from the live item; a change from the planned level rejects. |
| `TargetSelectOptions.GetTargeting()` and `TargetStructure.GetRandomList(ScalingInfo)` | UI-cached, revalidatable | Treat published or previously computed targets as stale-capable. Invoke this exact authored chain as the declared scoped recomputation and require a non-empty result; no ambient screen visit or blanket cache warming is trusted. |
| `SelectAndFire()` and the later durable effect | Unrefreshable / attempt-and-verify | Submit exactly once. Verify stock -1 and queue +1; also verify randomization for Scroll or usage +1 for a temporary item. Never claim durable effect completion, and let later publications supply activation evidence. |

There is no feature-side configuration freshness check. Dispatch deliberately uses the
cycle-pinned configuration, as specified by the doctrine. Configuration staleness is bounded by the
one-or-two-frame batch drain and its single player-driven writer; native facts are different because
the game can change them every frame without bound. Committed master-disable refresh also releases
the consumable-use ownership lease as a fast backstop, not as the policy-correctness mechanism.

## GameAction boundary

`AutoItemsConsumableUseGameAction` is the sole consumable-use mutation definition. At each lifecycle
it validates the complete reflected binding set before resolving an item:

- `ConsumableSO`, `ConsumableTypeSO`, `ResourceSO`, `Inventory`, `ConsumableCount`,
  `ConsumableUsage`, `ResourceCostList`, `ResourceTuple`, and `ScalingInfo`;
- `InstantEffectBlock`, `IInstantEffectScript`, and exact `RequestTargetEffectScript`;
- exact target options, base selection, target structure, and targetable types;
- every field and method used for family, visibility, randomization, targeting, busy/readiness
  admission, quantity/queue evidence, and `SelectAndFire()`.

Only immutable reflection metadata is retained. Scene, save-load, reset, and NG+ lifecycle
invalidation drops the set, clears quarantine and health, and validates a new complete set before
another use.

The action order is:

1. resolve stable UUID plus exact native type;
2. revalidate live family and visibility;
3. repeat temporary duration and complete immediate/held toxicity-only cost checks, or Scroll
   randomization capability and the scoped target recomputation;
4. scan all temporary families for pending or active usage;
5. check native busy and `CanFire()`;
6. capture current cooperative ownership permits with their exact conflict explanation;
7. enter native multi-buy quantity one;
8. capture stock, queue, randomization, and usage-count state;
9. perform the native randomization/use mutation;
10. capture again and require the exact family-specific immediate deltas.

An unavailable complete binding set is `ContractUnavailable`. Lost identity, changed family,
native busy, lost visibility, a native fire refusal, an empty target result, and a lost permit are
named refusals. A mutation attempt that throws or cannot prove its postcondition faults the receipt
with its native-call evidence. Scroll or Relic ambiguity quarantines the entire consumable
GameAction; temporary ambiguity quarantines only the exact item UUID. Health and diagnostics retain
the same exact reason. A later ordinary publication cannot retry the quarantined target; lifecycle
replacement is the explicit recovery boundary.

The pattern is local because the binding topology, preflight vocabulary, and postcondition are
capability-specific. A follow-up Common extraction should be limited to a small lifecycle-scoped
GameAction shell that owns complete-bind state, quarantine state, and evidence-to-receipt mapping.
It should not introduce a generic preflight DSL, feature policy keys, configuration readers, or a
reflection abstraction that hides each capability's declared contracts.

## Scheduling and configuration

Auto Items is an ordinary ServiceCycle service with a one-action main-thread turn. It evaluates
after world or committed-configuration publication and returns `OnPublication` on every path.
There is no evaluator interval, native-busy poll, cooldown, recovery latch, immediate chain, or
candidate memory. The engine's one-attempt-per-world floor supplies the retry boundary.

The additive configuration is:

- `AutoItems.Mode`, default `Disabled`;
- `AutoItems.UseScrolls`, default `true` behind the master mode;
- `AutoItems.UseRelics`, default `true` behind the master mode.
- `AutoItems.TemporaryItemAllowlist`, default empty; comma-separated exact UUIDs only.

The allowlist has no family switch: naming an exact UUID is the complete player approval, a near
miss authorizes nothing, and an empty list leaves temporary-item behavior inert. The worker parses
it once per committed `ConfigGeneration` and pins that table to the cycle. The action carries no
configuration key, and neither adapter nor GameAction has a current-configuration reader.

The Mods-page picker is UI over this unchanged serialized key. Its main-thread display capture reads
`ConsumableSO.All`; exact `GetGuid()` identity; the `visible` discovery flag; the
`consumableTypes` relationship and each type's `GetGuid()`; the item's native `GetIcon()`; and the
current private `quantity` stock field. Item and family labels come only from Common's already-bound
live entity catalog, so the picker no longer owns parallel reflection contracts for `GetName()`. It retains
immutable facts plus the captured sprite, never a consumable or native UI object. The catalog feeds
each row's supported membership set through the same exact-topology resolver used by worker policy
and live boundary revalidation. Only rows whose resolved operation is Fruit, Potion, or Thread are
listed. Blitz Berry, Continuous Coconut, Frugal Fig, and Power Pear consequently resolve as Relics
and are excluded. Listed rows display every authored family name with the resolved operation first
and remaining acquisition/category metadata in native order, sort by resolved operation, native
item name, then UUID, and use the same captured base/active frame pair as the Mods rail and quick
controls.

The picker always renders `<approved> of <discovered> approved`. Unknown valid UUIDs and malformed
hand-edited tokens remain explicit removable rows instead of being normalized away. A genuinely
empty catalog says `No discovered temporary items yet.`; a binding, identity, family, name, icon,
stock, or enumeration failure says `Discovery read failed` in the failure color and recessed frame.
Stored entries remain visible and removable beside that failure. There is no blacklist, family
toggle, bulk selection, filter, or raw editor. Row clicks only call
`ConfigEditValue.Stage`; the existing Apply/Revert transaction remains the sole persistence path.

These keys join the existing committed store and one configuration generation. They require no
configuration schema migration because no prior shipped key is rewritten or removed. The picker
appears on the existing consolidated Auto Items Mods page, so there is no new rail entry or feature
icon.
The existing Auto Items feature-wide quick control changes the same master mode and never changes
the exact-UUID allowlist.

## Evidence limits

Portable tests use exact-shape game stubs. They cover live-family change, native busy, lost permit,
manual stock race, empty Scroll targets, ambiguous postconditions, lifecycle reset, direct adapter
mapping, publication-driven planning, exact/near-miss allowlisting, every temporary shape guard,
mutual exclusion in both directions, native stock/duration/Toxicity/usage evidence, exact-item
quarantine, and injected double-usage, premature-expiry, and missing-engagement publications. The
target stub injects a candidate list and therefore does not reproduce the game's complete
structure-eligibility calculation or eventual random choice. The installed-contract gate proves
the exact members and signatures. The read-only installed asset census and live Game MCP reading
cover the authored Continuous Coconut topology; they do not prove any durable effect.

Before claiming live gameplay completion, a runtime validation lane must observe at least one
Scroll and one Relic on the accepted game build: idle/busy admission, consume-cost and stock/queue
edges, random-target refusal and success, preparation completion, lifecycle replacement races, and
the later durable effect. That work is intentionally not inferred from these portable fakes.
