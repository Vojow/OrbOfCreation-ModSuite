# Advisor

Strategy ships as advice before it ships as automation. This page is the contract for the
advice, the protocol for scoring it, and the tests any automation must pass before it is
allowed to act on it.

## Advice first

**The deliverable is a ranked top three of the most valuable moves available right now** —
each scored by its impact on time-to-goal, each with a one-line why.

Why advice first: advice can be wrong cheaply, and a wrong recommendation is visible and
arguable before anything is spent. An actuator that is wrong spends the run's scarce pools
before anyone notices, and the pools it spends are usually the reserved ones.

## The scoring protocol

Rate options **−10 to +10**: 0 = doesn't matter, negative = would actively slow the run, +10 =
the single best move available in the game right now. Every rating carries a short why.

- **Score against the active goal's clock**, not against the option's own numbers. A large buff
  that does not shorten the current milestone is not a large score.
- **Score reservations, not only purchases.** "Hold 30 of X and 10 of Y for Z" is a valid — and
  frequently the correct — top-three entry.
- **"Defer" and "buy nothing" are legitimate entries.** They have been the right call more than
  once. The exception is a roll already paid for: an open choice blocks the required-roll queue,
  so it must be closed now.
- **Relitigate.** Ratings are relative to the current state: a purchase moves from +3 to +8 when
  its cost pools hit capacity, without changing at all. Re-score at cap crossings, portfolio
  changes, and whenever the binding constraint flips.
- **Say what you don't know.** Where magnitude is hidden by design, score the visible fraction
  *as* the visible fraction and label it. A confident number over an unknown is worse than no
  number.

## Two operating modes — name which one you are in

**Right now**: what is the single best action at this exact state, at real scarcity, with the
current pools and cooldowns. Comparable across options, reproducible, and the mode that
acceptance tests apply to.

**Generally**: what is this run aiming for over the next several milestones — the route, the
reservations, the posture. This mode carries priors (run intent, preferences) that the state
cannot supply, and it is the mode in which those priors are legitimate.

Mixing them produces advice that is neither: a "right now" score contaminated by a preference,
or a route argued from one instant's pools. Label the mode whenever it could be ambiguous.

One caveat that has bitten before: deliberation time inflates stocks. A recommendation validated
during a leisurely walkthrough may fail in the scarce regime it is meant for. **Validate against
scarcity, not against a state that accumulated while you were thinking.**

## Actuators graduate one at a time

Move a lever from advice to automation only after its advice has been right repeatedly, and only
one lever at a time — so that a regression is attributable to something.

**The first continuous, reversible actuator is the global output level dial.** It is a single
dial for all casting: higher output means more per cast at worse resource efficiency and longer
cooldowns. It is free to change in either direction at any time, which is exactly what makes it
safe to automate first. The policy follows the binding constraint: **tune it down when the
shared input binds** (save resources), **up when the goal or the queue binds** (take the juice).
Mechanics in [`../game-systems/casting-and-spells.md`](../game-systems/casting-and-spells.md).

Irreversible levers — advancement allocations, pool picks, glyph upgrades — do not graduate.
They stay advice permanently, because their failure mode is permanent too.

## Acceptance tests

Every automated behaviour passes these before it touches a run. Each is named after a failure
observed in real play, not a hypothetical.

- **Watermark blindness.** With cheap dribbles available and a milestone priced above them, does
  the policy ever save for the milestone? A cheapest-first ranker fails this by construction.
- **Milestone starvation.** Does it spend the frontier currency on small percentage attributes
  while the milestone that currency was reserved for stays unaffordable? The pay-for-itself
  exception is allowed only when the boost shortens the clock to *that same* milestone.
- **Full-throttle starvation across activities.** With one activity running flat out, can every
  other consumer of the shared pool still pay? A caster that drains mana to the floor must not
  be able to block mana-priced purchases indefinitely.
- **Cap blindness.** Does it generate into a full sink, or keep running a generator whose output
  resource is already at capacity?
- **Drain blindness.** For sustained-cost activities, does it check the per-second drain rather
  than only the entry cost? Affording the cast but not the drain wastes the cast.
- **Flat cast valuation.** Does it account for casts feeding the mastery and casting-level tracks
  that gate milestones and mint cross-run capital — or does it value a cast only by the resource
  it returns?
- **Yield-blind spending.** Does it spend a stock whose holding effect was load-bearing?
- **Queue serialization.** Does it respect that development is serialized, and leave headroom
  rather than filling the queue to the brim?

## Read the game's own state

**Game state is the single source of truth for every decision** — prices, requirements,
capacities, effective modifier values. Anywhere a decision needs something the state does not
publish, that is a gap to close in the state, not a special case to hard-code.

Two things this buys, and one trap:

- **The requirement graph is fully derivable, so chain-planning is mechanical**: "this needs 12
  of that — 11 with the active challenge's requirement adjustment — and I hold 10, so buy that
  first." Model hard and soft requirements both; they behave differently, and a chain can run
  four or five hops deep through hidden nodes before it reaches something purchasable.
- **The contents of unlocks and pools are knowable before purchase.** Rating an unlock blind is
  a constraint of reading the interface, not a constraint of the information. Read the data
  instead of guessing.
- **The trap: requirement checks read purchased levels, not the summed display value.** A
  visible "+5" bonus can leave you failing a "≥ 5" gate, because the bonus levels are not the
  term the gate evaluates. Never plan a gate against the number on screen.

Where the data genuinely does not say — hidden gate thresholds behind irreversible allocations
— that is the one-way-door case from [principles.md](principles.md): buy information, or defer.
