# Scoring moves

The deliverable is a ranked top three of the most valuable moves available right now, each scored by
its impact on time-to-goal, each with a one-line why.

## The protocol

Rate options **−10 to +10**: 0 = doesn't matter, negative = would actively slow the run, +10 = the
single best move available in the game right now.

- **Score against the active goal's clock**, not against the option's own numbers. A large buff that
  does not shorten the current milestone is not a large score.
- **Score reservations, not only purchases.** "Hold 30 of X and 10 of Y for Z" is a valid and
  frequently correct entry.
- **"Defer" and "buy nothing" are legitimate entries.** The exception is a roll already paid for: an
  open choice blocks the required-roll queue, so it must be closed now.
- **Relitigate.** Ratings are relative to the current state: a purchase moves from +3 to +8 when its
  cost pools hit capacity, without changing at all. Re-score at cap crossings, at portfolio changes,
  and whenever the binding constraint flips.
- **Say what you don't know.** Where magnitude is hidden by design, score the visible fraction *as*
  the visible fraction and label it. A confident number over an unknown is worse than no number.

## Two operating modes — name which one you are in

**Right now**: the single best action at this exact state, at real scarcity, with the current pools
and cooldowns. Comparable across options and reproducible.

**Generally**: what the run is aiming for over the next several milestones — the route, the
reservations, the posture. This mode carries priors the state cannot supply, and it is the mode in
which those priors are legitimate.

Mixing them produces advice that is neither: a "right now" score contaminated by a preference, or a
route argued from one instant's pools. Label the mode whenever it could be ambiguous.

Deliberation time inflates stocks, so a recommendation validated during a leisurely walkthrough may
fail in the scarce regime it is meant for. **Validate against scarcity.**
