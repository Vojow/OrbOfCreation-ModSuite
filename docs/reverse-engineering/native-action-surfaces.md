# Native action surfaces

[Back to index](README.md)

Three action paths are decompiled: purchasing, consumable use, and crafting. Each is given as the
call sequence, the admission short-circuit in its real order, the stable identities involved, and
the constraints that make a naive call fail.

The costs these paths charge are in [resources-and-bigdouble.md](resources-and-bigdouble.md).

---

## Purchasing structures and upgrades

### Surface

| Concern | Structure | Upgrade |
|---|---|---|
| Registry | `StructureSO.All` | `UpgradeSO.All` |
| Availability | `IsAvailable()` | `IsAvailable()` |
| Admission | `CanPurchase()` | `CanPurchase()` |
| Cost | `GetPurchaseCost()` → `ResourceCostList` | `GetPurchaseCost()` → `ResourceCostList` |
| Current level | `GetPurchaseLevel()` | `GetPurchaseLevel()` |
| Queued state | `GetQueuedQuantity()` | `GetQueuedPurchaseLevel()` |
| Mutation | `Purchase(bool)` | `Purchase()` |
| Queue signal | `QueueBuild(int)` | `Purchase()` |
| Completion | `CompleteAction()` | `CompleteAction()` |
| Finite lifecycle | — | `HasFiniteLevels()`, `IsMaxLevel()`, `IsMaxQueuedLevel()` |

`QueueBuild(int)`, `Purchase()`, and `CompleteAction()` are the three viable Harmony targets on
this path: the first two are where queue state is mutated, the third is where it settles.

### `CanPurchase()` does less than its name suggests

This is the most misread member on the surface. It folds in **live requirements and queue
admission** — and neither availability nor cost.

```text
Structure:
    IsAvailable()  = prerequisites.Check()
    CanPurchase()  = prerequisitesPerLevel.Check(ConditionInfo(quantity))
                     && queue.HasRoom()

Upgrade:
    CanPurchase()  = !IsMaxQueuedLevel()
                     && purchaseCost.HasEnough()
                     && !IsMaxLevel()
                     && prerequisites.Check()
                     && prerequisitesPerLevel.Check(ConditionInfo(level + queuedLevels + 1))
                     && queue.HasRoom()
```

The orders matter because they short-circuit: an Upgrade that is at its queued maximum never
evaluates cost, and a Structure never evaluates cost at all. Availability and affordability are
your job on the Structure path.

Note that both `prerequisitesPerLevel` checks take the level as an argument — the level a purchase
would *reach*, which is `quantity` for a structure and `level + queuedLevels + 1` for an upgrade.
That is the one admission term that cannot be answered from a snapshot taken earlier.

A player never meets any of this, because the buy button runs its own preflight — per-level
prerequisite, affordability, queue room — and renders the cost line red instead of firing. Red is
the interface's check, not the data layer's. Driving the data layer directly, an attribute purchase
that cannot be paid for silently does nothing: admission passes (cost is untested there), and the
purchase's own per-level payment commits zero levels — no error, no queue entry, no message.

### Diagnosing a refusal

When `CanPurchase()` says no, decompose it by asking the game the readable terms separately:
`IsAvailable()`, `IsMaxLevel()`, `IsMaxQueuedLevel()`, and `GetPurchaseCost().HasEnough()` — the
game's own verdict on the price, not a re-pricing.

If only `HasEnough()` refuses, quantities moved after you read them; that is ordinary staleness.
If availability or a level cap refuses, your model of the entity is wrong. **If every readable
term passes, the parameterized per-level prerequisite is the refusing term by elimination** — it
is the only one you cannot read directly, so this is how you name it.

### The owning-view reachability chain

Player-facing reachability is a property of the owning `ViewSO`, not of the item: a natively
purchasable entity can sit inside a tab whose view is unavailable, and item-only reads cannot see
that gate. The authored ownership edge is indirect:

```text
candidate exact UUID + exact native type
  → exact StructureListVariable / UpgradeListVariable membership
  → ViewSO.relevantLists or ViewSO.availableLists
  → exact owning ViewSO
  → ViewSO.IsAvailable()   (driven by ViewSO.prerequisites)
```

