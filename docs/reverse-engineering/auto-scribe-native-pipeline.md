# Auto Scribe native pipeline

> **Evidence status: implemented and installed-contract verified against both accepted 1.0.5-2
> baselines; interactive validation remains open.** Metadata, immutable world publication, the
> rewritten one-shot GameAction, and deterministic paid-failure tests are present. No game process
> or save was used for this port.

[Back to reverse-engineering index](README.md) ·
[Game boundary doctrine](../runtime-architecture/game-boundary-doctrine.md) ·
[Testing doctrine](../testing/README.md)

## Evidence and compatibility boundary

The original dossier was established from read-only ILSpy and AssetRipper inspection of the
accepted Windows build. This release-line port retains that native evidence while reconciling the
implementation to the suite's accepted GameAction doctrine. The manifest admits the exact Windows
and macOS 1.0.5-2 `Assembly-CSharp.dll`/`Assembly-CSharp-firstpass.dll` pairs and verifies every
reflected Scribe field, method, inheritance relation, return type, parameter list, staticness, and
the `CraftingInstance(CraftingRecipeSO, BigDouble)` constructor.

The manifest check is necessary but not sufficient. Runtime makes the mutation capability
available only when one complete binding set resolves at lifecycle scope. No type search, overload
selection, field lookup, or string-named helper remains after the action can begin.

No binary, installation, running game, or save was changed while reconciling this dossier.

## Stable role identities

The supported semantic role graph is:

| Role key | Recipe UUID | Scroll UUID | Enchantment UUID | Cost rank |
|---|---|---|---|---:|
| `scribe.advancement` | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` | 0 |
| `scribe.power` | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` | 1 |
| `scribe.learning` | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` | 2 |
| `scribe.excellence` | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` | 3 |
| `scribe.development` | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` | 4 |
| `scribe.echo` | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` | 5 |
| `scribe.investment` | no audited recipe | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` | coverage only |
| `scribe.speed` | no audited recipe | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` | coverage only |

The common recipe type is `ScribeCrafting`, UUID
`ee001474-8209-4238-9566-84899a877226`, native type `CraftingRecipeTypeSO`.
Configuration accepts only the stable semantic keys. Names are diagnostic, and UUID ordering,
serialized registry order, or localized strings never decide policy.

Investment and Speed are real Scroll/enchantment identities but are not inferred to have recipes.
They remain coverage-only until another audited baseline proves a native production path.

## Serialized registry and relationship proof

The `ScribeCraftingRecipes` registry has UUID `2917516f-34a5-47b7-85b2-0b2f9ab3a29f`,
native type `CraftingRecipeListVariable`, and exactly these six serialized recipes:

| Unity asset GUID | Registered asset |
|---|---|
| `8606cdecd57531f45a29721686cf3f46` | `CraftScrollAdvancement` |
| `97b0b66b7aa06ed41b09fca3963b9619` | `CraftScrollDevelopment` |
| `2485059731ae6f340abef8146fa311dc` | `CraftScrollEcho` |
| `9fdb05cf0be9b6c4d9da94886d285381` | `CraftScrollExcellence` |
| `195a4c0244b73514791414a811f72b59` | `CraftScrollLearning` |
| `554e9363d5830b2489ce5314690e8a42` | `CraftScrollPower` |

Identity alone is not relationship evidence. Lifecycle collection and action preflight both require:

1. exactly six exact `CraftingRecipeSO` registry values;
2. one audited stable recipe UUID for every producible role;
3. exactly one `ScribeCrafting` type reference;
4. `useQuantityAsLevel=true`, `CraftingRecipeTypeSO.isLevelType=true`, and the same live main type;
5. exactly one `ConsumableGainEffect` output referencing that role's exact Scroll;
6. exactly one `RequestTargetEffectScript` on the Scroll;
7. an exact `TargetStructure` target selector; and
8. exactly one `EnchantItemScript` referencing the role's exact enchantment.

Any missing, extra, wrong-typed, or contradictory edge makes relationship evidence unknown. It
does not become a weaker name-based mapping.

## Level authority

`CraftingRecipeTypeSO` saves two distinct values:

- `startingLevel`: the player's manual-crafting selector;
- `maxStartingLevel`: the highest unlocked starting level.

