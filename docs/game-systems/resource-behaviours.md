# Resources with behaviours of their own

Beyond stock, rate and capacity, a resource can carry an **attached behaviour** — an extra effect
that belongs to that resource specifically and shows up in its tooltip. Observed shapes include a
natural drain, a missing-percent-driven fill, and effects on another statistic such as maximum mana.
Which resource carries what is recorded here as it is established, not inferred.

## Spark — drains toward zero

Spark is the only observed resource that decays toward **zero** rather than toward its cap, and it
does so even while below capacity. Its gain scales inversely with how much you are holding — more
when low, less when high — so its equilibrium sits at zero. It does not decay while its channel is
active. No formula has been extracted; see [open-questions.md](open-questions.md).

## Arcanum — fills on missing percent

Arcanum carries a similar attached behaviour driven by **missing %** (see
[growth-levers.md](growth-levers.md) for the mechanic). Its numbers are unextracted; see
[open-questions.md](open-questions.md).
