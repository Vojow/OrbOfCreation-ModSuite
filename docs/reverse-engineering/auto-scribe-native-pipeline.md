# Auto Scribe native pipeline

> **Evidence status: raw-spend receipt implemented and portable/installed-contract verified;
> interactive validation remains required for this revision.** Metadata, immutable world
> publication, the rewritten one-shot GameAction, and deterministic paid-failure coverage are
> present.

[Back to reverse-engineering index](README.md) ·
[Game boundary doctrine](../runtime-architecture/game-boundary-doctrine.md) ·
[Auto Scribe tests](../testing/automata/auto-scribe.md)

## Evidence and compatibility boundary

The original dossier was established from read-only ILSpy and AssetRipper inspection of the
accepted Windows build. This release-line port retains that native evidence while reconciling the
implementation to the suite's accepted GameAction doctrine. The manifest admits the exact Windows
and macOS 1.0.5-2 `Assembly-CSharp.dll`/`Assembly-CSharp-firstpass.dll` pairs and declares every
reflected Scribe field, method, inheritance relation, return type, parameter list, staticness, and
the `CraftingInstance(CraftingRecipeSO, BigDouble)` constructor for exact contract validation.

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
matching non-expired active or automatic work already exists at any level.

## Levelled inventory and coverage

`ConsumableSO.consumableCounts` is a list of `ConsumableCount` rows. `GetLevel()` supplies the
row's scaling level and `GetQuantity()` supplies its quantity. Aggregated consumable quantity is
insufficient because native Scroll use selects the strongest count and carries that count's scaling
through the use.

For one role and its independent Scroll frontier, coverage demand is:

```text
desired = maximum carry load when positive, otherwise uncovered eligible structures
deficit = max(0, desired
                 - owned Scrolls at or above target level
                 - active one-shot work at or above target level
                 - pending Auto Items uses at or above target level)
```

The positive carry load is a hard native capacity bound, not merely a minimum buffer. Once stock at
or above the target level fills that capacity, additional uncovered structures cannot authorize a
futile same-level purchase. Lower-level stock is deliberately excluded from the subtraction, so a
stronger target can still use the native replacement path even when total Scroll inventory is full.

Queued consumable quantity, active preparation, any unexpired Scroll use, and matching non-expired
manual or automatic Scribe work at any level activate a capacity-replacement interlock. The role
becomes external production and yields without deficit production or progression until a later
background-world publication shows that all four signals have cleared.

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
- the raw-spend receipt methods `ResourceSO.GetQuantity()`, `GetTrueSpend(BigDouble)`,
  `HasDecay()`, `GetDecayPercent()`, and `IsBandwidthResource()`;
- the replacement signals `ConsumableSO.GetQueued()`, `currentPrepTime`, `consumableUsages`, and
  exact `ConsumableUsage.en`/`dr` expiry fields;
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
8. reject queued Scroll quantity, active preparation, any unexpired Scroll usage, or matching
   non-expired active/automatic Scribe work at any level;
9. require a non-empty exact native target selection at that selected level and preserve its reason;
10. require exact `GetTotalCost(0, selected level)` type and `HasEnough()`;
11. capture every cost row's nominal value, raw `GetTrueSpend` value, decay state and percentage,
    and the resource's raw `GetQuantity()`;
12. reject any bandwidth cost resource and reject when duplicate rows aggregate to a raw debit
    greater than the live raw balance, even if native row-by-row `HasEnough()` returned true;
13. capture ceiling, queue count, and exact-level Scroll stock; and
14. capture the `CraftingQueueSubmission` mutation permit; and
15. immediately repeat visibility, queue room, selected-level affordability, all replacement
    signals, target selection, exact cost affordability, raw spend evidence, and before-state
    capture before payment.

Payment is the final suite-owned risk. No configuration, policy, registry relationship, target,
affordability, ownership, metadata discovery, spend-modifier calculation, or before-state read
remains afterward. These reads stay inside the main-thread GameAction immediately around mutation;
the background world remains an immutable planning input and does not publish native resource
objects or attempt to authorize the final debit.

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

For each cost row, native `ResourceSO.Spend` first converts the nominal tuple value to a raw debit:

```text
expected raw debit = resource.GetTrueSpend(nominal tuple value)
if resource.HasDecay():
    expected raw debit *= 1 - resource.GetDecayPercent()
```

That calculation and the raw before quantity are captured immediately before payment. Replenish
may schedule a later gain but does not alter this immediate debit. Comparing `GetTrueQuantity()`
against the nominal tuple value is invalid because quality and decay participate in native spend
semantics.

The receipt retains the exact native row count, order, resource object, nominal value, and captured
raw debit. For each distinct resource it starts from the captured raw quantity and applies every row
in authored order as native `Spend` does:

```text
predicted after = BigDouble.Max(predicted before - row raw debit, 0)
```

