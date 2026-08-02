# Allocations displayed as resources

Advancement points and their kin play as **a number you gain and spend**: a tab levels, you get a
point, you put it into a node. Two things stand out at the counter, though: it reads like a stock
with a cap (`2/3`), yet sitting "full" never wastes anything, and `0/2` shows you empty while you
are in fact fully invested.

**Code shape:** these are budgets, not stocks. The cap is the total you have earned, the quantity is
what is still unallocated, and a level-up raises the cap rather than minting a unit — `2/2` becomes
`2/3`. The same shape covers the game's other budgets — Spell Capacity, plot capacity, Alchemical
Capacity and more, 25 in all. They are allocated and reallocated, never produced and consumed.

Whichever shape you read, advancement allocations are permanent within a run: no refund, no respec,
and no moving a point from one node to another.
