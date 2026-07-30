# Auto Scribe native pipeline

> **Evidence status: implemented against an audited installed build; interactive validation
> pending.** The installed assembly pair is accepted as `steam-windows-2026-07-29`. Exact source
> contracts and installed metadata gates now cover the shared publication, target preflight, and
> one-shot Scribe action. This evidence authorizes the guarded code path when configured, but no DLL
> has been installed and no disposable-save UAT has yet been performed.

[Back to reverse-engineering index](README.md) | [Auto Scribe plan](../plans/auto-scribe.md)

## Observed input

Selected C# was decompiled read-only with ILSpy 10.1.0 from the locally installed Windows build:

| Assembly | SHA-256 |
|---|---|
| `Assembly-CSharp.dll` | `436210E61D9F8B84658609D35E32BC274356170005AC15FE93FA36D4D9F7AA4C` |
| `Assembly-CSharp-firstpass.dll` | `D14D52652591ED3CB5ACF55186478DD3873F3C836871E0F68AA861D1767F480A` |

`origin/main` records this exact pair in `GameAssemblyAudit` and `data/native-contracts.json` under
baseline `steam-windows-2026-07-29`. Compatibility trust and the feature-specific reflected-member
manifest pass against the installed pair.

No binary or save was changed.

## Exact identity candidates

The screenshot exposed only three currently visible recipes. The checked-in entity catalog
contains a wider candidate graph:

| Candidate role | `CraftingRecipeSO` candidate | `ConsumableSO` | `EnchantmentSO` |
|---|---|---|---|
| Advancement | `a4a02a8f-6573-411c-a30c-6d9bcee12605` | `5f6aa08d-7da6-4c7a-89c9-aabcfe48e886` | `0796ee25-e1f6-4c5c-abba-aad46e02318b` |
| Development | `b15690ab-828c-42b9-ad69-70f169a45961` | `09d6101a-460d-4ce9-b7d4-46c4abaeadb7` | `cb354ece-fd8c-4ffc-a67e-b24cc3fe5fa5` |
| Echoing | `008ccaa9-da26-4b55-95a5-5bc5df9c62f0` | `164dbfa9-8b9f-4976-9d17-ad3ad6b07a62` | `d854b177-865f-45ee-97a3-23d904df1ba1` |
| Excellence | `6c5c36ea-4736-46d2-b961-6227d4cce5d3` | `49057abe-fe54-481e-99bc-2b82c3995c6b` | `7b17670e-b3b6-401f-83f7-9c0e6d157852` |
| Investment | Not identified | `da5eab6d-ab4c-4b32-aca1-2e83b6d3a64b` | `f75cea6e-5d21-439f-bce4-79199b22434d` |
| Learning | `49da8d21-0f6a-492e-bd9a-15531b1737d5` | `ec14ee5d-66a3-4b28-a271-25dca2414387` | `b74c2058-4113-4b6c-b11e-1c97304d236c` |
| Power | `9c0a2b96-45fa-4aca-83ba-8efad8895608` | `4bb8af50-fc7d-44a7-b1fc-937c390f8aec` | `b9d5f0f7-43fd-4bad-a8e2-8a73f2f1d1d6` |
| Speed | Not identified | `b2232a7d-5c97-44c9-9520-686e99fa8293` | `9f068bad-f3a0-47de-84f4-407e67622fe1` |

The common recipe type is:

| UUID | Runtime type | Canonical name |
|---|---|---|
| `ee001474-8209-4238-9566-84899a877226` | `CraftingRecipeTypeSO` | `ScribeCrafting` |

Identity mappings alone do not prove serialized relationships. The implementation therefore
enumerates the exact registered recipes and accepts a role only when native references prove one
levelled recipe output, one Scroll-family item, one request-target script, one target structure,
and one matching enchantment graph. Investment and Speed demonstrate why neither recipe existence
nor recipe identity is inferred from a Scroll name.

## Serialized Scribe registry

A read-only AssetRipper 1.3.14 export of the installed build resolved the exact
`ScribeCraftingRecipes` registry to six entries:

| Unity asset GUID | Registered asset |
|---|---|
| `8606cdecd57531f45a29721686cf3f46` | `CraftScrollAdvancement` |
| `97b0b66b7aa06ed41b09fca3963b9619` | `CraftScrollDevelopment` |
| `2485059731ae6f340abef8146fa311dc` | `CraftScrollEcho` |
| `9fdb05cf0be9b6c4d9da94886d285381` | `CraftScrollExcellence` |
| `195a4c0244b73514791414a811f72b59` | `CraftScrollLearning` |
| `554e9363d5830b2489ce5314690e8a42` | `CraftScrollPower` |

This proves that the current Scribe production registry contains exactly these six assets.
AssetRipper could not deserialize the recipe and target graphs because their
`Prerequisites.Container.prerequisites` data uses `SerializeReference`. The lifecycle-bound world
reader closes that static-extraction gap by following the audited runtime references and publishing
completeness evidence. Investment and Speed remain coverage-only unless a future audited baseline
adds and proves a native Scribe production path.

## Level authority

`CraftingRecipeTypeSO` saves two distinct values:

- `startingLevel`: the player's current manual-crafting selector;
- `maxStartingLevel`: the highest unlocked starting level.

`CraftingRecipeTypeSO.SetStartingLevel(int)` clamps the first value to the second.
`CraftingRecipeSO.GetStartingQuantity()` returns the type's `startingLevel` for a level recipe, but
an automated `CraftingInstance` stores its own `quantity`. For `useQuantityAsLevel` recipes,
`CraftingInstance.SetAutomationQuantity(int)` sets that quantity directly and
`GetAutomationQuantity()` returns it.