For a structure, the resolver additionally proves `StructureSO.structureType` is an exact
`StructureTypeSO` containing that exact structure reference exactly once in its private
`structures` list. Both view list fields participate; the same view/list route appearing in both
collapses to one; different matching routes are ambiguous. A missing, unreadable, ambiguous, or
contradictory route is a named status, not an empty gap. The proving incident: `ConstructionAura`
(`6a361a01-8405-4fbc-9af1-42f471911d9e`) present and purchasable in the `ArtificerStructures` list
(`2c3b16bc-1eb4-4382-9d93-0d20f81f07a9`) while its owning `WorkshopArtificer` view
(`b8ebce37-ba04-42bc-b36d-63f7a7766a21`) was unavailable — the item itself carried no tab
prerequisite.

The authored route shapes: most content carries two. Either a parent tab and its subtab show the
same list (the *Witchcraft* attribute sits in a single Wizardry list shown by both Magic and
Wizardry), or a candidate sits in an aggregate list and a screen list at once (a Wizardry upgrade
in both the all-upgrades aggregate and Magic's own upgrade list — aggregates are legitimate routes,
not summaries). The persistent right-panel Upgrades / Inventory strip is genuinely global and
always available, a route in its own right. Single-route content is where things go missing:
*Life Weaver* is reachable only through World > Druidry, because World does not co-reference its
subtabs the way Magic co-references Wizardry.

### Making the mutation

`StructureSO.Purchase(true)` forces exactly **one** level and consults no multiplier;
`UpgradeSO.Purchase()` honours the global multi-buy. The same intent therefore produces two
shapes: a bulk structure buy is **N calls**, re-checking `CanPurchase()` before each level past
the first; a bulk upgrade buy is **one call** with `GlobalVariables.GetMultiBuy()` set to N and
restored afterwards (protocol in [modding-hooks.md](modding-hooks.md)). Both accept a queued delta
in `[1, N]` — either can commit fewer levels than asked for, any committed level is a success, and
only a zero delta is a miss.

### Where an upgrade's reward lands

An `UpgradeSO` can carry authored `viewListAdditions`: `ViewListVariable.ListTuple` values whose
inherited `list` and `element` fields `UpgradeSO.ApplyListAdditions()` applies. Neither
`CanPurchase()` nor `Purchase()` inspects that destination, so a full one is admitted and charged
like any other purchase.

Traverse every tuple before paying. A tuple targeting an unbounded list needs no gate; a
capacity-bound one must match an audited exact list/max identity pair and answer live
`HasEmptySpot()`. The audited pair is the world-aspect destination `CreatedWorldAspects`
(`74ec1f90-e94c-4cd7-a1d0-7b35016b57ff`), whose `AbstractListVariable<ViewSO>.maxSizeVariable` is
the `WorldAspectSlots` `IntVariable` (`4b1bb2de-723a-4360-827c-8e4483f3ff8d`). An unknown pair, a
malformed tuple, an identity contradiction, or a full destination refuses before payment. The rule
has an origin: an aspect bought against zero free pedestals without this gate made the game
manifest an extra slot, and a later slot grant left one permanently empty.

### Queue and completion

Queue authority is `ActionManager.GetRemainingRoom()` together with
`ActionManager.instance.actionableItems.maxQueuedItems.AsInt()`. Read them as a pair immediately
before acting; capacity grows through ordinary progression without any lifecycle boundary firing,
and `Player.GetBulkDevelopment()` is a separate value that only shapes grouping.

Payment lands at queue time, not completion: the game prices the levels it is queueing at that
moment and charges as it queues, so queueing a large group spends its whole sum at once.

**The upgrade path never checks room per level.** One committed level is one queue slot —
`UpgradeSO.Purchase()` loops the multi-buy multiplier and `QueueAction` stacks once per committed
level — and that loop consults `GetRemainingRoom()` nowhere; its only per-level term is a
`HasEnough()` that breaks the loop once the next level is unaffordable. A caller holding slots back
must clamp its own request to `remainingRoom - reservedSlots`: with room 5, a reserve of 3 and a
request of 4, the admission check passes, four levels queue, and one slot is left. The structure
path is one slot per call, because `Purchase(true)` queues exactly one level.

A completion can unlock unrelated content — including content in the *other* registry. So
`CompleteAction()` returning invalidates more than the entity that completed, and a completion
postfix should trigger a broader re-read rather than a targeted one.

