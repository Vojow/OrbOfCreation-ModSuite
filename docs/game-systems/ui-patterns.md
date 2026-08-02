# Interaction patterns

Every page in the game is one of these patterns, or a purchase list with a dial bolted on. Learning
them makes a page you have never opened readable on sight: you already know where the cost line is,
what turns red, and what the button at the bottom does.

| Pattern | Shape | Where it appears |
|---|---|---|
| Discovery surface | one buy-the-next-thing purchase, then a choice among rolled options | every discovery tree |
| Budgeted loadout | a `used/total` budget, a per-item price against it, red for what will not fit | spell, artifact and alchemy loadouts |
| Level dial plus raise-cap purchase | a `Lv n/max` stepper with a purchase beside it that raises the max | Output Lv, Reserve Lv, Alchemy Lv, the Scribe's Starting Level |
| Purchase list | a scrolling list of levelled rows with a progression counter in the header | every discipline subtab |
| Manual craft rows | a recipe list, each row carrying its full cost line and a craft action | Workshop > Crafting |
| Timed job | a point cost per school, a duration, and an output; starting it runs a timer and drains a resource | Research |
| Mastery confirm | cost lines plus parallel XP tracks that fill from casting, not from spending | Spellbook > Spells |

Three rules cut across all of them:

- **Dials are two-way.** You can tune a level dial *down* at any time; the purchase beside it moves
  only the ceiling.
- **Budget pages preflight their own fit**, so a red number on a loadout means "this will not go in",
  not "this looks expensive".
- **A budgeted loadout can have more than one binder**, and either can be the one that is full.
