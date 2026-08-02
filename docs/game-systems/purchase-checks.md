# What the buy button checks

Attributes and Upgrades are **not symmetric**, and they fail in different places for the same user
action.

**For an Attribute**, the purchasability check tests exactly two things: level requirements are met,
and there is room in the development queue. It does **not** test the price and does **not** test
whether the attribute is currently available. Affordability is enforced later, inside the purchase
itself, per queued level.

The consequence is a genuine trap: **triggering a purchase you cannot afford silently does nothing.**
No error, no queue entry, no message — the row simply does not advance.

**For an Upgrade**, the equivalent check is broader: maximum queued level, the cost, the upgrade's
own availability, and queue room.

## The red cost line is the interface's own preflight

The buy button drawn in the list runs its own check — the per-level prerequisite, affordability and
queue room — which is why an unaffordable row renders its **cost line in red** rather than firing.
Red is that preflight, not the underlying rule.

## Payment happens at queue time

You pay when the purchase enters the queue, not when it completes, and the game prices the levels it
is queueing at that moment. So queueing a lot at once spends a lot at once, and a purchase whose
group total you cannot afford does not partially fire.

With Bulk Development above 1, each level is priced individually and the sum is charged: at
≈1.25–1.34× per level, a two-level group costs ≈2.25–2.34× the displayed next-level price.

## Another silent-failure path

An upgrade whose reward is to add something to a capacity-limited list can be **paid for and
completed while the addition never happens**, because the add is dropped when there is no empty slot.
