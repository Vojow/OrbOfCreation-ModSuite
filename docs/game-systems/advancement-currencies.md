# Advancement currencies

When a progression tab levels it does **not** grant one unit of an advancement currency. It adds `+1`
to the **maximum quantity** of the currencies it feeds, so a currency reading `2/2` becomes `2/3`.
**Nothing is wasted at cap**, and there is therefore no urgency to spend an advancement point before
the next tab level lands. Quantity/cap reads as unspent/earned; see
[allocations.md](allocations.md).

| Progression tab | Advancement maximums it raises, per level |
|---|---|
| Wizardry | +1 Magical, +1 Glyph |
| Scholar | +1 Cognitive, +1 Technology |
| Alchemy | +1 Cognitive, +1 Materials |
| Artificer | +1 Ability, +1 Equipment |
| Construction | +1 Technology, +1 Equipment |
| Druid | +1 Ability, +1 Magical |
| Mystic | +1 Technology, +1 Materials |
| Shaper | +1 Technology, +1 Glyph |
| Orb | +1 Orb |

Maximums are also raised **outside** that table: resource-type levels and various named upgrades
target the same maximums — e.g., Technology Mastery adds +2 Technology, and Boost Glyphs adds +50 %
to Glyph. Because these are modifiers, the effective cap is their folded result rather than a stored
integer.

## The supply is run-finite

There is no steady income. Advancement points come from tab levels, tab levels come from completed
attribute levels, and attribute costs grow faster than production does. At some point in every run
attributes become unaffordable, the tabs stop levelling, and the supply simply stops.

That makes the layer an **allocation problem with a hard budget**. Only a persistent reset puts the
cost curves back to zero; see [reset-and-ng-plus.md](reset-and-ng-plus.md).
