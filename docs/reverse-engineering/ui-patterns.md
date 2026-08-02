# Interaction patterns

[Back to index](README.md)

Every page in the game is one of these patterns, or a purchase list with a dial bolted on. For
anything modelling the interface — captures, navigation, tooling — the pattern tells you where the
cost line is, what turns red, and what the bottom button does, before the screen has ever been
opened.

| Pattern | Shape | Where it appears |
|---|---|---|
| Discovery surface | one buy-the-next-thing purchase, then a choice among rolled options | every discovery tree |
| Budgeted loadout | a `used/total` budget, a per-item price against it, red for what will not fit | spell, artifact and alchemy loadouts |
| Level dial plus raise-cap purchase | a `Lv n/max` stepper with a purchase beside it that raises the max | Output Lv, Reserve Lv, Alchemy Lv, the Scribe's Starting Level |
| Purchase list | a scrolling list of levelled rows with a progression counter in the header | every discipline subtab |
| Manual craft rows | a recipe list, each row carrying its full cost line and a craft action | Workshop > Crafting |
| Timed job | a point cost per school, a duration, and an output; starting it runs a timer and drains a resource | Research |
| Mastery confirm | cost lines plus parallel XP tracks that fill from casting, not from spending | Spellbook > Spells |

The rules each pattern implies (two-way dials, budget preflights, dual binders) are documented on
the owning [game-systems](../game-systems/README.md) pages; this table is the map, not the rules.