`CraftingRecipeSO.GetStartingQuantity()` uses the first value for a level recipe, while a
`CraftingInstance` stores its own `quantity`. Auto Scribe supplies the selected level directly to
the one-shot instance and never changes `startingLevel`.

The native payment/progression composite is:

```text
CraftingRecipeSO.PurchaseQuantity(purchasedQuantity, previousQuantity)
  -> pay GetTotalCost(previousQuantity, purchasedQuantity)
  -> CraftingRecipeTypeSO.SetMaxStartingLevel(
       purchasedQuantity + previousQuantity)
```

`SetMaxStartingLevel` keeps the larger of its old and proposed values. For a one-shot purchase,
`previousQuantity` is zero, so the expected transition is
`max(before.maxStartingLevel, purchasedLevel)`. The resulting Scroll separately raises its
`maxCreatedLv` when gained.

The shared `maxStartingLevel` is progression evidence and a payment receipt, not a coverage target
for every recipe. Each Scroll owns a frontier equal to the strongest of its positive
`maxCreatedLv`, owned level, non-expired queued work, and non-expired pending use, with level one as
the initial floor. A stable covered role requests the next level. The guarded action requires that
minimum to be affordable, brackets the first unaffordable level, and binary-searches the monotonic
`CanBuyAt(BigDouble)` boundary before running the exact cost preflight. Purchasing that strongest
affordable level is the native way to advance both the individual Scroll frontier and, when higher,
the shared ceiling.

## Native Scribe lists and external production

The Scribe page references two exact list assets:

| UUID | Runtime type | Canonical name |
|---|---|---|
| `b557060a-e109-40de-9a7d-f2b02bc9766d` | `CraftingInstanceListVariable` | `ActiveScribeInstances` |
| `f6cb65a8-a959-477c-9293-ff66f646c95d` | `CraftingInstanceListVariable` | `AutoScribeInstances` |

The game's persistent automation path creates or updates one repeating instance per recipe. A
`CraftingInstanceListVariable` does not retain caller ownership, so the suite cannot safely edit a
matching persistent instance. Auto Scribe only observes `AutoScribeInstances` as external supply.
It never creates, edits, removes, or claims a persistent automatic instance.

The manual one-shot UI path is:

```text
UICraftingPage.QueueCraft(recipe, quantity)
  -> recipe.PurchaseQuantity(quantity, existingQuantity)
  -> existing stack.AddQuantity(quantity), or
  -> new CraftingInstance(recipe, quantity)
     -> Initiate()
     -> craftingQueueInstances.Add(instance)
```

There is no non-UI one-shot composite. Re-driving the data-layer sequence below the UI handler is
therefore the only supported automation entry point. The suite uses a new instance and refuses when
matching active or automatic supply already exists at the requested level or higher.

## Levelled inventory and coverage

`ConsumableSO.consumableCounts` is a list of `ConsumableCount` rows. `GetLevel()` supplies the
row's scaling level and `GetQuantity()` supplies its quantity. Aggregated consumable quantity is
insufficient because native Scroll use selects the strongest count and carries that count's scaling
through the use.

Scrolls are a consumption pipeline. Their value is realized by applying them to eligible
structures, and each structure retains its applied enchantment level. Stock at or below every
remaining target's level creates no coverage. For one role and its independent Scroll frontier,
coverage demand is therefore:

```text
when maximum carry load > 0:
    desired = min(uncovered eligible structures, maximum carry load)
otherwise:
    desired = 0 and block with NonPositiveCarryLimit

deficit = max(0, desired
                 - owned Scrolls at or above target level
                 - active one-shot work at or above target level
                 - pending Auto Items uses at or above target level)
```

The positive carry limit is an in-flight clamp, not a stock target. Demand below capacity produces
only the uncovered count; demand above capacity produces at most one carry load. A gift naturally
reduces the next publication's deficit. Full target-level stock makes the deficit zero, so the
suite never pays for an equal-level replacement that would leave coverage unchanged. Weaker stock
does not subtract from frontier demand and does not create a cleanup action.

The audited v1.0.5 `ConsumableSO.Gain()` path defines “weakest” by level only. At capacity, an
incoming level strictly above the weakest removes that weakest unit and admits the stronger unit;
an equal level also replaces a unit but does not change level coverage; and a strictly weaker level
decrements the incoming amount to zero, silently dropping it. `Gain()` first clamps its positive
incoming amount to `maximumCarryLoad`, so a non-positive limit guarantees lost output. Native
`UICraftingPage.QueueCraft` pays `CraftingRecipeSO.PurchaseQuantity` before construction and
completion, and both instant and queued `CraftingInstance` completion execute the identical
`ConsumableGainEffect -> ConsumableSO.Gain()` path. The capacity decision therefore happens after
payment on both paths.

