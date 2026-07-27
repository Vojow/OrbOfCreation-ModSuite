# In-game vocabulary

[Back to index](README.md) · [Entity catalog](entity-catalog.md) · [Evidence strength](evidence-strength.md)

The words the game shows a player are not the words the code uses, and in one case they are
actively misleading: the class named `StructureSO` is called an **Attribute** on screen, and
the thing a player calls an Attribute has nothing to do with `AttributeSO`.

This matters because anything authored from observation — milestone tables, embargo lists,
curated priorities — arrives as screenshots and spoken descriptions in player vocabulary and
has to land on the right managed type. Getting the translation wrong silently targets the
wrong 200 entities.

## The translation

| Managed type | What a player calls it | Where they see it |
|---|---|---|
| `StructureSO` | **Attribute** | levelled things: Cauldron, Machinery, Hydro Aura |
| `StructureTypeSO` | Attribute **category** | Alchemist, Swift, Grove, Workshop |
| `UpgradeSO` | **Upgrade** | the "Upgrades" screen; one-shot purchases |
| `AttributeSO` | **Statistic** | the "Statistics" tab |
| `ResearchSO` | Research | the "Research" screen |
| `ResourceSO` | Resource | the resource bar |

Read the first two rows carefully. `StructureSO` and `AttributeSO` both translate to words
that sound like the other one, so "attribute" in a player's sentence almost always means
`StructureSO`, and almost never means `AttributeSO`.

## Evidence

All of it is `SerializedAssetVerified` — read out of the game's own serialized assets, not
inferred from naming.

**`StructureSO` is an Attribute.** `StructureTypeSO.GlobalStructureType` has
`displayName = "All Attribute"`, and every other `StructureTypeSO` describes itself as a
group of attributes — `AlchemistStructures`: *"Attributes related to your ability to
transmute and create."* Individual structures do the same: `Determination`: *"Further
improves swift attributes."*; `Upheaval`: *"Reduces the cost of all attributes."* The
`ActiveActionables` list, which holds structures under development, has
`displayName = "Active Attributes"` and the description *"The attributes you are currently
developing."*

**`UpgradeSO` is an Upgrade.** The `ViewSO` with `displayName = "Upgrades"` declares
`relevantLists = ["AllUpgrades"]`, and `AllUpgrades` is an `UpgradeListVariable`. The view
is bound to the type directly, so this is not a naming coincidence.

**`AttributeSO` is a Statistic.** 110 of 201 `AttributeSO` records carry
`displayTypeRef.displayType = "Statistic"` (the rest are `Information` and `Action`, both
also non-purchasable), and the `ViewSO` named `PlayerStatsAttributes` displays as
**"Statistics"**. `AttributeSO` has no cost and no prerequisite fields — nothing here is
ever bought, so it is never a purchase target.

## Resolving a name to an identity

Display names are for human authoring only. Per
[evidence strength](evidence-strength.md), they are diagnostics, are absent from the bounded
source mask, and can never upgrade evidence or authorize a mutation — a name resolves to a
UUID once, at authoring time, and the UUID is what gets stored.

`data/entity-display-names.tsv` holds the labels; resolve with:

```bash
tools/find-entity.py "Improved Alchemy" --costs
```

Display name alone is not a key — 152 labels are shared. Display name **plus managed type**
effectively is: `ResourceSO`, `StructureSO`, `UpgradeSO`, and `ResearchSO` contain no
within-type collisions at all. Since the translation table above turns a player's word into
exactly one managed type, a screenshot label plus the screen it came from resolves uniquely.
The 18 within-type collisions split 12 `ViewSO`, 5 `AttributeSO`, and 1 `PlotNodeActionSO`.
None of those three types is purchasable, so none of the 18 is ever a strategy target — but
note that two thirds of them are screen labels, including "Upgrade" itself, which names three
separate `ViewSO` assets. A screen label resolves through the table above, not through the
display-name file.

## Open

The table is derived from serialized assets rather than from the running UI. It has not been
checked against a live game, so a screen whose label is composed at runtime rather than
stored on the `ViewSO` would not appear here. Confirm against the actual screens when
convenient; the `StructureSO` → Attribute row is the one worth a deliberate look, since it is
the row that inverts intuition.
