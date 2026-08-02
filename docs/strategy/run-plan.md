# Run Plan

How to plan a run from the reset screen forward. The systems named here are documented in
[`../game-systems/`](../game-systems/README.md); this page is the route and the ordering rules.

## Start by spending the carry

**Apply carried time bonuses before the first purchase.** They multiply everything downstream,
and the opening minute's rates set the cadence for the whole early game. A run started before
the carry is applied is measurably behind for no reason.

The reset screen publishes exactly three decision facts — starting Time Advancements, the delta
against the previous run, and what survives — so the shape of the carry is known before you
commit to anything. See
[`../game-systems/time-and-prestige.md`](../game-systems/time-and-prestige.md).

## The early chain, as a reference walk

The critical path is deterministic: required rolls are rigged to a single option, so the spine
is the same every run and only optional pool picks are genuinely open. **Treat this as a route,
not a script** — prices move with the carried bonuses.

1. **Mana only.** Infuse the orb repeatedly. The cost doubles against a fixed capacity until it
   cannot be paid — that is the game forcing the next step, not a wall you should try to beat.
2. **First time rune** (always the Discovery-rarity one) — this opens the cross-run layer.
3. **First glyph** — the first pool unlocker; option space, not rate.
4. **Spellcraft** → first spell roll → the first cast-driven resource. Casting is now a
   production engine, not a button.
5. **First attributes tab** → a capacity attribute. This is the detour that un-gates the
   infusion ladder.
6. **Second glyph → a charm → the loadout upgrade** (spots plus a weight budget).
7. **Spell advancement** → spell levels, which raise both output per cast and XP per cast.
8. **Novice Spells** — the first gate you can neither buy nor save for: it wants spell mastery,
   and only casting produces mastery.
9. **Second produced resource** → the augment table and augments → the global output level dial
   → more spell spots.
10. **Scholarism** → the third resource, on a trickle-rate economy → research, via Innovation.
11. **Storm and the first channelled spell** → orb research → **Concepts**.

Why in this order: each step's currency is the next step's price. Skipping ahead means saving in
a currency you cannot yet produce.

## Capacity gates force cross-currency detours — plan them

**When a price exceeds capacity, time-to-afford is infinite: the purchase is gated, not slow.**
The affordable set is bounded by capacity, not by stock, so a plan that only tracks stock will
chase an unreachable price for an entire interval.

The resolution is always the same multi-hop detour, and it runs several times in the first hour:

> unlock the pool → take the spell → produce the new currency → buy the capacity attribute →
> the original purchase becomes reachable.

**When a goal is cap-gated, make "raise the cap" the goal** and price it in whatever currency
the capacity attribute wants. The detour is not a distraction from the plan; it *is* the plan.
Requirement chains behave the same way — gates reference mastery levels, casting level, upgrade
levels and attribute levels all at once, so plan through the gate rather than saving into it.

## Distribute Time Advancements

Per-level costs rise inside a rune (0, 1, 2, 3, …) while returns diminish, so **equalize
marginal returns across all the runes the run gives you** — roughly seven to ten. Breadth beats
depth.

- **The first level of every rune is free, and an unlevelled rune is inert.** Take the free
  level on every rune you pick, without exception.
- **Rune mastery is the compounding part**: each mastery point is +1 starting Time Advancement
  next run. Levelling a rune you do not otherwise want can still be correct purely for mastery.
- **Only Persistent-tagged runes are cross-run capital.** Their granted levels keep applying
  even though the rune itself is cleared at reset, and repicking is only needed to buy *more*
  levels. Non-persistent runes are run-local power — value them against this run alone.
- **Time Advancements are refunded at every reset**, so there is no spend-timing risk at all.
  Deferring them is free; the only thing to get right is the split.

## Challenges set the run's risk posture

Challenges change the rules of a run — and they modify *requirements*, not only numbers, so a
node's gate can be several levels cheaper than its base text says. Several can be active at once.

**Pick the posture deliberately, then honour it.** An easy set makes it a chill run, which
legitimately shifts every fork toward permanent cross-run bonuses over run-local power; a hard
set inverts that. Why: the posture is the tie-breaker on every pool pick the run will present,
and deciding it once beats re-arguing it a dozen times under time pressure.

## Advancements are run-finite — never waste them

Attribute costs eventually blow past what a run can pay, so the advancement points a run mints
are bounded, and only a new run resets those curves. **You will never research everything**, so
every allocation is a choice against everything else you could have had.

- They arrive as capacity rather than balance, so nothing is lost at cap and deferring is free.
  There is never a reason to allocate one in a hurry.
- **Spend them on engines you are actually running.** An advancement buffing a discipline you
  have not unlocked is dead capital for this run, regardless of how large the numbers look.
- Orb levels double as unlock keys with hidden thresholds — apply the one-way-door rule from
  [principles.md](principles.md) and hold until you can read the gates.

## The run's real output is capital, not just milestones

The compounding chain is **spell level → more output and more XP per cast → mastery faster →
more Time Advancements next run**. One purchase pays three dividends across three horizons.

**When two candidates tie on time-to-milestone, take the one that also feeds a cross-run
track.** Why: the milestones reset at the next run; the capital does not.

- **Count casts, not stock, when planning through a mastery gate.** Mastery XP, the per-spell-
  type tracks and the global casting level fill only by casting — the casting level also ticks
  passively, so idle time is not wholly wasted, but a mastery gate is cleared by scheduling the
  activity, never by saving.
- **Meta-velocity is purchasable.** Attributes that raise spell mastery rate spend run-local
  currency to buy cross-run capital velocity — an unusually good trade in a run you intend to
  reset from anyway.

See [`../game-systems/mastery-and-xp.md`](../game-systems/mastery-and-xp.md).