### Prerequisite breadth

Before traversing a prerequisite graph, know how wide it is. `Prerequisites.Container` holds
nested AND/OR graphs over conditions on Upgrades, Structures, Research, Resources, Numbers, Lists,
Views, Spells, Alchemy, Rituals, Equipment, prerequisite links, and generic upgradeable values.
Measure the subtypes actually attached to your candidates from the serialized assets; IL proves
only which are possible.

---

## Using a consumable

### Family is a set, not a subtype

There is no per-family class. Family is membership in `ConsumableSO.consumableTypes`, a set of
`ConsumableTypeSO` references:

| Family | UUID | Native name |
|---|---|---|
| Fruit | `46e0ab83-df7c-4f35-8012-3d9a3c97b753` | `Fruit` |
| Potion | `8103dae4-6945-4d18-b562-d2ffcd7ef49e` | `Potion` |
| Relic | `5d27b76e-eed3-49cc-a069-b9106000ede4` | `Relic` |
| Scroll | `70b36536-64e5-4f70-ad6f-af5787d719cc` | `ScrollConsumable` |
| Thread | `66a50127-5210-4a3a-93f4-952287858b90` | `ThreadConsumable` |

The set is not a discriminator, but an item's *operation* is single. The asset census finds 68
authored consumables across exactly eight membership patterns. In every multi-membership pattern
the second family records an acquisition channel or category — Fruit, Treasure, Modification,
Resource, Food — rather than a second operation, so a treasure relic and a fruit relic are both
just Relics. The only two-operation topology authored is the four permanent fruits (Blitz Berry,
Continuous Coconut, Frugal Fig, Power Pear) carried as both `Fruit` and `Relic`; they behave as
Relics. Encode that as the exact authored set — do not invent a general precedence rule for
combinations the game does not contain.

### Call sequence

```text
ConsumableSO.SelectAndFire()
  → CollectQuantity(GlobalVariables.GetMultiBuy())
    → CanFire()
    → consumeCost.PerformCost()
  → queuedQuantity++
  → Inventory.QueueConsumable(this)
  → an idle inventory begins preparing the queued item
```

Payment happens inside `CollectQuantity`, before the queue increment — so the observable edge of a
successful submission is stock −1 and queued quantity +1. Everything after that (preparation,
random target selection, the actual effect) is game-owned and is not evidence that the effect
landed.

`Inventory.CanUseConsumable()` is a **global** single-preparation gate: one item prepares at a
time, across every family, so any pending preparation refuses every other submission.

### Scrolls target structures

A Scroll needs `canBeRandomized`, then `SetRandomization(true)` confirmed by `IsRandomized()`. Its
authored graph contains exactly one `RequestTargetEffectScript`; that script's
`TargetSelectOptions.GetTargeting()` resolves to `Targeting.TargetStructure`, and
`TargetStructure.GetRandomList(ScalingInfo.Basic(level))` computes candidates against the
strongest owned level — filtering visible eligible structures, applying the serialized enchantment
matcher, and ordering deficient targets. **An empty list is a normal outcome**, meaning the Scroll
would not upgrade anything; it is not an error and not a reason to enter the manual targeting
branch.

### `Gain()` admission runs after payment

`ConsumableSO.Gain()` first clamps its positive incoming amount to `maximumCarryLoad` — a
non-positive carry limit therefore guarantees the entire gain is lost. At capacity, "weakest" is
defined by level only: an incoming level strictly above the weakest owned unit replaces it; an
equal level replaces a unit without changing level coverage; a strictly weaker level is
decremented to zero and silently dropped. Native `UICraftingPage.QueueCraft` pays
`CraftingRecipeSO.PurchaseQuantity` before construction, and instant and queued completion run the
identical `ConsumableGainEffect → ConsumableSO.Gain()` path — so the capacity decision always
happens after payment.

### Levelled stock

`ConsumableSO.consumableCounts` is a list of `ConsumableCount` rows, each with `GetLevel()` and
`GetQuantity()`. Aggregate quantity is the wrong question: use selects the **strongest** count and
carries that count's scaling through the effect. Read `GetStrongestLevel()`, `GetStrongest()`, and
`GetCountScalingInfo()` instead of summing.

