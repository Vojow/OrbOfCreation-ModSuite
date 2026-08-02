# Attributes, upgrades and the development queue

[Back to game systems](README.md)

Attributes and upgrades are the game's main purchase surface: the rows with a cost line and a buy
button that make numbers go up. Everything on this page is about what you actually buy, what it
costs, and what happens between pressing the button and the thing existing.

## Read this first: the vocabulary trap

The word **attribute** means two different things depending on where you read it.

| On screen | Internally | Purchasable? |
|---|---|---|
| **Attribute** | a *structure* | yes — this is the thing with levels and a cost line |
| **Statistic** | an *attribute* | **never** |
| Attribute category (Container, Aura, Talent, …) | a structure *type* | no — it is a grouping |
| Upgrade | an upgrade | yes |
| Research | research | yes, in advancement points |
| Resource | a resource | not applicable |

So: the levelled, priced things in the Wizardry / Scholar / Alchemist lists are **Attributes** to
the player and *structures* to the game. The thing the game's own code calls an attribute is
surfaced to the player as a **Statistic** — Cost Scaling, Effect Levels, Spell Mastery Rate and
friends. Statistics are never bought directly; they are moved by attributes, upgrades, research and
effects.

The internal names matter only because tooltips, effect text and requirement text occasionally
leak them. Everywhere else, use the on-screen words.

## Names and types are both targetable keywords

An attribute carries its own name *and* a type, and **both are things effects can target**.

Observed attribute types include **Container** ("attributes that hold resources"), **Machine**
("large mechanical objects"), **Tool**, **Aura**, **Talent**, **Purity**, **Radiant**, **Innate**
and **Infusion**. An effect reading "+X% Aura Effect Levels" hits every Aura you own, present and
future; an effect naming a single attribute hits only that one.

This is the same effect grammar the rest of the game uses — `<term> <statistic> <target>`, where
the target may be one entity, a category keyword, or something as broad as "all". See
[value-computation.md](value-computation.md).

Tooltips nest: you can inspect a term inside a tooltip and get that term's own tooltip, which is
how you find out that Container is a type and that Effect Levels and Cost Scaling are statistics
attached to that type rather than to any one attribute.

## Per-level scaling: read it, never assume it

Attribute effects are shown with a per-level scaling factor, for example `×1.074/level`: each level
gives slightly more than the one before.

**Whether levels stack additively or multiplicatively varies per effect.** Two levels of an
additive effect worth +80 and +90 give +170; two levels of a multiplicative effect worth ×2 and ×3
give ×6. Nothing in the display tells you which one you are looking at — you have to read the
effect. Do not generalise from one attribute to another.

### Generator stacking

For the common case — an attribute that adds a flat rate, with a cumulative per-level multiplier —
the total is:

```
total ≈ base × (1 + m + m² + m³ + …)
```

where `m` is the per-level factor. With `m = 1.05`, buying the second level does not add 5%; it
roughly **doubles** output, because the second level contributes its own full base on top of the
first. Observed directly: an Aura at level 2 produced `3.17e-2` against `1.54e-2` at level 1,
exactly `1.54e-2 × (1 + 1.05)`.

Early generator levels are therefore approximately doublings, and the per-level factor only starts
to look like a small percentage much later, once the sum has many terms.

## The development queue

Buying an attribute or an upgrade does not give it to you. It **queues** it, and it develops over
time.

- **Attributes develop slowly. Upgrades develop more slowly still.** They share the **same**
  queue — a slow upgrade occupies a slot an attribute could have used.
- Default capacity is **8** slots. Purchases raise it: *Improved Development* takes it from 8 to 10
  and adds +10% development speed; *Greater Mental Acuity* adds +2. Late-game capacity has been
  observed as high as **304**.
- Both capacity and development *speed* are upgradeable, and they are separate knobs.
- Early on, the queue is a real limiter. Even when a resource is abundant, the order in which you
  spend it matters, because the queue — not the resource — is what binds.

Two mechanical details worth knowing:

- **Queue occupancy counts stack units, not distinct entries.** One frame held 35 distinct queued
  objects but **131** queued stack units, because a multi-level purchase occupies multiple units.
