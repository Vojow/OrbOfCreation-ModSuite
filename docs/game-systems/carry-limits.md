# Carry limits

Carry capacity is a **single global value, enforced separately for each exact item**. Every distinct
consumable independently gets the same limit. It is **not** a shared pool across a family and **not**
per level row.

Each unit you hold has a **level**. When a new unit arrives while you are at capacity, it is compared
against the **weakest unit you own**, on level alone:

| Situation | Incoming level | Outcome |
|---|---|---|
| Below capacity | any | Added to a free slot. Nothing is removed. |
| At capacity | strictly **stronger** than the weakest | The weakest is removed and the incoming one kept. |
| At capacity | **equal** to the weakest | A unit is churned out and replaced; level coverage does not improve. |
| At capacity | strictly **weaker** than the weakest | The incoming unit is **silently lost**. |
| Carry capacity `0` or below | any | **Every** gain is suppressed. |

"Weakest" means lowest level and nothing else — power and other attributes are not tie-breakers — and
ties keep whichever unit the game happens to hold first.

## Payment happens before the capacity decision

A craft **pays its cost when you submit it**, and the capacity decision happens **when it
completes**. Instant crafting and queued crafting run the exact same completion path. So:

- A craft that completes weaker than your current weakest owned unit is **paid for and then silently
  lost**.
- A queued craft that was stronger than your weakest unit at submission can become equal or weaker by
  the time it completes, because the crafts ahead of it raised your floor.

The game returns no admission result to whoever paid, and nothing in the interface announces the
loss.
