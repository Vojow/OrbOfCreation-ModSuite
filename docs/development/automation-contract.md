# Automation contract

What an automated behaviour must satisfy before it is allowed to act on a run. The play advice it
implements lives in [../strategy/](../strategy/README.md); the mechanics it reasons over live in
[../game-systems/](../game-systems/README.md).

## Actuators graduate one at a time

Move a lever from advice to automation only after its advice has been right repeatedly, and only one
lever at a time, so that a regression is attributable to something.

**The first continuous, reversible actuator is the global output level dial.** It is free to change
in either direction at any time, which is exactly what makes it safe to automate first. The policy
follows the binding constraint: tune it **down** when the shared input binds, **up** when the goal or
the queue binds.

Its sibling, the **Reserve dial**, trades active-cast cost for passive generation. It sits maxed
while Output does the continuous tuning: Reserve's resource-rate multipliers work all the time while
its cost penalty only bites active casts, so Reserve moves only when the casting economy itself
changes shape.

**Irreversible levers do not graduate.** Advancement allocations, pool picks and glyph upgrades stay
advice permanently, because their failure mode is permanent too.

## Acceptance tests

Every automated behaviour passes these before it touches a run. Each is named after a failure
observed in real play, not a hypothetical.

- **Watermark blindness.** With cheap dribbles available and a milestone priced above them, does the
  policy ever save for the milestone? A cheapest-first ranker fails this by construction.
- **Milestone starvation.** Does it spend the frontier currency on small percentage attributes while
  the milestone that currency was reserved for stays unaffordable? The pay-for-itself exception is
  allowed only when the boost shortens the clock to *that same* milestone.
- **Full-throttle starvation across activities.** With one activity running flat out, can every other
  consumer of the shared pool still pay?
- **Cap blindness.** Does it generate into a full sink, or keep running a generator whose output
  resource is already at capacity?
- **Drain blindness.** For sustained-cost activities, does it check the per-second drain rather than
  only the entry cost?
- **Flat cast valuation.** Does it account for casts feeding the mastery and casting-level tracks
  that gate milestones and mint cross-run capital, or does it value a cast only by the resource it
  returns?
- **Yield-blind spending.** Does it spend a stock whose holding effect was load-bearing?
- **Resting-rate suppression.** Does a standing dribble of automatic activity keep a resting-rate
  resource permanently un-rested?
- **Queue serialization.** Does it respect that development is serialized, and leave headroom rather
  than filling the queue to the brim?

## Game state is the single source of truth

Every decision reads prices, requirements, capacities and effective modifier values from game state.
**Anywhere a decision needs something the state does not publish, that is a gap to close in the
state, not a special case to hard-code.**

- **The requirement graph is fully derivable, so chain-planning is mechanical.** Model hard and soft
  requirements both; they behave differently, and a chain can run four or five hops deep through
  hidden nodes before it reaches something purchasable.
- **The contents of unlocks and pools are knowable before purchase.** Rating an unlock blind is a
  constraint of reading the interface, not a constraint of the information.
- **Requirement checks read purchased levels, not the summed display value**, so a visible bonus can
  leave a gate unsatisfied. Never plan a gate against the number on screen.

Where the data genuinely does not say — hidden gate thresholds behind irreversible allocations — that
is the one-way-door case: buy information, or defer.
