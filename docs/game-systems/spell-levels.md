# Spell levels

A spell's level is bought with resources and raises **both** what the spell produces and how much XP
it generates per cast. E.g., the first level of one starter spell moved its yield from 2.02 to 2.23
per cast, about +10.4 %.

That double effect is why levelling compounds: more output now, more XP per cast, therefore faster
mastery, therefore the mastery-gated purchases and the cross-run yield arrive sooner.

## The level cost curve

Level cost is the spell's authored base cost list multiplied by a single scaling factor and then
rounded to two significant digits:

```
cost factor at level L = 3.3 ^ (L × (1 + 0.007 × L))
```

The `3.3` is a stacking multiplier and the `0.007` a diminishing exponent, both authored. E.g., at
level 24 the factor is `3.43e14`, so a base cost of `9` Knowledge prices that level at `3.1e15`.

Spells draw their level costs from **different resources**, so per-spell affordability is genuinely
independent: being broke in one currency does not stall the whole spellbook.

## Level and mastery are one ladder

The spell card's "Lv N" and the requirement rows' "mastery N/M" are two labels on the same per-spell
ladder. Casting fills the spell's mastery XP; once the XP threshold is met the level purchase
unlocks; paying it advances the level. A spell level therefore always costs both the casts and the
resources, in that order.

## Levelling every spell at once

An upgrade unlocks a **level-all** button, and it is not all-or-nothing:

- It walks every discovered spell recipe in turn.
- For each one it levels repeatedly while that spell can level and its next level cost is affordable.
- **An unaffordable spell skips only that spell.** The walk does not stop, and later spells are not
  blocked by an earlier one it could not pay for.

So the button drains whatever it can afford across the whole spellbook in one press, in enumeration
order.
