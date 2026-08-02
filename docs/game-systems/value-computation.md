# Value computation

Almost every number in Orb of Creation — a rate, a cost, a capacity, a cooldown, a spell's
power — is a base value with a pile of modifiers folded onto it. The same folding machinery
runs everywhere, so once you can read one number you can read all of them.

Two things make the result surprising if you assume ordinary arithmetic: the order in which
modifiers fold is fixed and non-commutative, and the game only works a number out when
something asks for it.

## The game computes lazily

A value is recalculated when the game looks at it, not when its inputs change. In practice:

- **Displays can lag.** A number that nothing is currently reading keeps the answer it last
  worked out. Opening the screen or hovering the thing is what forces the recalculation.
- **Tooltips freeze while open.** A tooltip caches its content when it is built; a value that
  keeps changing underneath will not update inside an already-open tooltip until it is
  rebuilt (close and re-hover).
- **Some screens are the only thing that advances their own data.** Agromancy is the known
  case: harvest state refreshes while the Agromancy page is open and goes quiet otherwise.
  A plot action whose prerequisites became satisfied while the page was closed stays
  invisible until you open the page — and once opened it stays unlocked permanently.
- **Numbers settle just after a load.** Immediately after a save is loaded, derived values
  have not been worked out yet; they resolve as things read them over the first moments of
  play.

None of this loses progress — production, queues and timers all run on their own clock. It
only affects what a display is showing you at a given instant.

## The five kinds of modifier

Every modifier is one of five kinds. The game does not name them in tooltips, but which kind
you are looking at completely changes the arithmetic — two "+12%" bonuses are +24% under one
kind and ×1.2544 under another — so the internal names are the only way to talk about them
precisely.

| Kind | What it does to the current value `v` | How several of the same kind combine |
|---|---|---|
| Raw | `v + a` | adjustments add |
| MultiDiminishing | `v × (1 + a)` | adjustments **add**, then one multiply |
| MultiStacking | `v × f` | factors **multiply** |
| Reduction | `v / (1 + a)` | adjustments add into one denominator |
| Exponent | `v ^ e` (an exponent below 1 is applied as its inverse) | exponents multiply |

MultiDiminishing is the reason percentage bonuses in this game usually feel weaker than
expected: ten sources of +10% are +100%, not ×2.59. MultiStacking is the compounding kind,
and it is comparatively rare — the per-level cost growth on Attributes is one of the few
places it appears (a ×1.25 factor per level, which is exactly why Attribute prices run away).

### Fold order

Modifiers also carry an order number. The game folds **within one order in kind sequence**:

```
Raw → MultiDiminishing → MultiStacking → Reduction → Exponent
```

and then folds **orders ascending**. This is not commutative: a Raw addition placed in a
later order lands on top of the multipliers instead of underneath them, and the two give
different results. When a tooltip shows a list of contributing effects, it shows the
contributions, not the sequence.

### Worked proof: 225 becomes 306

Investment Power tiers on a time rune read as +12% each. With three tiers earned, a base
gain of 225 previewed as **306**, on two different runes with the same rune level:

```
MultiDiminishing:  225 × (1 + 0.12 + 0.12 + 0.12) = 225 × 1.36 = 306.000
if it multiplied:  225 × 1.12 × 1.12 × 1.12       = 316.1
```

The observed number was exactly 306, so those tiers add. This is the cleanest available
demonstration that same-kind percentages add rather than compound — but it only proves it
for *that* kind. Any other stacking claim stays open until the modifier's kind is known.

## Per-level scaling varies per effect

An effect that reads "×1.074/level" is telling you how the *next* level compares to the last,
not how levels combine with each other. **Whether the levels of one effect stack additively
or multiplicatively is a property of that effect**, and both exist in the game. An additive
effect with two levels of +80 and +90 gives +170; a multiplicative one with ×2 and ×3 gives
×6. Read the scaling from the effect; never assume.

The common shape for generator Attributes is additive-on-base with a cumulative per-level
multiplier, so the total is roughly `base × (1 + m + m² + …)`. That makes the second level of
an early generator close to a doubling — measured on one aura: level 2 rate `3.17e-2` equals
level 1's `1.54e-2 × (1 + 1.05)`.

## The cost pipeline

Costs are built in stages, and the stages are not interchangeable. A spell's cost is assembled
as:

1. **Base cost** — the authored price of the spell.
2. **Glyph augmentation and conversion** — socketed augments modify or replace cost terms.
3. **Per-level scaling** — the spell's own level curve.
4. **Cost modifiers** — spell-specific, global, and spell-type modifiers, folded by the rules
   above.
5. **Percentage multiplication** — the remaining percentage terms.
6. **Rounding to two significant digits** — the displayed price.

Then, at the moment you pay, the **actual spend is the displayed cost divided by the
resource's Quality**. Quality never appears in the price you see; it appears in how much
leaves your stock.

There is no shortcut that merges modifiers from two different stages into a single bag. A
+10% at stage 4 and a +10% at stage 5 do not add, and they do not commute.

Attributes and upgrades have their own pipelines and **share no cost chain** with each other.
Attribute prices additionally pass through a per-resource Attribute Cost term (see
[resources.md](resources.md#the-growth-terms)). Costs are charged **at queue time**, and each
queued level is priced individually — so a two-level bulk purchase is not twice the displayed
price, it is the displayed price plus the next level's higher one (measured at ≈1.25–1.34× for
the second level of an Attribute).

### The number on the button is not always the number that leaves your stock

Payment works from raw quantities in the authored order of the cost rows, applies Quality, and
clamps at zero. Displayed price × quantity is a good estimate and a bad prediction. If you are
reconciling stock against a price, reconcile against what actually moved.

## Rounding to two significant digits

Prices are rounded to two significant digits, but **the two rounding rules in the game are not
the same rule**:

- **Attributes** use an early-rounding form that only alters values in the range `[10, 100)`.
  Outside that window an Attribute price is whatever the pipeline produced, unrounded.
- **Upgrades** use the full form, which snaps at every magnitude. An upgrade price is always
  two significant digits.

Discovery pool prices and spell level costs also snap to two significant digits, which is why
ladders read as clean numbers like 90 / 900 / 9,000 (see [discovery.md](discovery.md)).

## Big numbers

Numbers are stored as a **mantissa and an exponent** — the `4.52e3` form the game displays.
That representation holds enormous values comfortably but only keeps a fixed number of
significant digits.

The consequence worth knowing: **spending your entire stock can round the remainder to
literal zero.** If a purchase price is many orders of magnitude below your holdings, the
subtraction has no digits left to record the leftover, and the remainder collapses to 0
instead of a small number. Where this matters, the Reverb Rate and Replenish Ratio growth
terms exist as protection against it (see [resources.md](resources.md#the-growth-terms));
which of those two does what is not established.

## Where the same machinery shows up

The folding rules are global, so the same five kinds and the same order explain:

- resource Rate, Gained, Capacity and Quality — [resources.md](resources.md)
- spell power, cooldown and cost — [casting-and-spells.md](casting-and-spells.md)
- pool roll price ladders — [discovery.md](discovery.md)
- advancement capacities and research effects —
  [progression-advancements.md](progression-advancements.md)
- rune tiers and cross-run bonuses — [time-and-prestige.md](time-and-prestige.md)

Known gaps are collected in [open-questions.md](open-questions.md).