- **Bulk Development** is a live game value (observed as `2` on one save and `16` on another).
  Attributes request that many levels per purchase; **upgrades are always one level per action**.
  It is raised mostly by **research**, and research moves two separate queue knobs that are easy to
  conflate: some nodes add **parallel development slots** (more different purchases developing at
  once), while others raise **bulk** (queued levels of the *same* attribute processed together in
  one slot's pass). Sources outside research may exist.

## Entities can change category mid-line

Some purchases start life as an Upgrade and become an Attribute once they run past their upgrade
levels. *Infuse Orb* is the canonical case: it is an upgrade for its first few levels, then
transitions into a structure with infinite levels — **and scales more slowly** as a structure than
it did as an upgrade. The `Infuse <resource>` family generally behaves this way.

This matters for two reasons: the same thing appears on two different kinds of list over its
lifetime, and its cost curve visibly changes shape at the boundary.

Most upgrades are the opposite — genuinely one-shot. On one mid/late save, **207 of 222 upgrades
had a maximum level of 1 and were already bought out**. Treat an upgrade as a tiny milestone: it
either unlocks something or makes something stronger, once.

## Prices are mixed-currency by default

Two-currency prices are the norm, not an exception. Measured across 409 purchasable entities on one
mid/late save:

| Price shape | Count | Share |
|---|---|---|
| Two currencies | 335 | **82%** |
| One currency | 74 | 18% |
| Three or more | 0 | 0% |

The two currencies in a single price are usually nowhere near each other in magnitude: the median
ratio between the two halves of one price was **≈783×**, with a p90 around `3e22`. **Comparing cost
magnitudes across currencies is meaningless.** The only comparable quantity is time-to-produce.

### The per-level cost multiplier

From 342 sampled two-level price groups on that save, the factor by which each level's cost exceeds
the previous:

| Statistic | Value |
|---|---|
| Minimum | `0.970` |
| p25 | `1.069` |
| **Median** | **`1.244`** |
| p75 | `1.273` |
| Maximum | `1.358` |

The dominant multiplier is therefore **≈×1.25**, with a visible cluster at ×1.30 and a real tail
*below* 1.0 — a handful of entities actually get **cheaper** per level.

Costs also span an enormous range within one save — roughly `10⁻¹` through `10⁶⁰`, peaking around
`10²`–`10⁴` — and the authored mantissas are strongly clustered on round numbers (1.0, 2.0, 5.0,
1.5, 4.0, 2.5).

## What the buy button actually checks

This is the most commonly mis-modelled part of the purchase surface, and Attributes and Upgrades
are **not symmetric**.

**For an Attribute**, the purchasability check tests exactly two things:

1. level requirements are met, and
2. there is room in the development queue.

It does **not** test the price, and it does **not** test whether the attribute is currently
available. Affordability is enforced later, inside the purchase itself, per queued level.

The consequence is a genuine trap: **triggering a purchase you cannot afford silently does
nothing.** No error, no queue entry, no message. The row simply does not advance.

**For an Upgrade**, the equivalent check is broader — it covers the maximum queued level, the cost,
the upgrade's own availability, and queue room. So the two families fail in different places for
the same user action.

Separately, the **buy button drawn in the list** does its own check — the per-level prerequisite,
affordability, and queue room — which is why an unaffordable row renders its **cost line in red**
rather than firing. The red line is the UI's own preflight, not the underlying rule.

There is one more silent-failure path worth naming: an upgrade whose reward is to add something to
a capacity-limited list (a world aspect slot, for instance) can be **paid for and completed while
the addition never happens**, because the add is dropped when there is no empty slot.

## Payment happens at queue time

You pay when the purchase enters the queue, not when it completes, and the game prices the levels
it is queueing at that moment.

That has three consequences:

- Queueing a lot at once spends a lot at once. The resource leaves your pool immediately.
- With Bulk Development above 1, an attribute purchase prices **each level individually** and
  charges the sum. With the second level at **1.25–1.34×** the first, a two-level group costs
  ≈**2.25–2.34×** the displayed next-level price. Observed on one save: a level-102 attribute
  showing `4.4671566822164e12` for one level charged `1.01276638899877e13` for two.
- A purchase whose group total you cannot afford does not partially fire.

Actual spend also divides the displayed cost by that resource's **quality**, so a high-quality
resource pays less than the sticker price. See [resources.md](resources.md).

## The game's own auto-buy

The game ships a native auto-purchase, and its trigger is deliberately narrow. It fires only
when three things hold at once: the development queue is **empty**, a **five-second timer** has
elapsed, and the cost is **trivial — under 0.1 % of your current stock** of the pricing
resource. Anything costing a meaningful fraction of stock is never bought automatically. That
threshold is why the feature seems to act only on purchases your economy has absurdly outgrown:
it is designed to sweep up exactly those and nothing else.

## Cost Scaling is retroactive

**Cost Scaling** is a statistic attached to a typed group — it is the per-level cost growth for
everything in that group. Its tooltip states plainly: *"This statistic is retroactive."* Reducing
cost scaling does not only make future levels cheaper; it re-prices the curve you are already on.

The same retroactivity shows up elsewhere in the purchase surface. *Thaumic Wisdom*, for example,
adds +5 base Thaumaturgy capacity per Wisdom level and applies it **across all existing Wisdom
levels** the moment it completes.

One more code-level detail with a player-visible face: an attribute flagged as disabled has its
**effect** killed, not its purchasability — it can still be bought while contributing nothing.

## What the late-game purchase pages look like

Purchase lists carry a **progression counter** in the header showing that tab's level against its
next threshold. Observed on one deep save: Wizardry `160/420`, Scholar `82/320`, Alchemist
`20/260`, Mysticism `40/110`. Those counters are the tab XP tracks described in
[progression-advancements.md](progression-advancements.md).

**Mysticism additionally carries a decaying Stability meter** — observed at `4.73e3` falling at
`−0.376%/s`. No other purchase page seen so far has one. Stability is the **ritual system's health
pool**: rituals are the game's combat layer, fought in waves of enemies, and their attacks drain
Stability while a ritual runs. The ritual system itself is deliberately unmapped — see
[open-questions.md](open-questions.md).

Finally, a scale fact that explains why these pages feel sparse: on one mid/late save with 409
purchasable entities (180 attributes + 229 upgrades), **224 were unavailable and roughly 181–184
were unaffordable at any given instant, leaving 0–3 actually actionable**. The buy surface is
enormous; the frontier at any moment is tiny.

## Related pages

- [progression-advancements.md](progression-advancements.md) — what completing a level pays you.
- [value-computation.md](value-computation.md) — modifier folding, rounding, and caching.
- [resources.md](resources.md) — quality, capacity, and what the currencies do.
- [ui-map.md](ui-map.md) — where the purchase lists live.
- [open-questions.md](open-questions.md) — the Stability meter and other unknowns.