The product's "highest possible Scroll" therefore maps to the exact Scribe type's
`maxStartingLevel`. Auto Scribe does not need to change the player's `startingLevel`.

## Native Scribe lists and one-shot queue

The Scribe page references two exact list assets from the entity catalog:

| UUID | Runtime type | Canonical name |
|---|---|---|
| `b557060a-e109-40de-9a7d-f2b02bc9766d` | `CraftingInstanceListVariable` | `ActiveScribeInstances` |
| `f6cb65a8-a959-477c-9293-ff66f646c95d` | `CraftingInstanceListVariable` | `AutoScribeInstances` |

The UI's automation click path calls:

```text
UICraftingPage.ContextRecipeClick(recipe)
  -> CraftingInstanceListVariable.AutomateCraft(recipe, delta)
     -> update the existing recipe instance, or
     -> new CraftingInstance(recipe, 1)
        -> SetAuto()
        -> Initiate()
        -> SetAutomationQuantity(delta)
        -> Add(instance)
```

For automatic instances, `CraftingInstance.Initiate()` configures an `EngagedEffectInstance` with
the recipe's resource cost as a drain. Completion effects repeat instead of expiring the instance.
For each supported Scribe recipe, runtime publication requires exactly one audited
`ConsumableGainEffect` output matching the role profile before it publishes the recipe edge.

`CraftingInstanceListVariable` merges automation by recipe. It does not record which caller
contributed a level change. Therefore a feature must not edit an instance merely because its recipe
matches.

The manual queue path is:

```text
UICraftingPage.QueueCraft(recipe, quantity)
  -> recipe.PurchaseQuantity(quantity, existingQuantity)
  -> existing stack.AddQuantity(quantity), or
  -> new CraftingInstance(recipe, quantity)
     -> Initiate()
     -> craftingQueueInstances.Add(instance)
```

Auto Scribe uses the audited non-UI contract beneath this path to submit bounded one-shot work.
It observes player-owned `AutoScribeInstances` as external production pressure but does not
create, edit, or remove persistent automatic instances. This avoids ambiguous cross-restart
ownership and gives emergency stop the same terminal behavior as other ServiceCycle queue
services: already accepted native work may finish, but the suite submits nothing new.

## Scroll inventory is levelled

`ConsumableSO.consumableCounts` is a list of `ConsumableCount`. Each count carries:

- `ScalingInfo si`;
- quantity `qa`;
- free quantity `fr`.

`ConsumableCount.GetLevel()` reads the scaling level. `ConsumableSO.GetStrongest()` selects the
highest level, and `PrepNextUsage()` always fires that strongest count before decrementing it.
Aggregated `ConsumableSO.GetQuantity()` is insufficient for Auto Scribe demand accounting; the
shared world publication needs the levelled counts.

The created `ConsumableUsage` receives both effective and base scaling from the selected count.
The world reader publishes `baseSi.GetLevelInt()` under installed contracts, so policy reserves an
in-flight use only when its exact level is the requested level or higher.

## Higher enchantments replace lower ones

Every `StructureSO` owns an `EnchantmentSO.EnchantTable`. The table:

1. finds an existing instance by exact `EnchantmentSO` reference;
2. keeps the existing instance when it is stronger than the proposed instance;
3. otherwise removes the old persistent effects and list entry;
4. adds and applies the proposed instance.

`EnchantmentInstance.IsStrongerThan(other)` compares the integer scaling level with `>`.
`EnchantTable.CanUpgradeEnchantment(enchantment, scaling)` accepts an absent enchantment or a
strictly stronger proposed instance. Equal and lower levels do not upgrade.

This is the native basis for one deficit per role per structure. Auto Scribe must observe the table;
it must never call `AddEnchantment` itself.

## Native Scroll target selection

The Auto Items dossier already establishes that randomized Scroll use delegates to
`RequestTargetEffectScript`. For a random context it asks its serialized `TargetSelectOptions` for a
target and cancels when none exists.

The candidate build's `Targeting.TargetStructure`:

- filters to visible structures satisfying its serialized `StructureCondition`;
- asks its serialized `EnchantmentMatcher` whether every listed enchantment can upgrade;
- when the matcher is non-empty, sorts valid structures by enchantment separation and chooses the
  most deficient target;
- reports no valid target when the filtered set is empty.

This native "random" targeting distributes a supported Scroll toward a valid deficient structure.
The collector accepts only an exact target-selection shape with an unambiguous matcher.

Auto Items now consumes the shared coverage directive and repeats the exact native
`TargetStructure.GetRandomList` check with the live strongest Scroll level immediately before use.
A surplus or obsolete Scroll is therefore blocked when no upgrade target remains.

## Remaining runtime validation

The implementation now rejects unsupported multi-target or ambiguous graphs, publishes levelled
owned/pending/queued/automatic supply, revalidates exact native candidates at action time, and
verifies queue or instant-stock postconditions. The source-contract scan and installed metadata
suite cover every reflected field, method, type, and the `CraftingInstance(CraftingRecipeSO,
BigDouble)` constructor.

What remains is interactive evidence on a disposable save: correlate published coverage with the
visible enchantment table, observe both queued and instant completion, exercise manual and
persistent automatic competition, verify no-candidate Scroll blocking, and cross save/load, reset,
NG+, scene replacement, shutdown, and restart. Any disagreement fails closed and must update this
dossier before release.
