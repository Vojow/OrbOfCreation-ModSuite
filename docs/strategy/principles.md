# Principles

The decision rules that survive a change of era. Everything on the other three pages is an
application of one of these. Mechanics live in [`../game-systems/`](../game-systems/README.md).

## Hidden-magnitude one-way doors

**When a decision is irreversible and its true value is hidden by design, buy information or
defer. Never score off the visible fraction.** The visible part is not a sample of the whole —
it is the part the game chose to show you.

Orb allocations are the canonical case. Each level advertises a small buff, but the dominant
value is the gates it opens later ("available only at 12 levels of X"), and those thresholds
are not displayed. A confident +8 read off the visible buffs is really a +0.8 against what you
cannot see. Hold the orbs, read the gate tables out of the game's own data, then allocate.

**Free and non-scaling bonus levels carry extra option value — spend them last.** They do not
advance the paid cost curve (level 2 plus two bonus levels costs exactly what level 2 alone
costs), so holding one is free and spending one is irreversible. Spend a bonus level only
against a gate you can actually see.

See [`../game-systems/progression-advancements.md`](../game-systems/progression-advancements.md).

## Advice is shaped like a reservation

**Good advice is "hold 30 Knowledge and 10 Thaumaturgy for Bandwidth", not "buy the cheapest
affordable thing".** A ranking over currently-affordable options cannot express *don't spend
this* — and *don't spend this* is what carries a run to its next milestone.

The one exception is a pay-for-itself buy: break a reservation for a cheap production purchase
only when its boost shortens the clock **to that same milestone**, not to some other one.

## The watermark problem

**Cheapest-first never crosses a price watermark while cheap dribbles exist.** Early on,
attributes cost around 3 Knowledge, a spell level 10, the next system unlock 15. A greedy
policy buys attribute levels until they cost more than 15 before the unlock ever ranks — and by
then the run is behind on every track the unlock feeds.

Rank by effect on time-to-goal and let the goal's price set the watermark. Anything priced
above the cheap frontier must be evaluated, never filtered out for being unaffordable *now*.

## Find the binding constraint

**Name what actually limits the loop before valuing anything.** Filling 100 mana at 27/s takes
3.6 s against a 7.35 s cooldown: that loop is cooldown-bound, so extra mana rate has *zero*
marginal value at that instant, however good the tooltip looks.

Efficiency multipliers (charms, quality, cost reductions) win while the shared input binds.
Throughput adders (an extra spell spot, a duplicate copy, a shorter cooldown) win when
slots or cooldowns bind. **The answer flips as rates grow**, so re-check it inside an era, not
only at milestones. See
[`../game-systems/casting-and-spells.md`](../game-systems/casting-and-spells.md).

## Commitment points

**Paying the roll is the commitment — the pick is already spent.** Once rolled you must choose,
and an open choice blocks the required-roll queue, so the critical path stalls until you close
it. Deferral is only free *before* paying.

- Defer the payment, never the pick. "Buy nothing this turn" is a real move; "leave it open" is
  not.
- **Delay optional pool draws until pool-unlocking purchases have landed.** A richer pool is a
  strictly better roll at the same price, and prices climb per tree with every optional pick.
- Land recipe books, then roll. Not the other way round.

See [`../game-systems/discovery.md`](../game-systems/discovery.md).

## Relitigate on every state change

**Scores are state-relative.** One purchase moved from +3 to +8 without changing at all: both
its cost pools reached capacity, so paying became free in time terms, and the spell spot it
granted meant a second copy of the workhorse spell on its own cooldown — roughly halving the
time to the next milestone.

Re-score when a cost pool hits cap, when the binding constraint flips, and when the portfolio
changes. A buff on a category is worth what the category currently contains: a Divining buff
doubles its coverage the instant you own a second Divining spell.

## At cap, spending is free

Stock at capacity with positive income means income is already overflowing, so anything paid
out of it costs **zero time**. This is the cheapest possible moment for an expensive purchase.

Two corollaries: **never generate into a capped resource** (that production is discarded), and
**never spend a stock to exactly zero** — large-number rounding can zero the remainder instead
of leaving a small value. Leave an epsilon. See
[`../game-systems/value-computation.md`](../game-systems/value-computation.md).

## Buy is not equip

**Acquiring a spell and giving it a loadout spot are separate decisions against separate
budgets** — spots and weight bind independently, and a weight-0 spell still consumes a spot.
Buy now, bench it, socket it when weight capacity arrives or a bench trigger fires. Why:
acquisition windows close (pool prices climb, pools shift); loadout spots do not.

## More base beats a multiplier on a small base

Under real scarcity, buy rate and flat base additions before percentage multipliers. Base
additions multiply through the entire modifier stack, so face-value ranking systematically
undervalues them — while a percentage of a tiny base stays tiny. The multiplier's turn comes
when several sources feed the term it targets.

## Cheap information compounds

**Purchases that only reveal state are near-automatic buys.** They cost a few units of a
commodity and improve every decision that follows for the rest of the run. The same class:
unlock purchases for a system you are going to use anyway — they are milestones wearing a
purchase's clothes.

## Preferences are legitimate inputs

A run whose challenges were deliberately picked easy is a chill run, and that legitimately
shifts every fork toward permanent cross-run bonuses and away from run-local power. A hard run
inverts it. Neither run intent nor a taste for long-term bonuses appears anywhere in game data.

State the posture once, up front, as a standing prior. Why: it is the tie-breaker on every pool
pick the run will present, and deciding it once beats re-arguing it a dozen times.
