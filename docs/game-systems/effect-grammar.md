# The effect grammar

Every effect in the game — on spells, attributes, upgrades, runes, glyphs — reads as three parts:

```
<term> <statistic> <keyword-target>
```

- **Term** — how it combines: the modifier kinds in [modifiers.md](modifiers.md).
- **Statistic** — what it changes: power, cooldown, cost, capacity, gain, effect levels, and many
  more.
- **Keyword-target** — what it applies to: one named entity, a category keyword ("all Cantrips", "all
  Divining"), or something as broad as "all" or "all capped".

Because the target is a keyword, "what helps this thing" is derivable rather than a matter of taste:
enumerate the entity's effective keywords and read which effects name them.

The corollary is that a buff's value is a function of your **current portfolio**. A Primary-targeted
buff that was excellent when your only spell was Primary is worth much less once your workhorse is
Expansion, and a category buff doubles its coverage the instant you own a second member of that
category.

Tooltips nest, so a term inside a tooltip can be inspected for its own tooltip — which is how you
find out that a category is a type and which statistics hang off it.
