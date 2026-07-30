# Auto Items native pipeline

> **Evidence status: accepted metadata contracts and guarded Scroll/Relic submission.**
> Serialized asset topology and live effect completion have not been validated in a running game.

[Back to reverse-engineering index](README.md) ·
[Game boundary doctrine](../runtime-architecture/game-boundary-doctrine.md)

## Scope and audited baseline

This dossier reconciles the useful native evidence from the Auto Items work on PR #102 with the
suite's one-cadence ServiceCycle runtime and game-boundary doctrine. This lane implements only
Scrolls and Relics. Fruit, Potion, and Thread automation, an item allowlist, Auto Scribe, quick
controls, and installer work belong to later lanes and are neither implemented nor represented by
placeholder configuration.

The declared contracts were checked against the read-only `artifacts/game-v105` managed
assemblies for Orb of Creation v1.0.5-2. No game binary, installation, or save was changed.

## Identity and published facts

Scroll and Relic are memberships in `ConsumableSO.consumableTypes`, not managed subtypes and not
names. The exact supported `ConsumableTypeSO` identities are:

| Family | UUID | Canonical native name |
|---|---|---|
| Relic | `5d27b76e-eed3-49cc-a069-b9106000ede4` | `Relic` |
| Scroll | `70b36536-64e5-4f70-ad6f-af5787d719cc` | `ScrollConsumable` |

The collector publishes all native type memberships rather than reducing them to one guessed
family. Policy accepts exactly one supported family; no supported membership and conflicting
supported memberships both fail closed.

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

## Freshness classification

The protocol applies these classes from the
[game-boundary doctrine](../runtime-architecture/game-boundary-doctrine.md):

| Native check or operation | Class | Protocol consequence |
|---|---|---|
| Stable UUID plus exact `ConsumableSO` resolution | Pure | Resolve from the lifecycle-stamped native registry immediately before use; a missing or differently typed object rejects. |
| `consumableTypes` plus `ConsumableTypeSO.GetGuid()` | Pure | Re-read the complete live membership and require exactly one supported family matching the plan. |
| `IsVisible()`, `canBeRandomized`, `IsRandomized()` | Pure | Read live. Randomized mode is confirmed again after `SetRandomization(true)`. |
| Native busy check, `Inventory.CanUseConsumable()` | Pure | Read the live shared preparation state immediately before mutation. A false result is a named `NativeBusy` refusal. |
| `ConsumableSO.CanFire()` | Pure | Read live stock, cooldown, and native cost admission immediately before the ownership permit and mutation. A false result is a named `CanFireRefused` refusal. |
| `GetStrongestLevel()`, `GetStrongest()`, `GetCountScalingInfo()` | Pure | Rebuild the strongest-level scaling input from the live item; a change from the planned level rejects. |
| `TargetSelectOptions.GetTargeting()` and `TargetStructure.GetRandomList(ScalingInfo)` | UI-cached, revalidatable | Treat published or previously computed targets as stale-capable. Invoke this exact authored chain as the declared scoped recomputation and require a non-empty result; no ambient screen visit or blanket cache warming is trusted. |
| The target that `SelectAndFire()` will ultimately choose and the later durable effect | Unrefreshable / side-effectful | Never pre-trust an injected or cached chosen target and never claim effect completion. Submit once, verify only the immediate stock/queue/randomization evidence, and let the next world publication observe later game state. |

There is no feature-side configuration freshness check. Dispatch deliberately uses the
cycle-pinned configuration, as specified by the doctrine. Configuration staleness is bounded by the
one-or-two-frame batch drain and its single player-driven writer; native facts are different because
the game can change them every frame without bound. Committed master-disable refresh also releases
the consumable-use ownership lease as a fast backstop, not as the policy-correctness mechanism.

## GameAction boundary

`AutoItemsConsumableUseGameAction` is the sole Scroll/Relic mutation definition. At each lifecycle
it validates the complete reflected binding set before resolving an item:

- `ConsumableSO`, `ConsumableTypeSO`, `Inventory`, `ConsumableCount`, and `ScalingInfo`;
- `InstantEffectBlock`, `IInstantEffectScript`, and exact `RequestTargetEffectScript`;
- exact target options, base selection, target structure, and targetable types;
- every field and method used for family, visibility, randomization, targeting, busy/readiness
  admission, quantity/queue evidence, and `SelectAndFire()`.

Only immutable reflection metadata is retained. Scene, save-load, reset, and NG+ lifecycle
invalidation drops the set, clears quarantine and health, and validates a new complete set before
another use.

The action order is:

1. resolve stable UUID plus exact native type;
2. revalidate live family, visibility, and Scroll randomization capability;
3. run the scoped Scroll target recomputation when applicable;
4. check native busy and `CanFire()`;
5. capture current cooperative ownership permits with their exact conflict explanation;
6. enter native multi-buy quantity one;
7. capture stock, queue, and randomization state;
8. perform the native randomization/use mutation;
9. capture again and require the exact immediate deltas.

An unavailable complete binding set is `ContractUnavailable`. Lost identity, changed family,
native busy, lost visibility, a native fire refusal, an empty target result, and a lost permit are
named refusals. A mutation attempt that throws or cannot prove its postcondition faults the receipt
with its native-call evidence and quarantines the entire consumable GameAction for the lifecycle.
Health and diagnostics retain the same exact reason. A later ordinary publication cannot retry an
ambiguous mutation; lifecycle replacement is the explicit recovery boundary.

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

These keys join the existing committed store and one configuration generation. They require no
configuration schema migration because no prior shipped key is rewritten or removed. Auto Items
has a Mods feature page and Runtime health reporter, but this lane deliberately adds no gameplay
quick control.

## Evidence limits

Portable tests use exact-shape game stubs. They cover live-family change, native busy, lost permit,
manual stock race, empty Scroll targets, ambiguous postconditions, lifecycle reset, direct adapter
mapping, and publication-driven planning. The target stub injects a candidate list and therefore
does not reproduce the game's complete structure-eligibility calculation or eventual random choice.
The installed-contract gate proves the exact members and signatures, not the authored topology of
every Scroll asset or the durable result of any Scroll or Relic.

Before claiming live gameplay completion, a runtime validation lane must observe at least one
Scroll and one Relic on the accepted game build: idle/busy admission, consume-cost and stock/queue
edges, random-target refusal and success, preparation completion, lifecycle replacement races, and
the later durable effect. That work is intentionally not inferred from these portable fakes.