Demand-driven frontier crafting makes positive capacity self-cleaning: a needed frontier Scroll is
strictly stronger than dead stock, so native gain replaces the weakest as a side effect of the
craft the uncovered structure already required. No speculative inventory fill and no suite
`Discard()` call are necessary. Non-positive carry is instead a named fail-closed refusal because
no crafted output can survive that native clamp.

Automatic Scribe work at or above the target does not subtract a guessed quantity. It changes the
role disposition to external production, causing the suite to yield until a later publication
shows what the game produced.

Queued work and pending uses raise only their matching recipe's frontier. A higher shared
`maxStartingLevel` produced by cheap Advancement crafts never raises Power, Learning, Excellence,
Development, or Echoing by itself. Once ordinary demand is covered and no matching work is active,
the role becomes a next-level progression probe. The semantic cost order is a fair rotating cursor,
so an always-affordable earlier recipe cannot starve later unlocked recipes.

Every `StructureSO` owns an `EnchantmentSO.EnchantTable`. A native enchantment upgrade keeps a
stronger existing instance and replaces an equal-or-lower one only when the proposed scaling is
strictly stronger under the native `CanUpgradeEnchantment` relationship. World publication records
exact structure/enchantment levels; Auto Scribe never calls `AddEnchantment`.

## Native target selection

The supported Scroll graph contains one `RequestTargetEffectScript`, whose exact
`TargetSelectOptions.GetTargeting()` result is `Targeting.TargetStructure`.
`TargetStructure.GetRandomList(ScalingInfo.Basic(level))` filters visible eligible structures,
applies its serialized enchantment matcher, orders deficient targets, and returns no candidate when
the Scroll would not upgrade anything.

Collection publishes the exact candidate identities and a completeness count for each audited
Scroll/enchantment relation. Policy requires the rows and count to agree. The GameAction repeats
the exact native candidate query immediately before payment and refuses an empty result with the
Scroll UUID and requested level in the reason.

Portable fakes inject this candidate list. They prove the suite's count/evidence handling and live
empty-target refusal, not the game's complete structure-condition or ranking implementation.

## Rewritten one-shot GameAction

### Complete lifecycle binding set

Before the capability can run, `AutoScribeNativeBindings` resolves and validates:

- every native type used by relation proof, cost evidence, target selection, construction,
  initiation, and admission;
- exact instance/static field shapes, including generic element types;
- exact instance/static methods, declaring hierarchy, parameter types, return types, and staticness;
- separate identity methods for recipe, Scroll, enchantment, resource, and instance-reference
  domains;
- separate recipe-list and instance-list `value` fields; and
- exactly `CraftingInstance(CraftingRecipeSO, BigDouble)`.

One missing or ambiguous member returns `ContractUnavailable`; the GameAction remains unavailable
for that lifecycle. Lifecycle replacement discards quarantine and the complete binding set, then
binds a fresh set before another use.

### Preflight order

The main-thread action performs this complete order before `PurchaseQuantity`:

1. reject lifecycle quarantine or an unavailable binding set;
2. prove that the action recipe UUID and Scroll UUID identify the same audited role;
3. re-resolve the recipe, Scroll, enchantment, recipe type, registry, active list, and automatic
   list by stable UUID plus exact native type;
4. verify those live native UUIDs and the complete registry/recipe/Scroll/enchantment graph;
5. require `CraftingRecipeSO.IsVisible()`;
6. require `ActiveScribeInstances.HasEmptySpot()`;
7. require the requested per-Scroll level and bracket/binary-search the highest affordable level;
8. reject matching non-expired active or automatic supply at that selected level;
9. require a non-empty exact native target selection at that selected level and preserve its reason;
10. require exact `GetTotalCost(0, selected level)` type and `HasEnough()`;
11. capture resource quantities, ceiling, queue count, and exact-level Scroll stock; and
12. capture the `CraftingQueueSubmission` mutation permit.

Payment is the final suite-owned risk. No configuration, policy, registry relationship, target,
affordability, ownership, metadata discovery, or before-state read remains afterward.