---

## Harvest availability

Plot and action availability on the Agromancy screen is cached behind the interface: the plot
list's own render pass is what re-evaluates it, and a passed check latches on rather than being
re-tested. Growth and timers advance with the screen closed; only the availability evaluation is
screen-bound. An actually-available action can therefore read unavailable until the screen has
been opened once — the reason a published prerequisite latch is evidence only, and the exact
current action still gets one live validation at the action boundary.

---

## Crafting and scribing

### Identities

The recipe type is `ScribeCrafting`, `ee001474-8209-4238-9566-84899a877226`, a
`CraftingRecipeTypeSO`. The registry is `ScribeCraftingRecipes`,
`2917516f-34a5-47b7-85b2-0b2f9ab3a29f`, a `CraftingRecipeListVariable`, holding exactly six
serialized recipes: `CraftScrollAdvancement`, `CraftScrollDevelopment`, `CraftScrollEcho`,
`CraftScrollExcellence`, `CraftScrollLearning`, `CraftScrollPower`.

Each role is a recipe / Scroll / enchantment triple:

| Role | Recipe | Scroll | Enchantment |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |
| Excellence | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` |
| Development | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` |
| Echo | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` |
| Investment | no audited recipe | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` |
| Speed | no audited recipe | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` |

Investment and Speed are real Scroll and enchantment identities with no recipe in the registry —
the code-side half of the open question in
[`game-systems/open-questions.md`](../game-systems/open-questions.md). Do not
infer a production path for them from the existence of the identities.

A triple is proved by edges, never by names: exactly six exact `CraftingRecipeSO` values in the
registry, exactly one `ScribeCrafting` type reference on the recipe, `useQuantityAsLevel = true`
alongside `CraftingRecipeTypeSO.isLevelType = true`, exactly one `ConsumableGainEffect` output
naming that role's Scroll, exactly one `RequestTargetEffectScript` on the Scroll behind an exact
`TargetStructure` selector, and exactly one `EnchantItemScript` naming the role's enchantment. A
missing, extra, wrong-typed or contradictory edge leaves the relationship unknown; it never
degrades to a name-based mapping.

### There is no non-UI one-shot composite

The only entry point is re-driving the data layer under `UICraftingPage.QueueCraft`; the sequence
and its consequences are in [ui-internals.md](ui-internals.md).

### Level authority

`CraftingRecipeTypeSO` saves two distinct values: `startingLevel`, the manual-crafting selector,
and `maxStartingLevel`, the highest unlocked starting level.
`CraftingRecipeSO.GetStartingQuantity()` reads the **former**, while a `CraftingInstance` carries
its own `quantity`. Supply the level to the instance; do not write `startingLevel`.

Payment and progression are one composite:

```text
CraftingRecipeSO.PurchaseQuantity(purchasedQuantity, previousQuantity)
  → pay GetTotalCost(previousQuantity, purchasedQuantity)
  → CraftingRecipeTypeSO.SetMaxStartingLevel(purchasedQuantity + previousQuantity)
```

`SetMaxStartingLevel` keeps the **larger** of the old and proposed values, so for a one-shot
purchase with `previousQuantity = 0` the transition is `max(before, purchasedLevel)`. That ceiling
is shared across recipes; a cheap recipe raising it does not advance any other Scroll, whose own
frontier is its `maxCreatedLv`.

`CraftingRecipeSO.CanBuyAt(BigDouble)` is **monotonic in level** — bracket the first unaffordable
level and binary-search it rather than probing linearly.

### Instances and enchantments

Two `CraftingInstanceListVariable`s hold the live work: `ActiveScribeInstances`
(`b557060a-e109-40de-9a7d-f2b02bc9766d`) and `AutoScribeInstances`
(`f6cb65a8-a959-477c-9293-ff66f646c95d`). The game's own automation creates or updates one
repeating instance per recipe in the second; neither list retains caller ownership, so those
instances are observable but not editable by anyone else.

Every `StructureSO` owns an `EnchantmentSO.EnchantTable`. A native enchantment upgrade keeps a
stronger existing instance and replaces an equal-or-lower one only when the proposed scaling is
**strictly** stronger under `CanUpgradeEnchantment`. An apply that appears to do nothing usually
proposed equal scaling.
