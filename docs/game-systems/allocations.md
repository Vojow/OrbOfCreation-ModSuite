# Allocations displayed as resources

Some things the game models as resources are budgets, not stocks. Advancement currencies display as
`quantity / cap` where the **cap is what you have earned** and the **quantity is what is still
unallocated**: `0/2` means you earned two and spent both.

A progression level raises the cap rather than granting a unit, so an advancement sitting at `2/2`
becomes `2/3`. Nothing is ever wasted by being "full", and there is no urgency to spend before the
next level lands. Allocations are permanent within a run: no refund, no respec, and no moving a point
from one node to another.

The same shape covers the game's capacity and bandwidth budgets — Spell Capacity, plot capacity,
Alchemical Capacity, advancement points and others; the data holds 25 of them. They are allocated and
reallocated rather than produced and consumed.