### Mutation and exact evidence

The irreversible sequence is:

```text
PurchaseQuantity(level, 0)
new CraftingInstance(recipe, level)
Initiate()
CheckInstantCraft()
  -> InstantCraft()
  or ActiveScribeInstances.Add(instance)
```

The receipt re-reads each distinct resource in the exact `GetTotalCost` value, aggregates duplicate
resource rows, and proves that its quantity fell by the aggregated expected charge. It also proves
the exact `max(before ceiling, level)` transition and exactly one of:

- active queue count increased by one and contains non-expired work for the exact recipe and level;
- exact-level Scroll stock increased by one while queue count did not change.

A verified result carries four attempted native stages and one mutation attempt/verification. A
partial result preserves the reached stage, observed payment, resource, ceiling, queue, and stock
facts. It never substitutes the success call shape for incomplete work.

### Post-payment failure and quarantine

Native exceptions at payment, construction, initiation, instant admission, or queue admission
cannot be rolled back. The action captures the strongest available receipt, returns a faulted
mutation result with the exact stage and native reason, and quarantines Auto Scribe for the
remainder of that lifecycle. A postcondition mismatch follows the same quarantine path at the
verification stage.

Runtime health gives quarantine priority over later ordinary policy state. It reports the stage,
recipe UUID, exception or verification reason, and observed receipt. Lifecycle replacement is the
only recovery.

## Validator freshness classification

| Validator or evidence | Class | Boundary treatment |
|---|---|---|
| stable UUID plus exact runtime type | Pure | re-resolve live for every action |
| exact registry and serialized relationship graph | Pure | re-walk live before payment |
| `CraftingRecipeSO.IsVisible()` | Pure | call live before queue/cost work |
| `CraftingInstanceListVariable.HasEmptySpot()` | Pure | call live before payment |
| active and automatic instance membership | Pure | enumerate live before payment |
| `TargetSelectOptions.GetTargeting()` | Pure serialized dispatch | require exact `TargetStructure` |
| `TargetStructure.GetRandomList(ScalingInfo.Basic(level))` | Pure | repeat live immediately before payment |
| monotonic `CraftingRecipeSO.CanBuyAt(level)` frontier | Pure | bounded bracket and binary search live before cost capture |
| `GetTotalCost(0, level).HasEnough()` | Pure | call live and retain exact cost for receipt |
| action-family ownership permit | Pure process-local authority | capture immediately before payment |
| `PurchaseQuantity` acceptance | Side-effectful | attempt only after all preflights, then verify exact deltas |
| construction, initiation, and admission | Side-effectful | attempt once and quarantine any native exception |

No Scribe validator is known to be UI-cached or to require a UI-driven refresh. If runtime
staleness evidence changes that classification, the relevant validator must gain a named audited
revalidation path or stop authorizing action.

## Whole-publication fail-closed policy

The coverage planner first scans every currently enabled producible role. Missing category
collection, an incomplete recipe registry, a missing recipe, a contradictory relationship, a
missing positive target level, absent target evidence, or a contradictory target count makes the
role `EvidenceUnknown`.

One `EvidenceUnknown` role blocks the entire Auto Scribe service for that publication before cost
rank can select another role. Runtime health names the semantic role and exact evidence reason.
There is no degraded-but-producing state. The engine then waits for a strictly newer publication;
the feature owns no cadence, retry deadline, or candidate memory.

## Remaining runtime validation

Portable and installed-contract tests now prove the static surface, paid fake behavior, semantic
policy, F4 blocking, exact preflight reasons, both successful admission outcomes, every
post-payment injected-failure stage, receipt evidence, quarantine, and lifecycle reset.

A disposable-save Unity pass must still:

- correlate published structure/enchantment coverage with the visible game state;
- observe queue and native instant completion at the requested level;
- compare the exact resource and `maxStartingLevel` deltas with the receipt;
- exercise manual one-shot and persistent automatic competition;
- confirm empty native target selection blocks production;
- observe cheap and expensive Scroll recipes progress independently despite one shared Scribe ceiling;
- observe the fair cursor eventually selects every visible enabled producible role;
- confirm Runtime shows the exact role/evidence block and post-payment stage; and
- cross scene change, save/load, reset, NG+, shutdown, and restart.

Until that pass exists, the metadata result proves compatibility of the declared native shape, not
behavior inside Unity.
