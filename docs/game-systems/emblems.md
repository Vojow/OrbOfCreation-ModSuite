# Emblems

Emblems are passive token effects, counted like resources but not resources. The data holds 24 emblem
passives; they do not all share one shape, and only Momentum has been worked out.

## Momentum, worked

Momentum holds up to **ten tokens**, with a four-second duration before scaling. Each effective whole
stack contributes **+8 % build speed (additive), ×1.08 Cantrip cooldown speed and ×1.04 Cantrip
power**.

The stack runs **one shared countdown**: adding a token does **not** refresh it, and when the
countdown elapses a **single token** expires, after which the next token takes the following
interval. Effects change only as whole token boundaries are crossed, so fractional stacks contribute
nothing.

E.g., one observed source: casting Kinetic Mind adds one Momentum token and causes a Mental splash.
