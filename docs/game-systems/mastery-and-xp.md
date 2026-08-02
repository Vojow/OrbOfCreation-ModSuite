# Mastery and XP

Most of the game can be bought. Mastery cannot. It is a per-spell experience track that fills **only
by casting**, and it sits in front of purchases that no amount of saving will unlock. This page covers
mastery, the spell-type XP tracks behind Confirm Mastery, spell levels and their cost curve, and the
knob that makes mastery accrue faster.

Casting Level — the third, global XP track — lives on
[casting-and-spells.md](casting-and-spells.md#casting-level).

## Mastery is an earned-by-doing gate

Each spell has its own mastery XP bar. Casting that spell fills it; nothing else does.

Requirement rows read `Req. <spell> mastery <have>/<need>`, and they appear early. The tutorial
specimen is **Novice Spells** (15 Knowledge, `Req. Gather Knowledge mastery 0/1`) — a purchase you can
afford long before you are allowed to make it. Mastery gates keep appearing afterwards, in three
recognisable shapes:

- **Unlock gates** — Novice Spells needs Gather Knowledge mastery.
- **Per-spell upgrade lines** — "Improve Whirling Sorcery" needs Whirling Sorcery mastery 1/1.
  **Mastering a spell is what unlocks that spell's upgrade line at all.**
- **Conjunctive gates** — Scholarism needs Gather Knowledge mastery 1/2 *and* Output Lv 1/2.

The planning consequence is that time-to-milestone is not only a resource question. A gated purchase
is reached by *scheduling casts*, and a loadout that cannot cast the gating spell cannot progress
toward the gate at all.

## The readiness threshold

Mastery XP requirements escalate steeply. The authored curve is decompiled and exact — with
`masteryReqBase = 600` and a spell-mastery XP scaling of `MultiStacking(factor 16.62)` plus
`Reduction(0.4)`, the XP needed at mastery level `L` is:

```
600 * 16.62^L / (1 + 0.4 * L)
```

Applied to an eighteen-spell late-run save, that formula reproduced the game's own ready / not-ready
split exactly (fourteen ready, four not), so it is not an approximation.

The escalation in practice: Gather Knowledge's first level needs **600 XP**, the next needs **7.12e3**
— roughly a twelvefold step, and it keeps going.

XP per cast is not flat. The game's own tooltip states that XP generated is based on **cost, speed and
level**, which is why a levelled spell feeds its own mastery faster than an unlevelled one and why
cheap spam is a poor mastery engine.

## Confirm Mastery and the spell-type tracks

The Spells page's **Confirm Mastery** action is gated on **three parallel spell-type XP tracks**, one
per type the spell carries. Observed for Gather Knowledge (Primary / Divining / Cantrip):

| Track | Requirement |
|---|---|
| Primary | 0/300 |
| Divining | 0/200 |
| Cantrip | 0/500 |

**One cast feeds all three**, because the spell carries all three types. The tracks are per type, not
per spell, so casting any Cantrip advances the Cantrip track for every spell that needs it — a loadout
whose spells share types converges its tracks much faster than one built from disjoint tags. Which
types a spell actually carries can be changed by glyphs; see
[glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md).

Confirm Mastery also has a **resource cost**, rendered red when unaffordable, alongside the three
tracks.

## Spell levels

A spell's level is bought with resources and raises **both** what the spell produces and how much XP
it generates per cast. The first level of Gather Knowledge (10 Knowledge) moved its yield from 2.02 to
2.23 Knowledge per cast, about +10.4 %.

That double effect is why levelling compounds: more output now, more XP per cast, therefore faster
mastery, therefore the mastery-gated purchases and the cross-run yield below arrive sooner.

### The level cost curve

Level cost is the spell's authored base cost list multiplied by a single scaling factor and then
**rounded to two significant digits**:

```
cost factor at level L = 3.3 ^ (L * (1 + 0.007 * L))
```

The `3.3` is a stacking multiplier and the `0.007` a diminishing exponent, both read from the authored
`SpellLeveling` data. Worked examples from a late-run save:

| Spell | Level | Factor | Cost |
|---|---|---|---|
| Create Spring | 24 | 3.43e14 | Knowledge `9 × factor` = 3.1e15; Water `0.05 × factor` = 1.7e13 |
| Dense Expansion | 25 | 1.70e15 | Space `22 × factor` |
| Arcane Aura | 26 | 8.61e15 | Space `30 × factor` |
| Conjure Space | 27 | 4.42e16 | Space `15 × factor` |

Note that spells draw their level costs from **different resources**. Per-spell affordability is
genuinely independent — being broke in one currency does not stall the whole spellbook.

### Level and mastery are one ladder

The spell card's "Lv N" and the requirement rows' "mastery N/M" are **two labels on the same
per-spell ladder**. Casting fills the spell's mastery XP; once the XP threshold above is met, the
level purchase unlocks; paying it advances the level — which is why the decompiled level-cost lookup
is taken at `masteryLevel + 1`. A spell level therefore always costs both the casts and the
resources, in that order.

Do not confuse this per-spell ladder with the global one: **Casting Level** is fed a little by every
cast of any spell and is what gates `Raise Output Lv` purchases — a game-wide counter, not a
per-spell one. See [casting-and-spells.md](casting-and-spells.md#casting-level).

### Levelling every spell at once

An upgrade unlocks a **level-all** button. Its behaviour is worth knowing exactly, because it is not
all-or-nothing:

- It walks every discovered spell recipe in turn.
- For each one it levels **repeatedly** while that spell is discovered, can level, and its own next
  level cost is affordable.
- **An unaffordable spell skips only that spell.** The walk does not stop, and later spells are not
  blocked by an earlier one it could not pay for.

So the button drains whatever it can afford across the whole spellbook in one press, in enumeration
order, rather than stopping at the first spell it cannot buy.

For maintainers: the single-level path is asymmetric with the batch one. The underlying single level
purchase checks **only readiness** — not discovery, not affordability. Those two are enforced by the
UI row (visibility from the spell being discovered, affordability by the cost button) rather than by
the purchase itself.

## What mastery pays out

**Each mastery level grants +1 Time Advancement on your next run.** Mastery is therefore not only a
gate-opener but the main way casting turns into cross-run capital: Time Advancements are the currency
you spend on time runes at the start of the following run, and they are refunded on every reset. See
[time-and-prestige.md](time-and-prestige.md).

This is what makes spell levelling pay three dividends at once — production now, mastery velocity, and
Time Advancements next run — and why a purely cheapest-first buying order underrates it.

## Buying mastery velocity: Spellcraft

**Spellcraft** is a Wizardry attribute (the Studious Casting group) that buys **+8.79 % Spell Mastery
Rate per level, ×1.04 per level** — each level slightly larger than the last. It is a direct,
purchasable knob on how fast every mastery bar fills, which makes it a run-local price for a cross-run
yield.

Spellcraft also appears in the requirement graph as a gate in its own right (`Improved Spell Weight`
was observed requiring Spellcraft level 2/5), so its levels are worth more than their percentage.

## Related pages

- [casting-and-spells.md](casting-and-spells.md) — what a cast costs and what it produces; Casting
  Level; the loadout that decides which mastery bars can fill at all.
- [glyphs-augments-recipe-books.md](glyphs-augments-recipe-books.md) — how augments change a spell's
  effective types, and therefore which type tracks a cast feeds.
- [progression-advancements.md](progression-advancements.md) — the other earned-by-doing currencies.
- [value-computation.md](value-computation.md) — rounding, modifier folding, and why a displayed cost
  is not the amount debited.
