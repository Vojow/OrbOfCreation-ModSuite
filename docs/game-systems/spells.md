# Spells

Casting is the game's first *active* production loop: resources that have no passive rate exist only
because you cast for them.

Every spell has a **cost**, a **cooldown** and an **effect list**. E.g., the first spell of a run:

| Gather Knowledge, Lv 1 | Value |
|---|---|
| Types | Primary, Divining, Cantrip |
| Cost | 100 mana |
| Cooldown | 7.35 s |
| Effect | +2.01 Knowledge per cast |

Casting is available from the Spellbook and from the hotbar. Hotbar and keyboard casting do not
require the Spellbook screen to be open, so casting is never gated behind having the right tab
visible. Some spells open a **target prompt** when cast and resolve only once you pick a target.

Displayed cost is not the amount debited; see [cost-pipeline.md](cost-pipeline.md).

## A spell's effect list is run state

A whole class of upgrades does not buff a number — it **appends a row to another entity's effect
list**. E.g., "Improve Whirling Sorcery" turned a single-effect charm into a two-effect charm. Some
later spells are designed to do nothing but this.

So two saves can hold the same spell at the same level with different effects, and a spell's printed
description is not a static property of the spell.