It then requires the live raw quantity to equal that predicted post-state. Comparing a mathematical
`before - after` debit is not valid for `BigDouble`: when exponent distance exceeds its precision, a
positive native debit can leave the stored quantity unchanged. Such a verified row is classified as
below resolution rather than as an absent debit. Duplicate rows remain subject to the separate
aggregate pre-payment affordability check, preventing native row-by-row `HasEnough()` plus zero
clamping from hiding an underpayment; they are nevertheless reproduced sequentially for the
post-state proof, never collapsed into one synthetic spend. A bandwidth resource is unsupported and
rejects the action before payment. The receipt also proves the exact
`max(before ceiling, level)` transition and exactly one of:

- active queue count increased by one and contains non-expired work for the exact recipe and level;
- exact-level Scroll stock increased by one while queue count did not change.

A verified result carries four attempted native stages and one mutation attempt/verification. A
partial result preserves the reached stage, observed payment, first mismatched resource UUID,
nominal cost, expected raw debit, observed raw debit, captured decay evidence, ceiling, queue, and
stock facts. It never substitutes the success call shape for incomplete work.

### Post-payment failure and quarantine

Native exceptions at payment, construction, initiation, instant admission, or queue admission
cannot be rolled back. The action captures the strongest available receipt, returns a faulted
mutation result with the exact stage and native reason, and quarantines Auto Scribe for the
remainder of that lifecycle. A postcondition mismatch follows the same quarantine path at the
verification stage.

Runtime health gives quarantine priority over later ordinary policy state. It reports the stage,
recipe UUID, exception or verification reason, and observed receipt. Lifecycle replacement is the
only recovery. The first fault remains the root health record for that lifecycle. Later submissions
rejected because the action is already quarantined do not replace it, advance the fault revision,
or emit another warning.

## Validator freshness classification

| Validator or evidence | Class | Boundary treatment |
|---|---|---|
| stable UUID plus exact runtime type | Pure | re-resolve live for every action |
| exact registry and serialized relationship graph | Pure | re-walk live before payment |
| `CraftingRecipeSO.IsVisible()` | Pure | call live before queue/cost work |
| `CraftingInstanceListVariable.HasEmptySpot()` | Pure | call live before payment |
| active and automatic instance membership | Pure | enumerate live before payment |
| queued quantity, preparation, and usage expiry | Pure | read from the exact live Scroll and repeat after the mutation permit immediately before payment |
| `TargetSelectOptions.GetTargeting()` | Pure serialized dispatch | require exact `TargetStructure` |
| `TargetStructure.GetRandomList(ScalingInfo.Basic(level))` | Pure | repeat live immediately before payment |
| monotonic `CraftingRecipeSO.CanBuyAt(level)` frontier | Pure | bounded bracket and binary search live before cost capture |
| `GetTotalCost(0, level).HasEnough()` | Pure | call live and retain exact cost for receipt |
| `GetQuantity()`, `GetTrueSpend(cost)`, decay, and bandwidth evidence | Pure live spend evidence | capture on the main thread immediately before payment; preserve row order, reject unsupported or aggregate-insufficient costs, and predict the native sequential raw post-state |
| action-family ownership permit | Pure process-local authority | capture immediately before payment |
| `PurchaseQuantity` acceptance | Side-effectful | attempt only after all preflights, then verify the native row-ordered raw post-state and progression/admission deltas |
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
rank can select another role. Selection carries an explicit evidence-blocked flag alongside the
role: a default/empty coverage record is not a sentinel, because its enum value also represents
`EvidenceUnknown`. Runtime health names the semantic role and exact evidence reason.
There is no degraded-but-producing state. The engine then waits for a strictly newer publication;
the feature owns no cadence, retry deadline, or candidate memory.

## Remaining runtime validation

Required portable and installed-contract coverage includes the complete static binding surface,
paid fake behavior, semantic policy, F4 blocking, exact preflight reasons, both successful
admission outcomes, every post-payment injected-failure stage, raw receipt evidence, quarantine,
and lifecycle reset. The raw-spend cases include non-default quality, active decay, replenish
without an immediate debit change, a positive debit below `BigDouble` resolution, duplicate rows
whose sequential rounding differs from aggregate subtraction, duplicate rows with and without
sufficient aggregate balance, bandwidth refusal, injected zero/partial/excess debits, cost-row mutation, first-fault preservation,
warning deduplication, and lifecycle replacement.

A disposable-save Unity pass must still:

- correlate published structure/enchantment coverage with the visible game state;
- observe queue and native instant completion at the requested level;
- compare the exact raw resource post-state and `maxStartingLevel` deltas with the receipt, including a
  decay-active Advancement purchase;
- exercise manual one-shot and persistent automatic competition;
- exercise queued quantity, active preparation, pending/engaged use, and their post-permit races;
- confirm empty native target selection blocks production;
- observe cheap and expensive Scroll recipes progress independently despite one shared Scribe ceiling;
- observe the fair cursor eventually selects every visible enabled producible role;
- confirm Runtime shows the exact role/evidence block and post-payment stage;
- confirm a quarantined action preserves one root fault without repeated warnings; and
- cross scene change, save/load, reset, NG+, shutdown, and restart.

Until that pass exists, the metadata result proves compatibility of the declared native shape, not
behavior inside Unity.
